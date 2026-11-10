using System;
using System.IO;
using NJPlayer.Core.Playlist;
using Xunit;

namespace NJPlayer.Tests;

public class PlaylistTests
{
    [Fact]
    public void PlaylistManager_AddAndRemoveItems_ModifiesCollectionProperly()
    {
        var manager = new PlaylistManager();
        var item1 = manager.AddFile("C:\\videos\\clip1.mp4", "Clip 1", TimeSpan.FromMinutes(2));
        var item2 = manager.AddFile("C:\\videos\\clip2.mp4", "Clip 2", TimeSpan.FromMinutes(4));

        Assert.Equal(2, manager.Items.Count);
        Assert.Equal("Clip 1", manager.Items[0].Title);
        Assert.Equal("Clip 2", manager.Items[1].Title);

        manager.Remove(item1);
        Assert.Single(manager.Items);
        Assert.Equal("Clip 2", manager.Items[0].Title);
    }

    [Fact]
    public void PlaylistManager_MoveItem_ReordersCorrectly()
    {
        var manager = new PlaylistManager();
        manager.AddFile("C:\\track1.mp3", "Track 1");
        manager.AddFile("C:\\track2.mp3", "Track 2");
        manager.AddFile("C:\\track3.mp3", "Track 3");

        manager.Move(0, 2);

        Assert.Equal("Track 2", manager.Items[0].Title);
        Assert.Equal("Track 3", manager.Items[1].Title);
        Assert.Equal("Track 1", manager.Items[2].Title);
    }

    [Fact]
    public void PlaylistManager_NextAndPrevious_HonorsRepeatModes()
    {
        var manager = new PlaylistManager();
        manager.AddFile("C:\\file1.mp4", "File 1");
        manager.AddFile("C:\\file2.mp4", "File 2");
        manager.SetCurrentIndex(0);

        // Next item
        Assert.True(manager.Next());
        Assert.Equal(1, manager.CurrentIndex);

        // RepeatMode.None: at end of playlist, Next returns false
        Assert.False(manager.Next());
        Assert.Equal(1, manager.CurrentIndex);

        // RepeatMode.RepeatAll: wraps around to 0
        manager.RepeatMode = RepeatMode.RepeatAll;
        Assert.True(manager.Next());
        Assert.Equal(0, manager.CurrentIndex);

        // RepeatMode.RepeatOne: stays on same item
        manager.RepeatMode = RepeatMode.RepeatOne;
        Assert.True(manager.Next());
        Assert.Equal(0, manager.CurrentIndex);
    }

    [Fact]
    public void PlaylistManager_Shuffle_PermutesAndMaintainsState()
    {
        var manager = new PlaylistManager();
        for (var i = 1; i <= 10; i++)
        {
            manager.AddFile($"C:\\track{i}.mp3", $"Track {i}");
        }

        manager.IsShuffled = true;
        manager.SetCurrentIndex(0);

        Assert.True(manager.Next());
        Assert.InRange(manager.CurrentIndex, 0, 9);
    }

    [Fact]
    public void PlaylistManager_M3uSaveAndLoad_RoundtripsEntries()
    {
        var tempM3u = Path.Combine(Path.GetTempPath(), $"njplayer_test_{Guid.NewGuid()}.m3u");
        try
        {
            var manager1 = new PlaylistManager();
            manager1.AddFile("C:\\sample1.mp4", "Sample 1", TimeSpan.FromSeconds(120));
            manager1.AddFile("C:\\sample2.mp4", "Sample 2", TimeSpan.FromSeconds(240));
            manager1.SaveM3u(tempM3u);

            var content = File.ReadAllText(tempM3u);
            Assert.Contains("#EXTM3U", content);
            Assert.Contains("#EXTINF:120,Sample 1", content);
            Assert.Contains("C:\\sample1.mp4", content);

            var manager2 = new PlaylistManager();
            manager2.LoadM3u(tempM3u);

            Assert.Equal(2, manager2.Items.Count);
            Assert.Equal("Sample 1", manager2.Items[0].Title);
            Assert.Equal("Sample 2", manager2.Items[1].Title);
        }
        finally
        {
            if (File.Exists(tempM3u)) File.Delete(tempM3u);
        }
    }

    [Fact]
    public void PlaylistManager_M3u8WithWebStreamsAndRelativePaths_ParsesProperly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"njplayer_m3u8_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var localFile = Path.Combine(tempDir, "song.mp3");
        File.WriteAllText(localFile, "dummy audio content");

        var tempM3u8 = Path.Combine(tempDir, "playlist.m3u8");
        var m3u8Content =
            "#EXTM3U\n" +
            "#EXTINF:180,Local Song\n" +
            "song.mp3\n" +
            "#EXTINF:-1,Live Radio Stream\n" +
            "https://stream.example.com:8000/live.aac\n" +
            "#EXTINF:420,Unicode Track 🎵 ሙዚቃ\n" +
            "https://cdn.example.org/audio/music.mp3\n";

        File.WriteAllText(tempM3u8, m3u8Content, System.Text.Encoding.UTF8);

        try
        {
            Assert.True(PlaylistManager.IsPlaylistFile(tempM3u8));
            Assert.True(PlaylistManager.IsPlaylistFile("test.m3u"));
            Assert.False(PlaylistManager.IsPlaylistFile("movie.mp4"));

            var manager = new PlaylistManager();
            var count = manager.LoadM3u(tempM3u8);

            Assert.Equal(3, count);
            Assert.Equal(3, manager.Items.Count);

            // Item 0: relative path resolved to tempDir
            Assert.Equal(localFile, manager.Items[0].FilePath);
            Assert.Equal("Local Song", manager.Items[0].Title);
            Assert.Equal(TimeSpan.FromSeconds(180), manager.Items[0].Duration);

            // Item 1: web stream URL preserved intact
            Assert.Equal("https://stream.example.com:8000/live.aac", manager.Items[1].FilePath);
            Assert.Equal("Live Radio Stream", manager.Items[1].Title);

            // Item 2: UTF-8 Unicode title
            Assert.Equal("https://cdn.example.org/audio/music.mp3", manager.Items[2].FilePath);
            Assert.Equal("Unicode Track 🎵 ሙዚቃ", manager.Items[2].Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}

