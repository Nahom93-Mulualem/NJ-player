using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// Interface for subtitle providers (hash-based and query-based online downloaders).
/// </summary>
public interface ISubtitleProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<SubtitleSearchResult>> SearchByHashAsync(string filePath, string movieHash, CancellationToken ct = default);

    Task<IReadOnlyList<SubtitleSearchResult>> SearchByQueryAsync(string query, string? language = null, CancellationToken ct = default);

    Task<string?> DownloadSubtitleAsync(SubtitleSearchResult subtitle, string destinationDirectory, CancellationToken ct = default);
}

