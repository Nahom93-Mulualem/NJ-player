using System;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// Information about an embedded or external subtitle track.
/// </summary>
public sealed class SubtitleTrackInfo
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Language { get; init; } = "und";
    public string Codec { get; init; } = string.Empty;
    public bool IsExternal { get; init; }
    public bool IsSelected { get; init; }
    public string? ExternalFilePath { get; init; }

    public string DisplayName
    {
        get
        {
            var lang = string.IsNullOrWhiteSpace(Language) || Language == "und" ? "" : $"[{Language.ToUpperInvariant()}] ";
            var name = !string.IsNullOrWhiteSpace(Title) ? Title : (IsExternal ? "External Subtitle" : $"Track #{Id}");
            return $"{lang}{name}";
        }
    }
}

