using System;
using System.IO;
using System.Threading.Tasks;
using NJPlayer.Core.Playback;
using NJPlayer.Features.Audio;
using NJPlayer.Features.Subtitles;
using Xunit;

namespace NJPlayer.Tests;

public class MusicPlayerTests
{
    [Theory]
    [InlineData("track.mp3", true)]
    [InlineData("song.FLAC", true)]
    [InlineData("audio.m4a", true)]
    [InlineData("voice.wav", true)]
    [InlineData("stream.opus", true)]
    [InlineData("music.ogg", true)]
    [InlineData("classic.wma", true)]
    [InlineData("movie.mp4", false)]
    [InlineData("clip.mkv", false)]
    [InlineData("video.webm", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MediaFormats_IsAudioOnly_DetectsCorrectly(string? path, bool expected)
    {
        var result = MediaFormats.IsAudioOnly(path);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("video.MKV", true)]
    [InlineData("stream.ts", true)]
    [InlineData("track.mp3", false)]
    [InlineData("song.flac", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MediaFormats_IsVideo_DetectsCorrectly(string? path, bool expected)
    {
        var result = MediaFormats.IsVideo(path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void LrcLyricsService_ParsesStandardAndMultiTimestampLyrics()
    {
        var lrc = """
            [ti:Electric Cyan Dreams]
            [ar:NJ Artist]
            [al:Audio Hero]
            [00:05.50]First line of the song
            [00:10.00][00:25.00]Chorus line repeated
            [00:15.250]Third line with 3 digits ms
            """;

        var doc = LrcLyricsService.Parse(lrc);

        Assert.Equal("Electric Cyan Dreams", doc.Title);
        Assert.Equal("NJ Artist", doc.Artist);
        Assert.Equal("Audio Hero", doc.Album);
        Assert.Equal(4, doc.Lines.Count);

        // Lines must be sorted chronologically
        Assert.Equal(TimeSpan.FromSeconds(5.5), doc.Lines[0].Time);
        Assert.Equal("First line of the song", doc.Lines[0].Text);

        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[1].Time);
        Assert.Equal("Chorus line repeated", doc.Lines[1].Text);

        Assert.Equal(TimeSpan.FromSeconds(15.25), doc.Lines[2].Time);
        Assert.Equal("Third line with 3 digits ms", doc.Lines[2].Text);

        Assert.Equal(TimeSpan.FromSeconds(25), doc.Lines[3].Time);
        Assert.Equal("Chorus line repeated", doc.Lines[3].Text);
    }

    [Fact]
    public void LrcLyricsService_GetActiveLyrics_ReturnsSurroundingAndCurrentLines()
    {
        var lrc = """
            [00:05.00]Line 1
            [00:10.00]Line 2
            [00:15.00]Line 3
            """;

        var doc = LrcLyricsService.Parse(lrc);

        // 1. Before first line (e.g. 2s)
        var (prev0, cur0, next0) = doc.GetActiveLyrics(TimeSpan.FromSeconds(2));
        Assert.Null(prev0);
        Assert.Equal(string.Empty, cur0);
        Assert.Equal("Line 1", next0);

        // 2. Exactly at line 1 (5s)
        var (prev1, cur1, next1) = doc.GetActiveLyrics(TimeSpan.FromSeconds(5));
        Assert.Null(prev1);
        Assert.Equal("Line 1", cur1);
        Assert.Equal("Line 2", next1);

        // 3. During line 2 (12s)
        var (prev2, cur2, next2) = doc.GetActiveLyrics(TimeSpan.FromSeconds(12));
        Assert.Equal("Line 1", prev2);
        Assert.Equal("Line 2", cur2);
        Assert.Equal("Line 3", next2);

        // 4. During line 3 (20s)
        var (prev3, cur3, next3) = doc.GetActiveLyrics(TimeSpan.FromSeconds(20));
        Assert.Equal("Line 2", prev3);
        Assert.Equal("Line 3", cur3);
        Assert.Null(next3);
    }

    [Fact]
    public async Task AudioMetadataService_ExtractsFallbackGracefullyWhenFileDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NJPlayer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new AudioMetadataService(tempDir);
            var result = await service.ExtractMetadataAsync("nonexistent_song.mp3");

            Assert.Equal("nonexistent_song", result.Title);
            Assert.Equal("Unknown Artist", result.Artist);
            Assert.Equal("Unknown Album", result.Album);
            Assert.Null(result.AlbumArtPath);
            Assert.False(result.HasAlbumArt);
            Assert.Equal("Unknown Artist — Unknown Album", result.DisplaySubtitle);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AudioMetadataService_ResolvesSidecarCoverImage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NJPlayer_Sidecar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var audioFile = Path.Combine(tempDir, "sample_track.mp3");
            File.WriteAllText(audioFile, "dummy audio");

            var coverFile = Path.Combine(tempDir, "cover.jpg");
            File.WriteAllBytes(coverFile, new byte[] { 0xFF, 0xD8, 0xFF }); // dummy JPEG header

            var service = new AudioMetadataService(tempDir);
            var resolved = await service.ResolveAlbumArtAsync(audioFile);

            Assert.NotNull(resolved);
            Assert.Equal(coverFile, resolved);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void VisualizerFilterBuilder_BuildsCleanFiltersForDifferentModes()
    {
        var bars = VisualizerFilterBuilder.BuildFilter("bars");
        Assert.Contains("showfreqs", bars);
        Assert.Contains("mode=bar", bars);
        Assert.Contains("rate=60", bars);

        var wave = VisualizerFilterBuilder.BuildFilter("waveform");
        Assert.Contains("showwaves", wave);
        Assert.Contains("mode=cline", wave);
        Assert.Contains("rate=60", wave);

        var spec = VisualizerFilterBuilder.BuildFilter("spectrum");
        Assert.Contains("showcqt", spec);
        Assert.Contains("fps=60", spec);

        var vector = VisualizerFilterBuilder.BuildFilter("vector");
        Assert.Contains("avectorscope", vector);
        Assert.Contains("mode=lissajous", vector);
        Assert.Contains("rate=60", vector);
    }

    [Fact]
    public void VisualizerFilterBuilder_EscapeFilterPath_EscapesCorrectly()
    {
        var path = @"C:\Music\Backgrounds\bg:photo'1.jpg";
        var escaped = VisualizerFilterBuilder.EscapeFilterPath(path);

        Assert.Equal(@"C\:/Music/Backgrounds/bg\:photo\'1.jpg", escaped);
    }

    [Fact]
    public void VisualizerFilterBuilder_WithBackgroundFile_GeneratesCompositeFilter()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var filter = VisualizerFilterBuilder.BuildFilter("bars", tempFile, 1280, 720);

            Assert.Contains("movie=", filter);
            Assert.Contains("overlay=", filter);
            Assert.Contains("colorkey=", filter);
            Assert.Contains("showfreqs=", filter);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
