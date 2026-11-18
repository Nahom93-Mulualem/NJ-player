using System.Collections.Generic;

namespace NJPlayer.Core.Settings;

/// <summary>
/// Serializable application settings model for NJ Player.
/// </summary>
public sealed class AppSettings
{
    public bool ResumePlayback { get; set; } = true;
    public string HardwareDecoder { get; set; } = "auto-safe";
    public bool RememberWindowPosition { get; set; } = true;

    // Window geometry
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double WindowWidth { get; set; } = 980;
    public double WindowHeight { get; set; } = 580;
    public bool IsWindowMaximized { get; set; } = false;

    // Audio & playback defaults
    public double Volume { get; set; } = 100;
    public double Speed { get; set; } = 1.0;
    public bool IsMuted { get; set; } = false;

    // Recent files & per-file resume tracking
    public List<string> RecentFiles { get; set; } = new();
    public Dictionary<string, double> ResumePositions { get; set; } = new();

    // Audio & Equalizer configuration
    public AudioConfig Audio { get; set; } = new();

    // Radio & Visualizer configuration
    public List<string> RadioFavorites { get; set; } = new();
    public string VisualizerMode { get; set; } = "showcqt";
    public string? VisualizerBackgroundPath { get; set; }
    public string VisualizerType { get; set; } = "bars";

    // Codec & OpenCodec configuration
    public string VideoDecoderPriority { get; set; } = "auto";
    public string AudioDecoderPriority { get; set; } = "auto";
    public string CustomLavcOptions { get; set; } = string.Empty;
    public bool EnableMpeg4Dxva { get; set; } = true;
    public string HwdecCodecs { get; set; } = "all";

    // Playlist search configuration
    public NJPlayer.Core.Playlist.PlaylistSearchScope PlaylistSearchScope { get; set; } = NJPlayer.Core.Playlist.PlaylistSearchScope.TitleOnly;
    public NJPlayer.Core.Playlist.PlaylistSearchMode PlaylistSearchMode { get; set; } = NJPlayer.Core.Playlist.PlaylistSearchMode.Contains;
    public bool PlaylistSearchCaseSensitive { get; set; } = false;
}
