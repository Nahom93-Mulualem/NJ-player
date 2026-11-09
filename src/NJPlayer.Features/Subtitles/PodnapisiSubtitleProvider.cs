using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// Free keyless subtitle provider utilizing Podnapisi public search endpoints.
/// Handles network dropouts, empty results, and download extraction gracefully.
/// </summary>
public sealed class PodnapisiSubtitleProvider : ISubtitleProvider
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static PodnapisiSubtitleProvider()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NJPlayer/1.0 (Windows NT 10.0; Win64; x64)");
    }

    public string ProviderName => "Podnapisi";

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchByHashAsync(string filePath, string movieHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(movieHash)) return Array.Empty<SubtitleSearchResult>();

        // Fall back to filename search if hash search is not supported by endpoint
        var filename = Path.GetFileNameWithoutExtension(filePath);
        return await SearchByQueryAsync(filename, null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchByQueryAsync(string query, string? language = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SubtitleSearchResult>();

        var results = new List<SubtitleSearchResult>();
        try
        {
            var url = $"https://www.podnapisi.net/subtitles/search/old?sK={Uri.EscapeDataString(query)}&sXML=1";
            var response = await HttpClient.GetStringAsync(url, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response)) return results;

            var doc = XDocument.Parse(response);
            var subtitleElements = doc.Descendants("subtitle");

            foreach (var elem in subtitleElements)
            {
                var id = elem.Element("id")?.Value ?? string.Empty;
                var release = elem.Element("release")?.Value ?? elem.Element("title")?.Value ?? query;
                var lang = elem.Element("language")?.Value ?? "en";
                var dlUrl = elem.Element("url")?.Value ?? $"https://www.podnapisi.net/subtitles/{id}/download";

                if (!string.IsNullOrWhiteSpace(id))
                {
                    results.Add(new SubtitleSearchResult
                    {
                        Provider = ProviderName,
                        Id = id,
                        ReleaseName = release,
                        Language = lang,
                        Format = "srt",
                        DownloadUrl = dlUrl
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Network failure, XML parsing issue, or endpoint down: fail gracefully
        }

        return results;
    }

    public async Task<string?> DownloadSubtitleAsync(SubtitleSearchResult subtitle, string destinationDirectory, CancellationToken ct = default)
    {
        if (subtitle == null || string.IsNullOrWhiteSpace(subtitle.DownloadUrl)) return null;

        try
        {
            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var responseBytes = await HttpClient.GetByteArrayAsync(subtitle.DownloadUrl, ct).ConfigureAwait(false);
            if (responseBytes == null || responseBytes.Length == 0) return null;

            // Check if returned data is a ZIP archive (PK\x03\x04)
            if (responseBytes.Length > 4 && responseBytes[0] == 0x50 && responseBytes[1] == 0x4B && responseBytes[2] == 0x03 && responseBytes[3] == 0x04)
            {
                using var ms = new MemoryStream(responseBytes);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is ".srt" or ".vtt" or ".ass" or ".sub")
                    {
                        var safeName = SanitizeFileName(entry.Name);
                        var targetPath = Path.Combine(destinationDirectory, safeName);
                        entry.ExtractToFile(targetPath, overwrite: true);
                        return targetPath;
                    }
                }
            }
            else
            {
                // Raw subtitle file (.srt)
                var safeName = SanitizeFileName($"{subtitle.ReleaseName}_{subtitle.Language}.srt");
                var targetPath = Path.Combine(destinationDirectory, safeName);
                await File.WriteAllBytesAsync(targetPath, responseBytes, ct).ConfigureAwait(false);
                return targetPath;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Network or disk write error
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}

