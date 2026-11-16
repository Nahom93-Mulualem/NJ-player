using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NJPlayer.Features.Trimmer;

/// <summary>
/// A validated single slice segment in a batch trimming operation.
/// </summary>
public sealed record BatchTrimSegment(
    int SegmentIndex,
    int SourceLineNumber,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string OutputFilePath)
{
    public TimeSpan Duration => EndTime - StartTime;

    public string DisplaySummary =>
        $"Part {SegmentIndex}: {FormatTime(StartTime)} - {FormatTime(EndTime)} ({FormatTime(Duration)})";

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss\.fff") : t.ToString(@"mm\:ss\.fff");
}

/// <summary>
/// Result of parsing and validating a batch trim text input.
/// </summary>
public sealed record BatchTrimValidationResult(
    bool IsValid,
    string? ErrorMessage,
    int? ErrorLineNumber,
    IReadOnlyList<BatchTrimSegment> Segments);

/// <summary>
/// Parser and validator for batch trimming timestamp lists.
/// </summary>
public static class BatchTrimParser
{
    private static readonly Regex DelimiterRegex = new(@"\s*[-–—]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Validates and parses a multi-line string of timestamp cuts.
    /// Lines must be in the format 'start-end' (e.g. 00:30-01:15 or 01:20:00-01:45:00).
    /// </summary>
    public static BatchTrimValidationResult ValidateAndParse(string text, string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BatchTrimValidationResult(false, "No timestamp ranges provided.", null, Array.Empty<BatchTrimSegment>());
        }

        var segments = new List<BatchTrimSegment>();
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var dir = Path.GetDirectoryName(inputFilePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
        var ext = Path.GetExtension(inputFilePath);

        int segmentIndex = 1;

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();

            // Skip empty lines or comments starting with #
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = DelimiterRegex.Split(line);
            if (parts.Length != 2)
            {
                return new BatchTrimValidationResult(
                    false,
                    $"Line {lineNumber}: Invalid format '{line}'. Expected 'start-end' (e.g. 00:30-01:15).",
                    lineNumber,
                    Array.Empty<BatchTrimSegment>());
            }

            if (!TryParseTimestamp(parts[0], out var start))
            {
                return new BatchTrimValidationResult(
                    false,
                    $"Line {lineNumber}: Invalid start timestamp '{parts[0]}'. Use mm:ss or hh:mm:ss.",
                    lineNumber,
                    Array.Empty<BatchTrimSegment>());
            }

            if (!TryParseTimestamp(parts[1], out var end))
            {
                return new BatchTrimValidationResult(
                    false,
                    $"Line {lineNumber}: Invalid end timestamp '{parts[1]}'. Use mm:ss or hh:mm:ss.",
                    lineNumber,
                    Array.Empty<BatchTrimSegment>());
            }

            if (start < TimeSpan.Zero)
            {
                return new BatchTrimValidationResult(
                    false,
                    $"Line {lineNumber}: Start timestamp cannot be negative.",
                    lineNumber,
                    Array.Empty<BatchTrimSegment>());
            }

            if (end <= start)
            {
                return new BatchTrimValidationResult(
                    false,
                    $"Line {lineNumber}: End time ({parts[1]}) must be greater than start time ({parts[0]}).",
                    lineNumber,
                    Array.Empty<BatchTrimSegment>());
            }

            var outPath = Path.Combine(dir, $"{baseName}_part{segmentIndex}{ext}");

            segments.Add(new BatchTrimSegment(segmentIndex, lineNumber, start, end, outPath));
            segmentIndex++;
        }

        if (segments.Count == 0)
        {
            return new BatchTrimValidationResult(false, "No valid timestamp ranges found.", null, Array.Empty<BatchTrimSegment>());
        }

        return new BatchTrimValidationResult(true, null, null, segments);
    }

    /// <summary>
    /// Parses timestamps in formats: mm:ss, mm:ss.fff, hh:mm:ss, hh:mm:ss.fff, or total seconds (e.g. 90.5).
    /// </summary>
    public static bool TryParseTimestamp(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();

        // 1. Try standard TimeSpan formats
        if (TimeSpan.TryParseExact(s, @"h\:m\:s\.fff", CultureInfo.InvariantCulture, out result) ||
            TimeSpan.TryParseExact(s, @"h\:m\:s", CultureInfo.InvariantCulture, out result) ||
            TimeSpan.TryParseExact(s, @"m\:s\.fff", CultureInfo.InvariantCulture, out result) ||
            TimeSpan.TryParseExact(s, @"m\:s", CultureInfo.InvariantCulture, out result) ||
            TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        // 2. Try raw seconds (e.g. "45" or "120.5")
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var totalSec) && totalSec >= 0)
        {
            result = TimeSpan.FromSeconds(totalSec);
            return true;
        }

        return false;
    }
}
