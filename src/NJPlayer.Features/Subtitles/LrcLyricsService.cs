using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// A single timestamped lyric line from an LRC document.
/// </summary>
public sealed record LrcLine(TimeSpan Time, string Text);

/// <summary>
/// A parsed LRC document containing metadata and sorted lyric lines.
/// </summary>
public sealed class LrcDocument
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public IReadOnlyList<LrcLine> Lines { get; }

    public LrcDocument(IEnumerable<LrcLine> lines, string? title = null, string? artist = null, string? album = null)
    {
        Lines = lines.OrderBy(l => l.Time).ToList();
        Title = title;
        Artist = artist;
        Album = album;
    }

    /// <summary>
    /// Gets the active lyric line at the specified playback position along with previous and next lines for smooth 3-line display.
    /// </summary>
    public (string? Prev, string Current, string? Next) GetActiveLyrics(TimeSpan position)
    {
        if (Lines.Count == 0)
        {
            return (null, string.Empty, null);
        }

        int activeIndex = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= position)
            {
                activeIndex = i;
            }
            else
            {
                break;
            }
        }

        if (activeIndex < 0)
        {
            // Position is before the first line
            return (null, string.Empty, Lines[0].Text);
        }

        var prev = activeIndex > 0 ? Lines[activeIndex - 1].Text : null;
        var current = Lines[activeIndex].Text;
        var next = (activeIndex + 1 < Lines.Count) ? Lines[activeIndex + 1].Text : null;

        return (prev, current, next);
    }
}

/// <summary>
/// Parser and loader for synchronized LRC lyrics files.
/// Supports multi-timestamp lines, standard [mm:ss.xx] / [mm:ss.xxx] syntax, and directory auto-discovery.
/// </summary>
public static class LrcLyricsService
{
    private static readonly Regex TimestampRegex = new(@"\[(?<min>\d{1,2}):(?<sec>\d{1,2})(?:\.(?<ms>\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"^\[(?<key>[a-zA-Z]+):(?<val>.*)\]$", RegexOptions.Compiled);

    /// <summary>
    /// Parses LRC content from text.
    /// </summary>
    public static LrcDocument Parse(string lrcContent)
    {
        if (string.IsNullOrWhiteSpace(lrcContent))
        {
            return new LrcDocument(Array.Empty<LrcLine>());
        }

        var lines = new List<LrcLine>();
        string? title = null;
        string? artist = null;
        string? album = null;

        using var reader = new StringReader(lrcContent);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Check metadata tags e.g. [ti:Song], [ar:Artist], [al:Album]
            var tagMatch = TagRegex.Match(trimmed);
            if (tagMatch.Success && !char.IsDigit(tagMatch.Groups["key"].Value[0]))
            {
                var key = tagMatch.Groups["key"].Value.ToLowerInvariant();
                var val = tagMatch.Groups["val"].Value.Trim();
                switch (key)
                {
                    case "ti": title = val; break;
                    case "ar": artist = val; break;
                    case "al": album = val; break;
                }
                continue;
            }

            // Extract all timestamps on this line
            var matches = TimestampRegex.Matches(trimmed);
            if (matches.Count == 0) continue;

            // The text is whatever follows the last timestamp
            var lastMatch = matches[^1];
            var lyricText = trimmed[(lastMatch.Index + lastMatch.Length)..].Trim();

            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
                    !int.TryParse(m.Groups["sec"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    continue;
                }

                int milliseconds = 0;
                if (m.Groups["ms"].Success)
                {
                    var msStr = m.Groups["ms"].Value;
                    if (msStr.Length == 2 && int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                    {
                        milliseconds = c * 10; // centiseconds to ms
                    }
                    else if (msStr.Length == 3 && int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli))
                    {
                        milliseconds = milli;
                    }
                    else if (int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                    {
                        milliseconds = val;
                    }
                }

                var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                lines.Add(new LrcLine(timestamp, lyricText));
            }
        }

        return new LrcDocument(lines, title, artist, album);
    }

    /// <summary>
    /// Searches for a matching .lrc file next to the audio file and loads it if found.
    /// </summary>
    public static LrcDocument? FindAndLoad(string audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            return null;
        }

        try
        {
            var dir = Path.GetDirectoryName(audioFilePath);
            if (string.IsNullOrEmpty(dir)) return null;

            var baseName = Path.GetFileNameWithoutExtension(audioFilePath);
            var directLrc = Path.Combine(dir, $"{baseName}.lrc");

            if (File.Exists(directLrc))
            {
                var content = File.ReadAllText(directLrc);
                return Parse(content);
            }

            // Also check for case-insensitive match in folder
            var candidate = Directory.GetFiles(dir, "*.lrc")
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase));

            if (candidate != null && File.Exists(candidate))
            {
                var content = File.ReadAllText(candidate);
                return Parse(content);
            }
        }
        catch
        {
            // Ignore file read exceptions
        }

        return null;
    }
}
