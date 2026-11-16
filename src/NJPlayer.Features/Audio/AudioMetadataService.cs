using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NJPlayer.Core.Diagnostics;
using NJPlayer.Core.Tools;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Service for extracting audio track metadata and album art using sidecar inspection,
/// embedded tag extraction via ffmpeg, and container analysis via ffprobe.
/// </summary>
public sealed class AudioMetadataService
{
    private static readonly string[] SidecarPatterns =
    {
        "cover.jpg", "cover.png", "folder.jpg", "folder.png",
        "front.jpg", "front.png", "albumart.png", "albumart.jpg", "album.jpg"
    };

    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, AudioTrackMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    public AudioMetadataService(string? customCacheDir = null)
    {
        _cacheDirectory = customCacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NJPlayer",
            "album_art");

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch (Exception ex)
        {
            LogService.LogWarning($"[AudioMetadata] Could not create album art cache directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Asynchronously extracts metadata and album art for an audio track.
    /// Never throws; falls back gracefully to file name heuristics.
    /// </summary>
    public async Task<AudioTrackMetadata> ExtractMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AudioTrackMetadata(
                filePath,
                Title: Path.GetFileNameWithoutExtension(filePath) ?? "Unknown",
                Artist: "Unknown Artist",
                Album: "Unknown Album",
                Year: null,
                Bitrate: null,
                AudioCodec: null,
                SampleRate: null,
                Channels: null,
                AlbumArtPath: null);
        }

        if (_metadataCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        try
        {
            // 1. Locate Album Art (Sidecar first, then embedded via ffmpeg)
            var albumArtPath = await ResolveAlbumArtAsync(filePath, cancellationToken).ConfigureAwait(false);

            // 2. Extract tags using ffprobe if available
            var metadata = await ExtractTagsWithFfprobeAsync(filePath, albumArtPath, cancellationToken).ConfigureAwait(false);

            _metadataCache[filePath] = metadata;
            return metadata;
        }
        catch (Exception ex)
        {
            LogService.LogWarning($"[AudioMetadata] Extraction failed for '{filePath}': {ex.Message}");
            var fallback = new AudioTrackMetadata(
                filePath,
                Title: Path.GetFileNameWithoutExtension(filePath),
                Artist: "Unknown Artist",
                Album: "Unknown Album",
                Year: null,
                Bitrate: null,
                AudioCodec: Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                SampleRate: null,
                Channels: null,
                AlbumArtPath: null);

            return fallback;
        }
    }

    /// <summary>
    /// Resolves album art for a file: checks sidecar images first, then extracts embedded art via ffmpeg.
    /// </summary>
    public async Task<string?> ResolveAlbumArtAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath)) return null;

        var dir = Path.GetDirectoryName(audioFilePath);
        var baseName = Path.GetFileNameWithoutExtension(audioFilePath);

        // A. Check for matching track sidecar (e.g. song.jpg, song.png)
        if (!string.IsNullOrEmpty(dir))
        {
            var trackJpg = Path.Combine(dir, $"{baseName}.jpg");
            if (File.Exists(trackJpg)) return trackJpg;

            var trackPng = Path.Combine(dir, $"{baseName}.png");
            if (File.Exists(trackPng)) return trackPng;

            // B. Check common album cover sidecars in the folder
            foreach (var pattern in SidecarPatterns)
            {
                var sidecar = Path.Combine(dir, pattern);
                if (File.Exists(sidecar))
                {
                    return sidecar;
                }
            }
        }

        // C. Check cache for previously extracted embedded art
        var fileInfo = new FileInfo(audioFilePath);
        var cacheKey = ComputeHash($"{audioFilePath}_{fileInfo.LastWriteTimeUtc.Ticks}");
        var cachedArtPath = Path.Combine(_cacheDirectory, $"{cacheKey}.jpg");

        if (File.Exists(cachedArtPath) && new FileInfo(cachedArtPath).Length > 0)
        {
            return cachedArtPath;
        }

        // D. Extract embedded front cover art using ffmpeg
        var ffmpegResult = ExternalToolLocator.Locate("ffmpeg.exe");
        if (!ffmpegResult.Found || string.IsNullOrEmpty(ffmpegResult.ExecutablePath))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegResult.ExecutablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Fast stream copy of embedded video/picture stream
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(audioFilePath);
                psi.ArgumentList.Add("-an");
                psi.ArgumentList.Add("-vcodec");
                psi.ArgumentList.Add("copy");
                psi.ArgumentList.Add(cachedArtPath);

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Wait up to 3 seconds for art extraction
                if (process.WaitForExit(3000) && process.ExitCode == 0 && File.Exists(cachedArtPath) && new FileInfo(cachedArtPath).Length > 0)
                {
                    return cachedArtPath;
                }

                // If copy failed (e.g., png or requires transcoding to jpg), attempt scaled conversion
                var fallbackPsi = new ProcessStartInfo
                {
                    FileName = ffmpegResult.ExecutablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                fallbackPsi.ArgumentList.Add("-y");
                fallbackPsi.ArgumentList.Add("-i");
                fallbackPsi.ArgumentList.Add(audioFilePath);
                fallbackPsi.ArgumentList.Add("-an");
                fallbackPsi.ArgumentList.Add("-vf");
                fallbackPsi.ArgumentList.Add("scale=500:500:force_original_aspect_ratio=decrease");
                fallbackPsi.ArgumentList.Add(cachedArtPath);

                using var fallbackProcess = new Process { StartInfo = fallbackPsi };
                fallbackProcess.Start();

                if (fallbackProcess.WaitForExit(3000) && fallbackProcess.ExitCode == 0 && File.Exists(cachedArtPath) && new FileInfo(cachedArtPath).Length > 0)
                {
                    return cachedArtPath;
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[AudioMetadata] ffmpeg art extraction exception: {ex.Message}");
            }

            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AudioTrackMetadata> ExtractTagsWithFfprobeAsync(string audioFilePath, string? albumArtPath, CancellationToken cancellationToken)
    {
        var ffprobeResult = ExternalToolLocator.Locate("ffprobe.exe");
        var baseTitle = Path.GetFileNameWithoutExtension(audioFilePath);
        var ext = Path.GetExtension(audioFilePath).TrimStart('.').ToUpperInvariant();

        if (!ffprobeResult.Found || string.IsNullOrEmpty(ffprobeResult.ExecutablePath))
        {
            return new AudioTrackMetadata(
                audioFilePath,
                Title: baseTitle,
                Artist: "Unknown Artist",
                Album: "Unknown Album",
                Year: null,
                Bitrate: null,
                AudioCodec: ext,
                SampleRate: null,
                Channels: null,
                AlbumArtPath: albumArtPath);
        }

        try
        {
            var output = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobeResult.ExecutablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add("-v");
                psi.ArgumentList.Add("quiet");
                psi.ArgumentList.Add("-print_format");
                psi.ArgumentList.Add("json");
                psi.ArgumentList.Add("-show_format");
                psi.ArgumentList.Add("-show_streams");
                psi.ArgumentList.Add(audioFilePath);

                using var process = new Process { StartInfo = psi };
                process.Start();

                var jsonText = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                return jsonText;
            }, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
            {
                return new AudioTrackMetadata(audioFilePath, baseTitle, "Unknown Artist", "Unknown Album", null, null, ext, null, null, albumArtPath);
            }

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            string title = baseTitle;
            string artist = "Unknown Artist";
            string album = "Unknown Album";
            string? year = null;
            string? bitrate = null;
            string? codec = ext;
            string? sampleRate = null;
            string? channels = null;

            if (root.TryGetProperty("format", out var formatProp))
            {
                if (formatProp.TryGetProperty("bit_rate", out var brProp) && long.TryParse(brProp.GetString(), out var brVal))
                {
                    bitrate = $"{brVal / 1000} kbps";
                }

                if (formatProp.TryGetProperty("tags", out var tagsProp))
                {
                    title = GetTagValue(tagsProp, "title") ?? title;
                    artist = GetTagValue(tagsProp, "artist") ?? GetTagValue(tagsProp, "album_artist") ?? artist;
                    album = GetTagValue(tagsProp, "album") ?? album;
                    year = GetTagValue(tagsProp, "date") ?? GetTagValue(tagsProp, "year");
                }
            }

            if (root.TryGetProperty("streams", out var streamsProp) && streamsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsProp.EnumerateArray())
                {
                    if (stream.TryGetProperty("codec_type", out var typeProp) && typeProp.GetString() == "audio")
                    {
                        if (stream.TryGetProperty("codec_name", out var codecProp))
                        {
                            codec = codecProp.GetString()?.ToUpperInvariant() ?? codec;
                        }

                        if (stream.TryGetProperty("sample_rate", out var srProp) && int.TryParse(srProp.GetString(), out var srVal))
                        {
                            sampleRate = $"{srVal / 1000.0:0.#} kHz";
                        }

                        if (stream.TryGetProperty("channels", out var chProp))
                        {
                            channels = $"{chProp.GetInt32()} ch";
                        }
                        break;
                    }
                }
            }

            return new AudioTrackMetadata(
                audioFilePath,
                Title: title,
                Artist: artist,
                Album: album,
                Year: year,
                Bitrate: bitrate,
                AudioCodec: codec,
                SampleRate: sampleRate,
                Channels: channels,
                AlbumArtPath: albumArtPath);
        }
        catch (Exception ex)
        {
            LogService.LogWarning($"[AudioMetadata] ffprobe JSON parse error: {ex.Message}");
            return new AudioTrackMetadata(audioFilePath, baseTitle, "Unknown Artist", "Unknown Album", null, null, ext, null, null, albumArtPath);
        }
    }

    private static string? GetTagValue(JsonElement tags, string tagName)
    {
        foreach (var prop in tags.EnumerateObject())
        {
            if (string.Equals(prop.Name, tagName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
