using System;
using System.IO;

namespace NJPlayer.Features.Downloader;

public enum DownloadFormatPreset
{
    BestAvailable = 0,
    Max1080p,
    Max720p,
    AudioOnlyMp3,
    AudioOnlyBest
}

/// <summary>
/// Options for online video/audio downloading via yt-dlp.
/// </summary>
public sealed class DownloadOptions
{
    public string Url { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public DownloadFormatPreset Format { get; set; } = DownloadFormatPreset.BestAvailable;
    public bool PlayWhenDone { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url) || !Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Please provide a valid URL (e.g. https://...).", nameof(Url));
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}

