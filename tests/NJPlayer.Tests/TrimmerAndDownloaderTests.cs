using System;
using NJPlayer.Features.Downloader;
using NJPlayer.Features.Trimmer;
using Xunit;

namespace NJPlayer.Tests;

public class TrimmerAndDownloaderTests
{
    [Fact]
    public void TrimOptions_CalculatesDuration_AndValidatesBounds()
    {
        var options = new TrimOptions
        {
            InputFilePath = @"C:\media\video.mp4",
            OutputFilePath = @"C:\media\video_trimmed.mp4",
            InPoint = TimeSpan.FromSeconds(10),
            OutPoint = TimeSpan.FromSeconds(45),
            ReEncode = false
        };

        options.Validate(); // Should succeed
        Assert.Equal(TimeSpan.FromSeconds(35), options.Duration);
    }

    [Fact]
    public void TrimOptions_ThrowsOnInvalidCutPoints()
    {
        var options = new TrimOptions
        {
            InputFilePath = @"C:\media\video.mp4",
            OutputFilePath = @"C:\media\video_trimmed.mp4",
            InPoint = TimeSpan.FromSeconds(50),
            OutPoint = TimeSpan.FromSeconds(20) // Out <= In
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void TrimOptions_ThrowsOnMissingPaths()
    {
        var options = new TrimOptions
        {
            InputFilePath = "",
            OutputFilePath = @"C:\media\video_trimmed.mp4",
            InPoint = TimeSpan.FromSeconds(0),
            OutPoint = TimeSpan.FromSeconds(10)
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void DownloadOptions_ValidatesUrl_AndDefaultsFolder()
    {
        var options = new DownloadOptions
        {
            Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            OutputDirectory = ""
        };

        options.Validate(); // Should succeed and populate OutputDirectory
        Assert.NotEmpty(options.OutputDirectory);
    }

    [Fact]
    public void DownloadOptions_ThrowsOnInvalidUrl()
    {
        var options = new DownloadOptions
        {
            Url = "not-a-valid-url"
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void MediaTrimmerService_FindBinary_DoesNotThrow()
    {
        var path = MediaTrimmerService.FindFfmpegBinary();
        // May be null if not yet downloaded into libs/, but method must never throw unhandled exception
        Assert.True(path == null || path.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MediaDownloaderService_FindBinary_DoesNotThrow()
    {
        var path = MediaDownloaderService.FindYtDlpBinary();
        // May be null if not yet downloaded into libs/, but method must never throw unhandled exception
        Assert.True(path == null || path.EndsWith("yt-dlp.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(DownloadFormatPreset.BestAvailable)]
    [InlineData(DownloadFormatPreset.Max2160p_4K)]
    [InlineData(DownloadFormatPreset.Max1440p_2K)]
    [InlineData(DownloadFormatPreset.Max1080p)]
    [InlineData(DownloadFormatPreset.Max720p)]
    [InlineData(DownloadFormatPreset.Max480p)]
    [InlineData(DownloadFormatPreset.Max360p)]
    [InlineData(DownloadFormatPreset.AudioOnlyMp3)]
    [InlineData(DownloadFormatPreset.AudioOnlyM4a)]
    [InlineData(DownloadFormatPreset.AudioOnlyFlac)]
    [InlineData(DownloadFormatPreset.AudioOnlyBest)]
    public void DownloadOptions_SupportsAllFormatPresets(DownloadFormatPreset preset)
    {
        var options = new DownloadOptions
        {
            Url = "https://example.com/video",
            Format = preset
        };

        options.Validate();
        Assert.Equal(preset, options.Format);
    }

    [Fact]
    public async System.Threading.Tasks.Task MediaDownloaderService_DownloadAsync_HandlesPreCancelledToken()
    {
        var downloader = new MediaDownloaderService();
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var options = new DownloadOptions
        {
            Url = "https://example.com/stream",
            Format = DownloadFormatPreset.Max1080p
        };

        if (downloader.IsYtDlpAvailable)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await downloader.DownloadAsync(options, ct: cts.Token);
            });
        }
        else
        {
            await Assert.ThrowsAsync<System.IO.FileNotFoundException>(async () =>
            {
                await downloader.DownloadAsync(options, ct: cts.Token);
            });
        }
    }
}
