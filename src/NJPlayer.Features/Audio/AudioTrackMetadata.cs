using System;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Audio track metadata including album art and stream details.
/// </summary>
public sealed record AudioTrackMetadata(
    string FilePath,
    string Title,
    string Artist,
    string Album,
    string? Year,
    string? Bitrate,
    string? AudioCodec,
    string? SampleRate,
    string? Channels,
    string? AlbumArtPath)
{
    public bool HasAlbumArt => !string.IsNullOrEmpty(AlbumArtPath);

    public string DisplaySubtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Album))
            {
                return $"{Artist} — {Album}";
            }
            if (!string.IsNullOrWhiteSpace(Artist))
            {
                return Artist;
            }
            if (!string.IsNullOrWhiteSpace(Album))
            {
                return Album;
            }
            return "Unknown Artist";
        }
    }

    public string QualityBadge
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(AudioCodec)) parts.Add(AudioCodec.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(Bitrate)) parts.Add(Bitrate);
            if (!string.IsNullOrWhiteSpace(SampleRate)) parts.Add(SampleRate);
            return parts.Count > 0 ? string.Join(" • ", parts) : "AUDIO";
        }
    }
}
