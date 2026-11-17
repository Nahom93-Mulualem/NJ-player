using System;
using System.Collections.Generic;
using System.IO;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Media format detection and categorization utilities.
/// Pure C# with zero WPF dependencies per architecture rules.
/// </summary>
public static class MediaFormats
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".wav", ".aac", ".opus", ".ogg",
        ".wma", ".alac", ".aiff", ".ape", ".wv", ".mka"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv",
        ".ts", ".m4v", ".3gp", ".vob", ".ogv", ".mpd"
    };

    /// <summary>
    /// Checks if a file is a DASH streaming manifest (.mpd).
    /// </summary>
    public static bool IsDashManifest(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".mpd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a local file is an HLS playlist or master manifest (.m3u8 containing #EXT-X- tags).
    /// </summary>
    public static bool IsHlsStream(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        if (!string.Equals(ext, ".m3u8", StringComparison.OrdinalIgnoreCase)) return false;

        if (File.Exists(path))
        {
            try
            {
                using var reader = new StreamReader(path);
                for (int i = 0; i < 30; i++)
                {
                    var line = reader.ReadLine();
                    if (line == null) break;
                    if (line.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the given file or URL is primarily an audio-only file based on its extension.
    /// </summary>
    public static bool IsAudioOnly(string? filePathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(filePathOrUrl)) return false;

        try
        {
            var ext = Path.GetExtension(filePathOrUrl);
            return AudioExtensions.Contains(ext);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the given file or URL is a video file based on its extension.
    /// </summary>
    public static bool IsVideo(string? filePathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(filePathOrUrl)) return false;

        try
        {
            var ext = Path.GetExtension(filePathOrUrl);
            return VideoExtensions.Contains(ext);
        }
        catch
        {
            return false;
        }
    }
}
