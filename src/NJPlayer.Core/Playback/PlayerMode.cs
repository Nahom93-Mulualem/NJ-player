using System;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Active player modes ensuring strict separation between video playback and music playback.
/// </summary>
public enum PlayerMode
{
    Video,
    Music
}

/// <summary>
/// Snapshot of playback state for a player mode (file path, position, duration, and play state).
/// </summary>
public sealed class PlaybackModeState
{
    public string? FilePath { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool WasPlaying { get; set; }

    public void Clear()
    {
        FilePath = null;
        PositionSeconds = 0;
        DurationSeconds = 0;
        WasPlaying = false;
    }
}
