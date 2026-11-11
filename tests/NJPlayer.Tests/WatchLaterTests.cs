using System;
using System.IO;
using NJPlayer.Core.Playlist;
using Xunit;

namespace NJPlayer.Tests;

public class WatchLaterTests
{
    [Fact]
    public void Add_AppendsItemAndCalculatesFormattedDuration()
    {
        var manager = new WatchLaterManager();
        manager.Clear();

        var added = manager.Add("C:\\videos\\sample_movie.mp4", "Sample Movie", 3665);

        Assert.True(added);
        Assert.Single(manager.Items);

        var item = manager.Items[0];
        Assert.Equal("Sample Movie", item.Title);
        Assert.Equal("1:01:05", item.FormattedDuration);
        Assert.False(item.IsWatched);
    }

    [Fact]
    public void Add_DuplicatePaths_Rejected()
    {
        var manager = new WatchLaterManager();
        manager.Clear();

        Assert.True(manager.Add("C:\\videos\\sample.mp4", "Sample 1"));
        Assert.False(manager.Add("C:\\videos\\sample.mp4", "Sample Duplicate"));
        Assert.Single(manager.Items);
    }

    [Fact]
    public void Move_ReordersItemsCorrectly()
    {
        var manager = new WatchLaterManager();
        manager.Clear();

        manager.Add("C:\\video1.mp4", "Video 1");
        manager.Add("C:\\video2.mp4", "Video 2");
        manager.Add("C:\\video3.mp4", "Video 3");

        manager.Move(0, 2);

        Assert.Equal("Video 2", manager.Items[0].Title);
        Assert.Equal("Video 3", manager.Items[1].Title);
        Assert.Equal("Video 1", manager.Items[2].Title);
    }

    [Fact]
    public void MarkWatched_UpdatesWatchedStatus()
    {
        var manager = new WatchLaterManager();
        manager.Clear();

        manager.Add("C:\\video1.mp4", "Video 1");
        manager.MarkWatched("C:\\video1.mp4", true);

        Assert.True(manager.Items[0].IsWatched);
    }

    [Fact]
    public void JsonRoundtrip_PreservesWatchLaterItems()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"watch_later_test_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new WatchLaterManager();
            manager.Clear();
            manager.Add("C:\\vids\\clip1.mkv", "Clip 1", 120);
            manager.Add("C:\\vids\\clip2.mkv", "Clip 2", 240);
            manager.SaveToJson(tempFile);

            var manager2 = new WatchLaterManager();
            manager2.LoadFromJson(tempFile);

            Assert.Equal(2, manager2.Items.Count);
            Assert.Equal("Clip 1", manager2.Items[0].Title);
            Assert.Equal("Clip 2", manager2.Items[1].Title);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

