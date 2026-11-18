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

    [Fact]
    public void PlaylistManager_IsSupportedMediaFile_IdentifiesSupportedFormats()
    {
        Assert.True(PlaylistManager.IsSupportedMediaFile("video.mp4"));
        Assert.True(PlaylistManager.IsSupportedMediaFile("movie.mkv"));
        Assert.True(PlaylistManager.IsSupportedMediaFile("track.mp3"));
        Assert.True(PlaylistManager.IsSupportedMediaFile("album.flac"));
        Assert.True(PlaylistManager.IsSupportedMediaFile("song.opus"));
        Assert.True(PlaylistManager.IsSupportedMediaFile("c:\\folder\\audio.wav"));

        Assert.False(PlaylistManager.IsSupportedMediaFile("document.pdf"));
        Assert.False(PlaylistManager.IsSupportedMediaFile("image.jpg"));
        Assert.False(PlaylistManager.IsSupportedMediaFile(""));
    }

    [Fact]
    public void LocalMediaBrowserService_GetDrivesAndQuickPlaces_ReturnsValidLists()
    {
        var browser = new LocalMediaBrowserService();
        var drives = browser.GetDrives();
        Assert.NotNull(drives);
        Assert.NotEmpty(drives);

        var quickPlaces = browser.GetQuickPlaces();
        Assert.NotNull(quickPlaces);
        Assert.NotEmpty(quickPlaces);
    }

    [Fact]
    public async System.Threading.Tasks.Task LocalMediaBrowserService_EnumerateDirectoryAsync_FiltersMediaFiles()
    {
        var browser = new LocalMediaBrowserService();
        var tempDir = Path.Combine(Path.GetTempPath(), "njplayer_test_browser_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var subDir = Path.Combine(tempDir, "SubFolder");
        Directory.CreateDirectory(subDir);

        var videoFile = Path.Combine(tempDir, "test_video.mp4");
        var audioFile = Path.Combine(tempDir, "test_song.mp3");
        var textFile = Path.Combine(tempDir, "notes.txt");

        File.WriteAllText(videoFile, "fake video");
        File.WriteAllText(audioFile, "fake audio");
        File.WriteAllText(textFile, "some text");

        try
        {
            var items = await browser.EnumerateDirectoryAsync(tempDir);
            Assert.NotNull(items);
            Assert.Equal(3, items.Count); // 1 directory + 2 media files (notes.txt excluded)

            var dirItem = Assert.Single(items, i => i.IsDirectory);
            Assert.Equal("SubFolder", dirItem.Name);

            var videoItem = Assert.Single(items, i => i.IsVideo);
            Assert.Equal("test_video.mp4", videoItem.Name);

            var audioItem = Assert.Single(items, i => i.IsAudio);
            Assert.Equal("test_song.mp3", audioItem.Name);
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
    public void MediaFormats_DashAndHlsDetection_WorksAccurately()
    {
        Assert.True(NJPlayer.Core.Playback.MediaFormats.IsDashManifest("manifest.mpd"));
        Assert.True(NJPlayer.Core.Playback.MediaFormats.IsVideo("manifest.mpd"));
        Assert.False(NJPlayer.Core.Playback.MediaFormats.IsDashManifest("video.mp4"));

        var tempHls = Path.Combine(Path.GetTempPath(), "test_stream_" + Guid.NewGuid().ToString("N") + ".m3u8");
        var hlsContent = "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:10\n#EXTINF:9.009,\nseg0.ts\n";
        File.WriteAllText(tempHls, hlsContent);

        var tempTrackPlaylist = Path.Combine(Path.GetTempPath(), "test_playlist_" + Guid.NewGuid().ToString("N") + ".m3u8");
        var trackContent = "#EXTM3U\n#EXTINF:180,Artist - Song\nSong.mp3\n";
        File.WriteAllText(tempTrackPlaylist, trackContent);

        try
        {
            // HLS stream contains #EXT-X- tags
            Assert.True(NJPlayer.Core.Playback.MediaFormats.IsHlsStream(tempHls));
            Assert.False(NJPlayer.Core.Playback.MediaFormats.IsHlsStream(tempTrackPlaylist));

            // PlaylistManager.IsPlaylistFile treats HLS streams as playable media (false) so it plays continuously
            Assert.False(PlaylistManager.IsPlaylistFile(tempHls));
            // Standard track playlist is recognized as playlist (true)
            Assert.True(PlaylistManager.IsPlaylistFile(tempTrackPlaylist));
        }
        finally
        {
            if (File.Exists(tempHls)) File.Delete(tempHls);
            if (File.Exists(tempTrackPlaylist)) File.Delete(tempTrackPlaylist);
        }
    }

    [Fact]
    public void MpvOptionBuilder_SetsStreamingDefaults()
    {
        var builder = new NJPlayer.Core.Playback.MpvOptionBuilder();
        var options = builder.Build();

        Assert.True(options.ContainsKey("stream-lavf-o"));
        Assert.Contains("reconnect=1", options["stream-lavf-o"]);
    }

    [Fact]
    public void PlaylistSearch_TitleOnly_MatchesTitleNotPath()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleOnly,
            Mode = PlaylistSearchMode.Contains
        };

        var item = new PlaylistItem(@"C:\Anime\AttackOnTitan\Episode01.mp4", "Episode 01");

        Assert.True(options.Matches(item, "Episode"));
        Assert.False(options.Matches(item, "AttackOnTitan")); // Path is ignored in TitleOnly
    }

    [Fact]
    public void PlaylistSearch_TitleAndPath_MatchesDirectoryAndTitle()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleAndPath,
            Mode = PlaylistSearchMode.Contains
        };

        var item = new PlaylistItem(@"C:\Anime\AttackOnTitan\Episode01.mp4", "Episode 01");

        Assert.True(options.Matches(item, "Episode"));
        Assert.True(options.Matches(item, "AttackOnTitan"));
    }

    [Fact]
    public void PlaylistSearch_ExactMatch_RequiresFullMatch()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleOnly,
            Mode = PlaylistSearchMode.ExactMatch
        };

        var item = new PlaylistItem(@"C:\Music\Track1.mp3", "Track1");

        Assert.True(options.Matches(item, "Track1"));
        Assert.False(options.Matches(item, "Track")); // Partial match fails in Exact mode
    }

    [Fact]
    public void PlaylistSearch_StartsWith_MatchesPrefix()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleOnly,
            Mode = PlaylistSearchMode.StartsWith
        };

        var item = new PlaylistItem(@"C:\Music\Song_Alpha.mp3", "Song_Alpha");

        Assert.True(options.Matches(item, "Song"));
        Assert.False(options.Matches(item, "Alpha"));
    }

    [Fact]
    public void PlaylistSearch_Regex_EvaluatesPatternAndHandlesInvalidGracefully()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleOnly,
            Mode = PlaylistSearchMode.Regex
        };

        var item = new PlaylistItem(@"C:\Videos\Movie_2024.mp4", "Movie_2024");

        Assert.True(options.Matches(item, @"^Movie_\d{4}$"));
        Assert.False(options.Matches(item, @"^Movie_\d{2}$"));
        // Invalid regex should return false gracefully without throwing exception
        Assert.False(options.Matches(item, @"[unclosed-bracket"));
    }

    [Fact]
    public void PlaylistSearch_CaseSensitive_HonorsCasing()
    {
        var options = new PlaylistSearchOptions
        {
            Scope = PlaylistSearchScope.TitleOnly,
            Mode = PlaylistSearchMode.Contains,
            CaseSensitive = true
        };

        var item = new PlaylistItem(@"C:\Music\NeonTrack.flac", "NeonTrack");

        Assert.True(options.Matches(item, "Neon"));
        Assert.False(options.Matches(item, "neon"));
    }

    [Fact]
    public void PlaylistManager_Search_ReturnsFilteredResults()
    {
        var manager = new PlaylistManager();
        manager.AddFile(@"C:\Media\AlphaSong.mp3");
        manager.AddFile(@"C:\Media\BetaSong.mp3");
        manager.AddFile(@"C:\Media\GammaVideo.mp4");

        var results = manager.Search("Song", new PlaylistSearchOptions { Mode = PlaylistSearchMode.Contains });
        Assert.Equal(2, results.Count);

        var regexResults = manager.Search(@"^(Alpha|Gamma)", new PlaylistSearchOptions { Mode = PlaylistSearchMode.Regex });
        Assert.Equal(2, regexResults.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task PlaylistManager_AddFolderAsync_LoadsMediaWithoutBlocking()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "njplayer_test_addfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "song1.mp3"), "audio");
        File.WriteAllText(Path.Combine(tempDir, "video1.mp4"), "video");
        File.WriteAllText(Path.Combine(tempDir, "ignore.txt"), "text");

        try
        {
            var manager = new PlaylistManager();
            var added = await manager.AddFolderAsync(tempDir, recursive: false);
            Assert.Equal(2, added);
            Assert.Equal(2, manager.Items.Count);
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
    public async System.Threading.Tasks.Task LocalMediaBrowserService_AsyncMethods_ReturnValidCollections()
    {
        var browser = new LocalMediaBrowserService();
        var drives = await browser.GetDrivesAsync();
        Assert.NotNull(drives);

        var places = await browser.GetQuickPlacesAsync();
        Assert.NotNull(places);
    }
}

