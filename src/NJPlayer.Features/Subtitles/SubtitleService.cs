using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// Service managing subtitle detection, sidecar auto-loading, encoding detection,
/// and online subtitle lookup.
/// </summary>
public sealed class SubtitleService
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub"];

    public static readonly string DefaultGeezFont = "Nyala, Ebrima, Segoe UI, sans-serif";

    private readonly ISubtitleProvider _provider;

    public ISubtitleProvider Provider => _provider;

    public SubtitleService(ISubtitleProvider? provider = null)
    {
        _provider = provider ?? new PodnapisiSubtitleProvider();
    }

    /// <summary>
    /// Scans the directory containing the given media file for matching sidecar subtitles.
    /// (e.g. "movie.en.srt", "movie.am.srt", "movie.srt").
    /// </summary>
    public IReadOnlyList<string> FindMatchingSubtitles(string mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return Array.Empty<string>();
        }

        var dir = Path.GetDirectoryName(mediaFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        var baseName = Path.GetFileNameWithoutExtension(mediaFilePath);
        var matches = new List<string>();

        try
        {
            foreach (var ext in SubtitleExtensions)
            {
                // Direct match: movie.srt
                var exactPath = Path.Combine(dir, $"{baseName}{ext}");
                if (File.Exists(exactPath))
                {
                    matches.Add(exactPath);
                }

                // Language tagged match: movie.*.srt
                var pattern = $"{baseName}.*{ext}";
                var found = Directory.GetFiles(dir, pattern);
                foreach (var file in found)
                {
                    if (!matches.Contains(file, StringComparer.OrdinalIgnoreCase))
                    {
                        matches.Add(file);
                    }
                }
            }
        }
        catch
        {
            // Ignore directory access permissions
        }

        return matches;
    }

    /// <summary>
    /// Detects text encoding for a subtitle file (UTF-8, UTF-16, ASCII/Windows-1252).
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        if (!File.Exists(filePath)) return Encoding.UTF8;

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bom = new byte[4];
            var read = file.Read(bom, 0, 4);

            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            {
                return Encoding.UTF8; // UTF-8 with BOM
            }
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            {
                return Encoding.Unicode; // UTF-16 LE
            }
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
            if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
            {
                return Encoding.UTF32;
            }

            // Fallback: Check if file bytes are valid UTF-8
            file.Position = 0;
            var buffer = new byte[Math.Min(file.Length, 4096)];
            var count = file.Read(buffer, 0, buffer.Length);
            if (IsValidUtf8(buffer, count))
            {
                return Encoding.UTF8;
            }
        }
        catch
        {
            // Default to UTF-8
        }

        return Encoding.Latin1; // Windows-1252 / ISO-8859-1 fallback
    }

    private static bool IsValidUtf8(byte[] bytes, int length)
    {
        int i = 0;
        while (i < length)
        {
            if (bytes[i] <= 0x7F)
            {
                i++;
                continue;
            }

            int count;
            if ((bytes[i] & 0xE0) == 0xC0) count = 1;
            else if ((bytes[i] & 0xF0) == 0xE0) count = 2;
            else if ((bytes[i] & 0xF8) == 0xF0) count = 3;
            else return false;

            i++;
            for (int j = 0; j < count; j++)
            {
                if (i >= length || (bytes[i] & 0xC0) != 0x80) return false;
                i++;
            }
        }
        return true;
    }

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchOnlineSubtitlesAsync(string mediaFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath)) return Array.Empty<SubtitleSearchResult>();

        var hash = await SubtitleHashCalculator.ComputeHashAsync(mediaFilePath, ct).ConfigureAwait(false);
        var filename = Path.GetFileNameWithoutExtension(mediaFilePath);

        var results = new List<SubtitleSearchResult>();

        // 1. Try search by hash
        if (!string.IsNullOrEmpty(hash))
        {
            var hashResults = await _provider.SearchByHashAsync(mediaFilePath, hash, ct).ConfigureAwait(false);
            results.AddRange(hashResults);
        }

        // 2. If no hash results, search by filename
        if (results.Count == 0 && !string.IsNullOrWhiteSpace(filename))
        {
            var queryResults = await _provider.SearchByQueryAsync(filename, null, ct).ConfigureAwait(false);
            results.AddRange(queryResults);
        }

        return results;
    }
}

