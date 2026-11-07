using System;
using System.Globalization;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Pure logic helper for mapping mpv event codes, property values, and command arguments.
/// </summary>
public static class MpvEventMapper
{
    public static EndFileReason MapEndFileReason(int rawReason)
    {
        return rawReason switch
        {
            0 => EndFileReason.Eof,
            2 => EndFileReason.Stop,
            3 => EndFileReason.Quit,
            4 => EndFileReason.Error,
            5 => EndFileReason.Redirect,
            _ => EndFileReason.Unknown
        };
    }

    public static TimeSpan SecondsToTimeSpan(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static bool FlagToBool(int flag)
    {
        return flag != 0;
    }

    public static string[] BuildLoadFileCommand(string filePath, bool startPaused)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        // mpv command: loadfile <filename> [replace|append|append-play] [options]
        return new[] { "loadfile", filePath, startPaused ? "pause" : "replace" };
    }

    public static string[] BuildSeekCommand(double seconds, bool exact)
    {
        var targetStr = seconds.ToString(CultureInfo.InvariantCulture);
        var flag = exact ? "absolute+exact" : "absolute";
        return new[] { "seek", targetStr, flag };
    }

    public static string[] BuildSeekRelativeCommand(double offsetSeconds, bool exact)
    {
        var targetStr = offsetSeconds.ToString(CultureInfo.InvariantCulture);
        var flag = exact ? "relative+exact" : "relative";
        return new[] { "seek", targetStr, flag };
    }
}

