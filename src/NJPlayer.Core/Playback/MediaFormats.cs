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
        ".ts", ".m4v", ".3gp", ".vob", ".ogv"
    };

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
