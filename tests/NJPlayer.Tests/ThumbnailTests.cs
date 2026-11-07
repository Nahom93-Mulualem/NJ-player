using NJPlayer.Core.Thumbnails;
using Xunit;

namespace NJPlayer.Tests;

public class ThumbnailTests
{
    [Theory]
    [InlineData("video.mp4", 0.0, "video.mp4|bucket_0")]
    [InlineData("video.mp4", 1.5, "video.mp4|bucket_0")]
    [InlineData("video.mp4", 4.99, "video.mp4|bucket_0")]
    [InlineData("video.mp4", 5.0, "video.mp4|bucket_1")]
    [InlineData("video.mp4", 7.8, "video.mp4|bucket_1")]
    [InlineData("video.mp4", 9.99, "video.mp4|bucket_1")]
    [InlineData("video.mp4", 10.0, "video.mp4|bucket_2")]
    [InlineData("video.mp4", 14.5, "video.mp4|bucket_2")]
    [InlineData("video.mp4", 15.0, "video.mp4|bucket_3")]
    [InlineData("C:\\movies\\sample.mkv", 62.0, "C:\\movies\\sample.mkv|bucket_12")]
    public void GetBucketKey_Calculates5SecondBuckets_Correctly(string file, double time, string expectedKey)
    {
        var key = FfmpegThumbnailService.GetBucketKey(file, time);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void GetBucketKey_NegativeTime_ClampsToBucketZero()
    {
        var key = FfmpegThumbnailService.GetBucketKey("test.mp4", -10.0);
        Assert.Equal("test.mp4|bucket_0", key);
    }

    [Fact]
    public void ThumbnailService_ClearCache_EmptiesCacheSuccessfully()
    {
        var service = new FfmpegThumbnailService();
        service.ClearCache();
        // Verifies no exception thrown
    }
}
