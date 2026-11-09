using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NJPlayer.Features.Subtitles;
using Xunit;

namespace NJPlayer.Tests;

public class SubtitleTests : IDisposable
{
    private readonly string _testDir;

    public SubtitleTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NJPlayer_SubTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SubtitleHashCalculator_ComputesValidHexHash_ForLargeFile()
    {
        var testFile = Path.Combine(_testDir, "test_movie.mp4");
        // Create 128 KB test file
        var data = new byte[131072];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);
        await File.WriteAllBytesAsync(testFile, data);

        var hash = await SubtitleHashCalculator.ComputeHashAsync(testFile);

        Assert.NotNull(hash);
        Assert.Equal(16, hash.Length); // 64-bit hex string is 16 chars
    }

    [Fact]
    public async Task SubtitleHashCalculator_ReturnsEmpty_ForSmallFile()
    {
        var smallFile = Path.Combine(_testDir, "small.mp4");
        await File.WriteAllBytesAsync(smallFile, new byte[1024]); // < 64 KB

        var hash = await SubtitleHashCalculator.ComputeHashAsync(smallFile);

        Assert.Equal(string.Empty, hash);
    }

    [Fact]
    public void SubtitleService_FindsMatchingSidecarSubtitles()
    {
        var service = new SubtitleService();
        var videoPath = Path.Combine(_testDir, "movie.mkv");
        File.WriteAllText(videoPath, "");

        var subDefault = Path.Combine(_testDir, "movie.srt");
        var subEn = Path.Combine(_testDir, "movie.en.srt");
        var subAmharic = Path.Combine(_testDir, "movie.am.srt");
        var subOther = Path.Combine(_testDir, "other_show.srt");

        File.WriteAllText(subDefault, "");
        File.WriteAllText(subEn, "");
        File.WriteAllText(subAmharic, "");
        File.WriteAllText(subOther, "");

        var matches = service.FindMatchingSubtitles(videoPath);

        Assert.Contains(subDefault, matches);
        Assert.Contains(subEn, matches);
        Assert.Contains(subAmharic, matches);
        Assert.DoesNotContain(subOther, matches);
    }

    [Fact]
    public void SubtitleService_DetectsUtf8_IncludingGeezAmharicText()
    {
        var geezFile = Path.Combine(_testDir, "amharic_sub.srt");
        // "ሰላም ዓለም" (Hello World in Ge'ez / Amharic) in UTF-8
        File.WriteAllText(geezFile, "1\n00:00:01,000 --> 00:00:04,000\nሰላም ዓለም\n", Encoding.UTF8);

        var encoding = SubtitleService.DetectEncoding(geezFile);

        Assert.Equal(Encoding.UTF8, encoding);
    }

    [Fact]
    public void SubtitleService_DetectsUtf8WithBom()
    {
        var bomFile = Path.Combine(_testDir, "bom.srt");
        var content = Encoding.UTF8.GetPreamble();
        File.WriteAllBytes(bomFile, content);

        var encoding = SubtitleService.DetectEncoding(bomFile);

        Assert.Equal(Encoding.UTF8, encoding);
    }

    [Fact]
    public async Task PodnapisiProvider_HandlesEmptyQueriesGracefully()
    {
        var provider = new PodnapisiSubtitleProvider();

        var emptyResults = await provider.SearchByQueryAsync("");
        Assert.Empty(emptyResults);

        var nullDownload = await provider.DownloadSubtitleAsync(null!, _testDir);
        Assert.Null(nullDownload);
    }
}

