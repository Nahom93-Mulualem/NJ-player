using System;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// A subtitle result returned by an online subtitle provider.
/// </summary>
public sealed class SubtitleSearchResult
{
    public string Provider { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string Language { get; init; } = "en";
    public string Format { get; init; } = "srt";
    public string DownloadUrl { get; init; } = string.Empty;
    public int DownloadsCount { get; init; }
    public double Rating { get; init; }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(ReleaseName) ? ReleaseName : $"Subtitle {Id}";
}

