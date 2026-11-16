using System;
using NJPlayer.Features.Trimmer;
using Xunit;

namespace NJPlayer.Tests;

public class BatchTrimTests
{
    [Fact]
    public void BatchTrimParser_ParsesValidMultiLineInput()
    {
        var input = """
            # Intro and chapter cuts
            00:30-01:15
            01:20-02:45

            00:05:00 - 00:06:30.500
            """;

        var result = BatchTrimParser.ValidateAndParse(input, @"C:\Media\movie.mp4");

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(3, result.Segments.Count);

        // Segment 1
        Assert.Equal(1, result.Segments[0].SegmentIndex);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Segments[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(75), result.Segments[0].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(45), result.Segments[0].Duration);
        Assert.Equal(@"C:\Media\movie_part1.mp4", result.Segments[0].OutputFilePath);

        // Segment 2
        Assert.Equal(2, result.Segments[1].SegmentIndex);
        Assert.Equal(TimeSpan.FromSeconds(80), result.Segments[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(165), result.Segments[1].EndTime);
        Assert.Equal(@"C:\Media\movie_part2.mp4", result.Segments[1].OutputFilePath);

        // Segment 3
        Assert.Equal(3, result.Segments[2].SegmentIndex);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Segments[2].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(390.5), result.Segments[2].EndTime);
        Assert.Equal(@"C:\Media\movie_part3.mp4", result.Segments[2].OutputFilePath);
    }

    [Fact]
    public void BatchTrimParser_FlagsEndBeforeStart_WithLineNumber()
    {
        var input = """
            00:30-01:15
            02:00-01:30
            03:00-04:00
            """;

        var result = BatchTrimParser.ValidateAndParse(input, @"C:\Media\sample.mkv");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Line 2", result.ErrorMessage);
        Assert.Contains("must be greater than start time", result.ErrorMessage);
        Assert.Equal(2, result.ErrorLineNumber);
    }

    [Fact]
    public void BatchTrimParser_FlagsMalformedTimestamp_WithLineNumber()
    {
        var input = """
            00:30-01:15
            invalid_time - 02:00
            """;

        var result = BatchTrimParser.ValidateAndParse(input, @"C:\Media\sample.mkv");

        Assert.False(result.IsValid);
        Assert.Contains("Line 2", result.ErrorMessage);
        Assert.Contains("Invalid start timestamp", result.ErrorMessage);
    }

    [Fact]
    public void BatchTrimParser_FlagsMissingDelimiter()
    {
        var input = """
            00:30 01:15
            """;

        var result = BatchTrimParser.ValidateAndParse(input, @"C:\Media\sample.mkv");

        Assert.False(result.IsValid);
        Assert.Contains("Line 1", result.ErrorMessage);
        Assert.Contains("Expected 'start-end'", result.ErrorMessage);
    }

    [Fact]
    public void BatchTrimParser_EmptyText_ReturnsFalse()
    {
        var result = BatchTrimParser.ValidateAndParse("   ", @"C:\Media\sample.mkv");
        Assert.False(result.IsValid);
    }
}
