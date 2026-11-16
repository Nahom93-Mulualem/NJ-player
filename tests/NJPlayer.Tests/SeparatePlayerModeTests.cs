using System;
using System.IO;
using NJPlayer.Core.Playback;
using NJPlayer.Core.Playlist;
using Xunit;

namespace NJPlayer.Tests;

public class SeparatePlayerModeTests
{
    [Fact]
    public void PlaybackModeState_SavesAndClearsCorrectly()
    {
        var state = new PlaybackModeState
        {
            FilePath = "C:\\Media\\song.mp3",
            PositionSeconds = 45.2,
            DurationSeconds = 180.0,
            WasPlaying = true
        };

        Assert.Equal("C:\\Media\\song.mp3", state.FilePath);
        Assert.Equal(45.2, state.PositionSeconds);
        Assert.Equal(180.0, state.DurationSeconds);
        Assert.True(state.WasPlaying);

        state.Clear();

        Assert.Null(state.FilePath);
        Assert.Equal(0, state.PositionSeconds);
        Assert.Equal(0, state.DurationSeconds);
        Assert.False(state.WasPlaying);
    }

    [Fact]
    public void SeparateQueues_DoNotInterfere()
    {
        var videoQueue = new PlaylistManager();
        var musicQueue = new PlaylistManager();

        videoQueue.AddFile("C:\\Videos\\movie.mp4");
        videoQueue.AddFile("C:\\Videos\\clip.mkv");

        musicQueue.AddFile("C:\\Music\\track1.mp3");
        musicQueue.AddFile("C:\\Music\\track2.flac");
        musicQueue.AddFile("C:\\Music\\track3.m4a");

        Assert.Equal(2, videoQueue.Items.Count);
        Assert.Equal(3, musicQueue.Items.Count);

        // Advancing video playlist does not touch music queue
        videoQueue.SetCurrentIndex(0);
        Assert.True(videoQueue.Next());
        Assert.Equal(1, videoQueue.CurrentIndex);

        // Music queue pointer is unaffected
        Assert.Equal(-1, musicQueue.CurrentIndex);
        musicQueue.SetCurrentIndex(0);
        Assert.Equal(0, musicQueue.CurrentIndex);
        Assert.Equal("C:\\Music\\track1.mp3", musicQueue.CurrentItem?.FilePath);
    }

    [Fact]
    public void ModeSeparation_AudioAndVideoStatesPreserved()
    {
        var videoState = new PlaybackModeState
        {
            FilePath = "movie.mp4",
            PositionSeconds = 120.5,
            DurationSeconds = 5400.0,
            WasPlaying = true
        };

        var musicState = new PlaybackModeState
        {
            FilePath = "song.flac",
            PositionSeconds = 42.0,
            DurationSeconds = 210.0,
            WasPlaying = true
        };

        // When switching to video, music position is untouched
        Assert.Equal(42.0, musicState.PositionSeconds);
        Assert.Equal("song.flac", musicState.FilePath);

        // When switching to music, video position is untouched
        Assert.Equal(120.5, videoState.PositionSeconds);
        Assert.Equal("movie.mp4", videoState.FilePath);
    }
}
