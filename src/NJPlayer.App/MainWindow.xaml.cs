using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NJPlayer.App.Overlay;
using NJPlayer.Core.Playback;
using NJPlayer.Core.Settings;

namespace NJPlayer.App;

/// <summary>
/// Frameless main player window hosting the Win32 mpv video surface and managing the airspace controls overlay.
/// </summary>
public partial class MainWindow : Window
{
    private MpvPlayer? _player;
    private ControlsOverlayWindow? _overlay;
    private readonly NJPlayer.Core.Hotkeys.HotkeyManager _hotkeyManager = new();
    private readonly NJPlayer.Core.Playlist.PlaylistManager _videoPlaylistManager = new();
    private readonly NJPlayer.Core.Playlist.PlaylistManager _musicPlaylistManager = new();
    private readonly NJPlayer.Core.Playback.PlaybackModeState _videoState = new();
    private readonly NJPlayer.Core.Playback.PlaybackModeState _musicState = new();
    private NJPlayer.Core.Playback.PlayerMode _currentMode = NJPlayer.Core.Playback.PlayerMode.Video;
    private NJPlayer.Core.Playlist.PlaylistManager _playlistManager => ActivePlaylistManager;
    public NJPlayer.Core.Playlist.PlaylistManager ActivePlaylistManager =>
        _currentMode == NJPlayer.Core.Playback.PlayerMode.Music ? _musicPlaylistManager : _videoPlaylistManager;
    public NJPlayer.Core.Playback.PlayerMode CurrentMode => _currentMode;
    private readonly NJPlayer.Core.Playlist.WatchLaterManager _watchLaterManager = new();
    private readonly SettingsManager _settingsManager = new();
    private readonly NJPlayer.Features.Subtitles.SubtitleService _subtitleService = new();
    private readonly NJPlayer.Features.Trimmer.MediaTrimmerService _trimmerService = new();
    private readonly NJPlayer.Features.Downloader.MediaDownloaderService _downloaderService = new();
    private NJPlayer.Features.Audio.IAudioSuite? _audioSuite;
    private readonly NJPlayer.Features.Audio.AudioMetadataService _audioMetadataService = new();
    private NJPlayer.Features.Subtitles.LrcDocument? _currentLyrics;
    private CancellationTokenSource? _audioDetailsCts;
    private CancellationTokenSource? _visualizerBgCts;
    private bool _isAudioMode = false;
    private bool _musicVisualizerActive = false;
    private Views.AudioPanelWindow? _audioPanel;
    private Views.SubtitlePickerWindow? _subtitlePicker;
    private Views.TrimmerWindow? _trimmerWindow;
    private Views.DownloaderWindow? _downloaderWindow;
    private NJPlayer.Features.Radio.RadioBrowserService? _radioService;
    private Views.RadioPanelWindow? _radioPanelWindow;
    private string _currentVisualizerMode = "showcqt";

    private readonly Stopwatch? _benchStopwatch;
    private bool _benchMeasured = false;
    private string? _currentFilePath;
    private double? _pendingResumePosition;

    private bool _isDllAvailable = true;
    private bool _isFullscreen = false;
    private bool _isAlwaysOnTop = false;
    private double _lastNonMuteVolume = 100;
    private int _subDelayMs = 0;

    private WindowState _savedState = WindowState.Normal;
    private WindowStyle _savedStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _savedResize = ResizeMode.CanResize;
    private double _savedLeft = 100;
    private double _savedTop = 100;
    private double _savedWidth = 980;
    private double _savedHeight = 580;

    public bool IsFullscreen => _isFullscreen;
    public bool IsVisualizerActive => _musicVisualizerActive;
    public bool IsAudioMode => _isAudioMode;
    public SettingsManager SettingsManager => _settingsManager;

    public MainWindow(Stopwatch? benchStopwatch = null)
    {
        _benchStopwatch = benchStopwatch;
        InitializeComponent();

        // Restore window geometry if enabled
        if (_settingsManager.Settings.RememberWindowPosition)
        {
            RestoreWindowGeometry();
        }

        LocationChanged += (_, _) => _overlay?.SyncWithMainWindow();
        SizeChanged += (_, _) => _overlay?.SyncWithMainWindow();
        StateChanged += MainWindow_StateChanged;

        _visualizerAnimTimer.Tick += VisualizerAnimTimer_Tick;

        PlayerHost.HwndCreated += (_, hwnd) =>
        {
            try
            {
                _player?.SetPropertyString("wid", hwnd.ToInt64().ToString());
                _player?.SetPropertyString("vo", "gpu");
            }
            catch { }
        };
    }

    private void RestoreWindowGeometry()
    {
        var s = _settingsManager.Settings;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (s.WindowWidth >= 400 && s.WindowHeight >= 300 &&
            s.WindowLeft >= -s.WindowWidth + 100 && s.WindowLeft < virtualWidth - 100 &&
            s.WindowTop >= 0 && s.WindowTop < virtualHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        if (s.IsWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        WindowOutline.BorderThickness = WindowState == WindowState.Maximized || _isFullscreen
            ? new Thickness(0)
            : new Thickness(1);

        _overlay?.SyncWithMainWindow();
    }

    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGED)
        {
            _overlay?.SyncWithMainWindow();
        }
        else if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var rcWork = monitorInfo.rcWork;
                var rcMonitor = monitorInfo.rcMonitor;

                if (_isFullscreen)
                {
                    mmi.ptMaxPosition.X = 0;
                    mmi.ptMaxPosition.Y = 0;
                    mmi.ptMaxSize.X = rcMonitor.Width;
                    mmi.ptMaxSize.Y = rcMonitor.Height;
                }
                else
                {
                    mmi.ptMaxPosition.X = rcWork.Left - rcMonitor.Left;
                    mmi.ptMaxPosition.Y = rcWork.Top - rcMonitor.Top;
                    mmi.ptMaxSize.X = rcWork.Width;
                    mmi.ptMaxSize.Y = rcWork.Height;
                }

                // Minimum window size: 480x270 across high-DPI scaling per Issue 3
                var source = PresentationSource.FromVisual(this);
                var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                mmi.ptMinTrackSize.X = (int)(480 * dpiX);
                mmi.ptMinTrackSize.Y = (int)(270 * dpiY);

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            }
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializePlayer();

        try
        {
            _hotkeyManager.LoadFromFile();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not load hotkeys.json: {ex.Message}");
        }

        // Create and display the airspace-safe controls overlay window
        _overlay = new ControlsOverlayWindow(this);
        if (_player != null)
        {
            _overlay.AttachPlayer(_player);
        }

        try
        {
            _videoPlaylistManager.LoadFromJson();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not load playlist.json: {ex.Message}");
        }

        try
        {
            _musicPlaylistManager.LoadFromJson(GetMusicQueueFilePath());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not load music_queue.json: {ex.Message}");
        }

        _overlay.AttachPlaylist(ActivePlaylistManager);
        _videoPlaylistManager.CurrentItemChanged += PlaylistManager_CurrentItemChanged;
        _musicPlaylistManager.CurrentItemChanged += PlaylistManager_CurrentItemChanged;

        try
        {
            _watchLaterManager.LoadFromJson();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not load watch_later.json: {ex.Message}");
        }

        _overlay.AttachWatchLater(_watchLaterManager);

        _overlay.Show();
        _overlay.SyncWithMainWindow();

        // Hook Win32 window message loop for tear-free synchronous overlay tracking
        var source = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
        source?.AddHook(WndProc);

        UpdateRecentFilesList();

        // Enforce clean startup defaults (Item 2)
        ResetStartupDefaults();

        // Measure benchmark startup to first window load
        if (_benchStopwatch != null && !_benchMeasured)
        {
            _benchMeasured = true;
            _benchStopwatch.Stop();
            var ms = _benchStopwatch.ElapsedMilliseconds;
            Trace.WriteLine($"[BENCH] Cold start to first window render: {ms} ms");
            Console.WriteLine($"[BENCH] Cold start to first window render: {ms} ms");
            _overlay?.ShowToast($"Cold Start: {ms} ms", "⚡");
        }
    }

    private void ResetStartupDefaults()
    {
        // 1. Reset video color adjustments (brightness, contrast, saturation, gamma) to 0
        ResetVideoColors(showToast: false);

        // 2. Clear any lingering video filters (e.g. 3D, VR)
        if (_player != null && _player.IsInitialized)
        {
            try
            {
                _player.SetPropertyString("vf", "");
                _player.SetPropertyDouble("speed", 1.0);
            }
            catch { }
        }

        // 3. Default view: Cover view (album cover, not full-screen visualizer)
        _musicVisualizerActive = false;
        VisualizerStripBorder.Height = 56;
        ToggleMusicVisualizerBtn.Content = "📊 Visualizer Mode";

        // 4. Default visualizer: "bars"
        if (string.IsNullOrWhiteSpace(_settingsManager.Settings.VisualizerType))
        {
            _settingsManager.Settings.VisualizerType = "bars";
        }

        // 5. Validate visualizer background: if path does not exist, clear it
        if (!string.IsNullOrEmpty(_settingsManager.Settings.VisualizerBackgroundPath) &&
            !File.Exists(_settingsManager.Settings.VisualizerBackgroundPath))
        {
            _settingsManager.Settings.VisualizerBackgroundPath = null;
        }

        // 6. Ensure no stuck playback on startup
        if (_player != null && _player.IsInitialized)
        {
            try
            {
                _player.Command("stop");
            }
            catch { }
        }

        _pendingResumePosition = null;
    }

    private void UpdateRecentFilesList()
    {
        var existing = _settingsManager.Settings.RecentFiles
            .Where(File.Exists)
            .Take(5)
            .Select(path => new
            {
                DisplayName = Path.GetFileName(path) ?? path,
                FullPath = path
            })
            .ToList();

        RecentFilesList.ItemsSource = existing;
        RecentFilesPanel.Visibility = existing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && File.Exists(path))
        {
            LoadMediaFile(path);
        }
    }

    private void PlaylistManager_CurrentItemChanged(object? sender, NJPlayer.Core.Playlist.PlaylistItem? item)
    {
        if (item != null)
        {
            Dispatcher.InvokeAsync(() => LoadMediaFile(item.FilePath));
        }
    }

    private void InitializePlayer()
    {
        try
        {
            var hwdec = _settingsManager.Settings.HardwareDecoder;
            var hwdecCodecs = _settingsManager.Settings.EnableMpeg4Dxva ? "all" : (_settingsManager.Settings.HwdecCodecs ?? "all");
            _player = new MpvPlayer();
            _player.Initialize(options =>
            {
                options.WithWindowHandle(PlayerHost.Hwnd);
                options.WithHardwareDecoding(string.IsNullOrWhiteSpace(hwdec) ? "auto-safe" : hwdec, hwdecCodecs);
                if (!string.IsNullOrWhiteSpace(_settingsManager.Settings.VideoDecoderPriority) &&
                    !string.Equals(_settingsManager.Settings.VideoDecoderPriority, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    options.WithOption("vd", _settingsManager.Settings.VideoDecoderPriority);
                }
                if (!string.IsNullOrWhiteSpace(_settingsManager.Settings.AudioDecoderPriority) &&
                    !string.Equals(_settingsManager.Settings.AudioDecoderPriority, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    options.WithOption("ad", _settingsManager.Settings.AudioDecoderPriority);
                }
                options.WithSubtitleDefaults();
                options.WithKeepOpen(true);
                options.WithIdle(true);
            });

            _player.Error += Player_Error;
            _player.EndFile += Player_EndFile;
            _player.TimePositionChanged += Player_TimePositionChanged;
            _player.MediaTitleChanged += Player_MediaTitleChanged;
            Trace.WriteLine($"[NJPlayer] mpv engine initialized with '{hwdec}' hardware decoding.");
        }
        catch (DllNotFoundException ex)
        {
            _isDllAvailable = false;
            DllNoticeBorder.Visibility = Visibility.Visible;
            Trace.WriteLine($"[NJPlayer] {ex.Message}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize playback engine: {ex.Message}", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Player_TimePositionChanged(object? sender, TimeSpan pos)
    {
        if (_pendingResumePosition.HasValue && _player != null)
        {
            var target = _pendingResumePosition.Value;
            _pendingResumePosition = null;

            Dispatcher.InvokeAsync(() =>
            {
                _player.Seek(target, exact: true);
                _overlay?.ShowToast($"Resumed at {TimeSpan.FromSeconds(target):mm\\:ss}", "⏱");
            });
        }

        if (_isAudioMode && _currentLyrics != null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var (prev, current, next) = _currentLyrics.GetActiveLyrics(pos);
                LyricPrevText.Text = prev ?? string.Empty;
                LyricCurrentText.Text = string.IsNullOrEmpty(current)
                    ? (string.IsNullOrEmpty(prev) ? "♪ Music playing" : string.Empty)
                    : current;
                LyricNextText.Text = next ?? string.Empty;
            });
        }
    }

    public void LoadMediaFile(string filePath)
    {
        if (!_isDllAvailable || _player == null)
        {
            MessageBox.Show(
                "Cannot open media: 'libmpv-2.dll' was not found.\n\nPlease place 64-bit libmpv-2.dll into the 'libs' folder or next to NJPlayer.App.exe.",
                "libmpv Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(filePath))
        {
            MessageBox.Show($"The specified file could not be found:\n{filePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isAudio = MediaFormats.IsAudioOnly(filePath);
        var targetMode = isAudio ? NJPlayer.Core.Playback.PlayerMode.Music : NJPlayer.Core.Playback.PlayerMode.Video;

        // Ensure file is in its dedicated mode playlist queue (isolated queues)
        var targetPlaylist = isAudio ? _musicPlaylistManager : _videoPlaylistManager;
        var existingIdx = -1;
        for (int i = 0; i < targetPlaylist.Items.Count; i++)
        {
            if (string.Equals(targetPlaylist.Items[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                existingIdx = i;
                break;
            }
        }
        if (existingIdx < 0)
        {
            targetPlaylist.AddFile(filePath);
            existingIdx = targetPlaylist.Items.Count - 1;
        }
        targetPlaylist.SetCurrentIndex(existingIdx);

        if (_currentMode != targetMode)
        {
            SwitchPlayerMode(targetMode, targetFileToPlay: filePath);
            return;
        }

        LoadMediaFileDirect(filePath);
    }

    private void LoadMediaFileDirect(string filePath)
    {
        // Save position of previous file before switching
        if (!string.IsNullOrEmpty(_currentFilePath) && _player != null)
        {
            _settingsManager.SetResumePosition(_currentFilePath, _player.Position.TotalSeconds, _player.Duration.TotalSeconds);
        }

        _currentFilePath = filePath;
        _settingsManager.AddRecentFile(filePath);
        UpdateRecentFilesList();

        // Check for resume position
        var savedPos = _settingsManager.GetResumePosition(filePath);
        if (savedPos.HasValue && savedPos.Value > 5.0)
        {
            _pendingResumePosition = savedPos.Value;
        }
        else
        {
            _pendingResumePosition = null;
        }

        var fileName = Path.GetFileName(filePath);
        _overlay?.UpdateTitle(fileName, filePath);
        if (_player != null)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(300);
                if (_player != null)
                {
                    _overlay?.UpdateCodecBadges(NJPlayer.Core.Playback.CodecInfo.FromPlayer(_player));
                }
            });
        }

        _isAudioMode = MediaFormats.IsAudioOnly(filePath);
        _musicVisualizerActive = false;
        ToggleMusicVisualizerBtn.Content = "📊 Visualizer";

        if (_isAudioMode)
        {
            PlayerHost.Visibility = Visibility.Collapsed;
            MusicHeroSurface.Visibility = Visibility.Visible;
            EmptyStateOverlay.Visibility = Visibility.Collapsed;

            // Initialize fallback placeholders
            MusicTitleText.Text = Path.GetFileNameWithoutExtension(filePath);
            MusicArtistAlbumText.Text = "Loading track info...";
            MusicQualityBadgeText.Text = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            AlbumArtImage.Source = null;
            AlbumArtPlaceholder.Visibility = Visibility.Visible;
            LyricPrevText.Text = string.Empty;
            LyricCurrentText.Text = "♪ Loading lyrics...";
            LyricNextText.Text = string.Empty;

            _overlay?.SetPlayerMode(NJPlayer.Core.Playback.PlayerMode.Music);
            _overlay?.UpdateVisualizerModeState(false, _settingsManager.Settings.VisualizerType, true);
            LoadAudioModeDetailsAsync(filePath);
            LoadVisualizerBackground();
            InitVisualizerElements();
            _visualizerAnimTimer.Start();
        }
        else
        {
            _visualizerAnimTimer.Stop();
            _currentLyrics = null;
            MusicHeroSurface.Visibility = Visibility.Collapsed;
            EmptyStateOverlay.Visibility = Visibility.Collapsed;
            PlayerHost.Visibility = Visibility.Visible;
            _overlay?.SetPlayerMode(NJPlayer.Core.Playback.PlayerMode.Video);
            _overlay?.UpdateVisualizerModeState(false, _settingsManager.Settings.VisualizerType, false);

            if (PlayerHost.Hwnd != IntPtr.Zero && _player != null)
            {
                try
                {
                    _player.SetPropertyString("wid", PlayerHost.Hwnd.ToInt64().ToString());
                    _player.SetPropertyString("vo", "gpu");
                }
                catch { }
            }
        }

        try
        {
            ApplyVisualizerFilter("off");
            _player?.Load(filePath, startPaused: false);
            ApplyAudioFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading media file: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void TogglePlayerMode()
    {
        var targetMode = (_currentMode == NJPlayer.Core.Playback.PlayerMode.Video)
            ? NJPlayer.Core.Playback.PlayerMode.Music
            : NJPlayer.Core.Playback.PlayerMode.Video;

        SwitchPlayerMode(targetMode);
    }

    public void SwitchPlayerMode(NJPlayer.Core.Playback.PlayerMode newMode, string? targetFileToPlay = null)
    {
        if (_currentMode == newMode && targetFileToPlay == null)
        {
            return;
        }

        // 1. Non-interference: Save outgoing state of departing mode
        if (!string.IsNullOrEmpty(_currentFilePath) && _player != null)
        {
            var curPos = _player.Position.TotalSeconds;
            var curDur = _player.Duration.TotalSeconds;
            var wasPlaying = !_player.IsPaused;

            if (_currentMode == NJPlayer.Core.Playback.PlayerMode.Music)
            {
                _musicState.FilePath = _currentFilePath;
                _musicState.PositionSeconds = curPos;
                _musicState.DurationSeconds = curDur;
                _musicState.WasPlaying = wasPlaying;
            }
            else
            {
                _videoState.FilePath = _currentFilePath;
                _videoState.PositionSeconds = curPos;
                _videoState.DurationSeconds = curDur;
                _videoState.WasPlaying = wasPlaying;
            }

            _settingsManager.SetResumePosition(_currentFilePath, curPos, curDur);
        }

        // 2. Pause/stop outgoing playback to avoid any interference
        try
        {
            _player?.Command("stop");
            ApplyVisualizerFilter("off");
        }
        catch { }

        _currentMode = newMode;
        _isAudioMode = (newMode == NJPlayer.Core.Playback.PlayerMode.Music);
        _musicVisualizerActive = false;

        // 3. Update overlay, drawer, and layout
        _overlay?.SetPlayerMode(newMode);
        _overlay?.AttachPlaylist(ActivePlaylistManager);

        // 4. If a target file is requested, load it
        if (!string.IsNullOrWhiteSpace(targetFileToPlay))
        {
            LoadMediaFileDirect(targetFileToPlay);
            return;
        }

        // 5. Restore previous session position for this mode
        var incomingState = (newMode == NJPlayer.Core.Playback.PlayerMode.Music) ? _musicState : _videoState;
        if (!string.IsNullOrEmpty(incomingState.FilePath) && File.Exists(incomingState.FilePath))
        {
            _pendingResumePosition = incomingState.PositionSeconds;
            LoadMediaFileDirect(incomingState.FilePath);
            _overlay?.ShowToast($"Resumed {newMode} at {TimeSpan.FromSeconds(incomingState.PositionSeconds):mm\\:ss}", "⏱");
        }
        else if (ActivePlaylistManager.CurrentItem != null && File.Exists(ActivePlaylistManager.CurrentItem.FilePath))
        {
            LoadMediaFileDirect(ActivePlaylistManager.CurrentItem.FilePath);
        }
        else
        {
            _currentFilePath = null;
            if (newMode == NJPlayer.Core.Playback.PlayerMode.Music)
            {
                PlayerHost.Visibility = Visibility.Collapsed;
                MusicHeroSurface.Visibility = Visibility.Visible;
                EmptyStateOverlay.Visibility = Visibility.Collapsed;
                MusicTitleText.Text = "NJ Music Player";
                MusicArtistAlbumText.Text = "Drop audio files here or press Ctrl+O";
                MusicQualityBadgeText.Text = "MUSIC";
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
                LyricPrevText.Text = string.Empty;
                LyricCurrentText.Text = "♪ No track playing";
                LyricNextText.Text = string.Empty;
                _overlay?.UpdateTitle("NJ Music Player", string.Empty);
            }
            else
            {
                MusicHeroSurface.Visibility = Visibility.Collapsed;
                PlayerHost.Visibility = Visibility.Collapsed;
                EmptyStateOverlay.Visibility = Visibility.Visible;
                _overlay?.UpdateTitle("NJ Player", string.Empty);
                _overlay?.UpdateCodecBadges(null);
            }
        }
    }

    private static string GetMusicQueueFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "NJPlayer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "music_queue.json");
    }

    private async void LoadAudioModeDetailsAsync(string filePath)
    {
        _audioDetailsCts?.Cancel();
        _audioDetailsCts?.Dispose();
        _audioDetailsCts = new CancellationTokenSource();
        var ct = _audioDetailsCts.Token;

        try
        {
            // 1. Look for sidecar or embedded lyrics off UI thread
            _currentLyrics = await Task.Run(() => NJPlayer.Features.Subtitles.LrcLyricsService.FindAndLoad(filePath), ct);
            if (ct.IsCancellationRequested) return;

            if (_currentLyrics != null && _currentLyrics.Lines.Count > 0)
            {
                LyricCurrentText.Text = "♪ Synchronized lyrics ready";
            }
            else
            {
                LyricCurrentText.Text = "♪ Music playing";
            }

            // 2. Extract metadata and album art asynchronously with cancellation
            var meta = await _audioMetadataService.ExtractMetadataAsync(filePath, ct);
            if (ct.IsCancellationRequested) return;

            // Verify file hasn't changed while extracting
            if (!string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MusicTitleText.Text = meta.Title;
            MusicArtistAlbumText.Text = meta.DisplaySubtitle;
            MusicQualityBadgeText.Text = meta.QualityBadge;
            _overlay?.UpdateTitle($"{meta.Title} — {meta.Artist}", filePath);

            if (!string.IsNullOrEmpty(meta.AlbumArtPath) && File.Exists(meta.AlbumArtPath))
            {
                var bytes = await Task.Run(() =>
                {
                    try { return File.ReadAllBytes(meta.AlbumArtPath); } catch { return null; }
                }, ct);

                if (bytes != null && !ct.IsCancellationRequested)
                {
                    using var ms = new MemoryStream(bytes);
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();

                    AlbumArtImage.Source = bi;
                    AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    AlbumArtImage.Source = null;
                    AlbumArtPlaceholder.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }

            // Also update background if using album art fallback
            if (string.IsNullOrEmpty(_settingsManager.Settings.VisualizerBackgroundPath) && AlbumArtImage.Source != null)
            {
                MusicBackdropImage.Source = AlbumArtImage.Source;
                MusicBackdropImage.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            NJPlayer.Core.Diagnostics.LogService.LogWarning($"[MainWindow] Audio details loading exception: {ex.Message}");
        }
    }

    private readonly System.Windows.Threading.DispatcherTimer _visualizerAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _animPhase = 0;
    private readonly List<System.Windows.Shapes.Rectangle> _visualizerBars = new();
    private readonly System.Windows.Shapes.Polyline _visualizerPolyline = new();

    private void VisualizerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InitVisualizerElements();
    }

    private void InitVisualizerElements()
    {
        if (VisualizerCanvas == null || VisualizerCanvas.ActualWidth <= 0 || VisualizerCanvas.ActualHeight <= 0) return;
        VisualizerCanvas.Children.Clear();
        _visualizerBars.Clear();

        var w = VisualizerCanvas.ActualWidth;
        var h = VisualizerCanvas.ActualHeight;
        var mode = _settingsManager.Settings.VisualizerType?.ToLowerInvariant() ?? "bars";

        if (mode is "bars" or "spectrum")
        {
            const int barCount = 38;
            var barWidth = Math.Max(2, (w / barCount) - 3);
            var cyanBrush = (Brush)FindResource("AccentBrush");
            var emeraldBrush = (Brush)FindResource("SuccessBrush");

            for (int i = 0; i < barCount; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = barWidth,
                    Height = 4,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = (i % 2 == 0) ? cyanBrush : emeraldBrush,
                    Opacity = 0.88
                };
                Canvas.SetLeft(rect, i * (barWidth + 3));
                Canvas.SetTop(rect, h - 4);
                VisualizerCanvas.Children.Add(rect);
                _visualizerBars.Add(rect);
            }
        }
        else
        {
            _visualizerPolyline.Stroke = (Brush)FindResource("AccentBrush");
            _visualizerPolyline.StrokeThickness = 2;
            _visualizerPolyline.Points.Clear();
            VisualizerCanvas.Children.Add(_visualizerPolyline);
        }

        UpdateVisualizerStatusVisuals();
    }

    private void VisualizerAnimTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isAudioMode || _player == null || _player.IsPaused || VisualizerCanvas.ActualWidth <= 0 || VisualizerCanvas.ActualHeight <= 0)
        {
            return;
        }

        _animPhase += 0.085;
        if (_animPhase > Math.PI * 200) _animPhase = 0;

        RenderVisualizerCanvas();
    }

    private void RenderVisualizerCanvas()
    {
        var h = VisualizerCanvas.ActualHeight;
        var w = VisualizerCanvas.ActualWidth;
        if (h <= 0 || w <= 0) return;

        var mode = _settingsManager.Settings.VisualizerType?.ToLowerInvariant() ?? "bars";

        if (mode is "bars" or "spectrum")
        {
            if (_visualizerBars.Count == 0)
            {
                InitVisualizerElements();
                return;
            }

            var count = _visualizerBars.Count;
            for (int i = 0; i < count; i++)
            {
                var norm = (double)i / count;
                var wave1 = Math.Sin(_animPhase * 1.7 + norm * Math.PI * 3.8);
                var wave2 = Math.Cos(_animPhase * 1.1 + norm * Math.PI * 2.2);
                var wave3 = Math.Sin(_animPhase * 2.3 + norm * Math.PI * 5.5);
                var combined = Math.Abs(wave1 * 0.45 + wave2 * 0.35 + wave3 * 0.20);

                var bassBoost = 1.0 + (1.0 - norm) * 0.55;
                var barHeight = Math.Clamp(combined * (h - 6) * bassBoost + 4, 4, h);

                var rect = _visualizerBars[i];
                rect.Height = barHeight;
                Canvas.SetTop(rect, h - barHeight);
            }
        }
        else
        {
            const int segs = 64;
            var midY = h / 2.0;

            if (_visualizerPolyline.Points.Count != segs + 1)
            {
                _visualizerPolyline.Points.Clear();
                for (int i = 0; i <= segs; i++)
                {
                    _visualizerPolyline.Points.Add(new Point(0, midY));
                }
            }

            for (int i = 0; i <= segs; i++)
            {
                var x = (w / segs) * i;
                var norm = (double)i / segs;
                var wave = Math.Sin(_animPhase * 2.8 + norm * Math.PI * 7.5) *
                           Math.Cos(_animPhase * 1.4 + norm * Math.PI * 3.0);
                var y = Math.Clamp(midY + wave * (midY - 4), 2, h - 2);
                _visualizerPolyline.Points[i] = new Point(x, y);
            }
        }
    }

    public void SetVisualizerActive(bool active)
    {
        if (!_isAudioMode || _player == null) return;

        _musicVisualizerActive = active;

        if (_musicVisualizerActive)
        {
            ToggleMusicVisualizerBtn.Content = "🖼 Cover View";
            VisualizerStripBorder.Height = 72;
            _overlay?.UpdateVisualizerModeState(true, _settingsManager.Settings.VisualizerType, true);
            _overlay?.ShowToast("Visualizer Mode", "📊");
        }
        else
        {
            ToggleMusicVisualizerBtn.Content = "📊 Visualizer Mode";
            VisualizerStripBorder.Height = 56;
            _overlay?.UpdateVisualizerModeState(false, _settingsManager.Settings.VisualizerType, true);
            _overlay?.ShowToast("Cover View", "🖼");
        }

        UpdateVisualizerStatusVisuals();
    }

    public void ToggleVisualizer()
    {
        SetVisualizerActive(!_musicVisualizerActive);
    }

    public void CycleVisualizerStyle()
    {
        var current = _settingsManager.Settings.VisualizerType?.ToLowerInvariant() ?? "bars";
        var next = current switch
        {
            "bars" => "waveform",
            "waveform" => "spectrum",
            "spectrum" => "vector",
            _ => "bars"
        };

        _settingsManager.Settings.VisualizerType = next;
        _settingsManager.Save();

        var label = next switch
        {
            "waveform" => "Oscilloscope Glow 🌊",
            "spectrum" => "Pitch Spectrum & Sonogram 🌈",
            "vector" => "Lissajous Cosmic Scope 💫",
            _ => "Smooth Frequency Bars 📊"
        };

        _overlay?.UpdateVisualizerModeState(_musicVisualizerActive, next, _isAudioMode);
        _overlay?.ShowToast($"Visualizer: {label}", "📊");

        InitVisualizerElements();
    }

    private void UpdateVisualizerStatusVisuals()
    {
        var mode = _settingsManager.Settings.VisualizerType?.ToLowerInvariant() ?? "bars";
        if (VisualizerStatusLabel != null)
        {
            VisualizerStatusLabel.Text = mode switch
            {
                "waveform" => "Oscilloscope Wave",
                "spectrum" => "Pitch Spectrum",
                "vector" => "Cosmic Vector Scope",
                _ => "Spectrum Bars"
            };
        }
        if (VisualizerStatusIcon != null)
        {
            VisualizerStatusIcon.Text = mode switch
            {
                "waveform" => "🌊",
                "spectrum" => "🌈",
                "vector" => "💫",
                _ => "📊"
            };
        }
        if (VisualizerCycleBtnText != null)
        {
            VisualizerCycleBtnText.Text = mode switch
            {
                "waveform" => "Wave ▾",
                "spectrum" => "Spectrum ▾",
                "vector" => "Vector ▾",
                _ => "Bars ▾"
            };
        }
    }

    public void SetVisualizerBackground(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        _settingsManager.Settings.VisualizerBackgroundPath = path;
        _settingsManager.Save();

        _overlay?.ShowToast($"Background: {Path.GetFileName(path)}", "🌄");
        LoadVisualizerBackground();
    }

    public void ClearVisualizerBackground()
    {
        _settingsManager.Settings.VisualizerBackgroundPath = null;
        _settingsManager.Save();

        _overlay?.ShowToast("Background Cleared", "✕");
        LoadVisualizerBackground();
    }

    public void LoadVisualizerBackground()
    {
        _ = LoadVisualizerBackgroundAsync();
    }

    public async Task LoadVisualizerBackgroundAsync()
    {
        _visualizerBgCts?.Cancel();
        _visualizerBgCts?.Dispose();
        _visualizerBgCts = new CancellationTokenSource();
        var ct = _visualizerBgCts.Token;

        try
        {
            var bgPath = _settingsManager.Settings.VisualizerBackgroundPath;
            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
            {
                var ext = Path.GetExtension(bgPath).ToLowerInvariant();
                if (ext is ".mp4" or ".mkv" or ".mov" or ".webm")
                {
                    // Video file: extract thumbnail frame asynchronously off UI thread
                    var thumbService = new NJPlayer.Core.Thumbnails.FfmpegThumbnailService();
                    var bytes = await thumbService.GetThumbnailAsync(bgPath, 1.0);
                    if (ct.IsCancellationRequested) return;

                    if (bytes != null && bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        MusicBackdropImage.Source = bi;
                        MusicBackdropImage.Visibility = Visibility.Visible;
                        return;
                    }
                }
                else if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp")
                {
                    var bytes = await Task.Run(() =>
                    {
                        try { return File.ReadAllBytes(bgPath); } catch { return null; }
                    }, ct);

                    if (bytes != null && !ct.IsCancellationRequested)
                    {
                        using var ms = new MemoryStream(bytes);
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        MusicBackdropImage.Source = bi;
                        MusicBackdropImage.Visibility = Visibility.Visible;
                        return;
                    }
                }
            }

            // Fallback: if song has album art, use subtle blurred album art as backdrop
            if (AlbumArtImage != null && AlbumArtImage.Source != null)
            {
                MusicBackdropImage.Source = AlbumArtImage.Source;
                MusicBackdropImage.Visibility = Visibility.Visible;
            }
            else
            {
                MusicBackdropImage.Source = null;
                MusicBackdropImage.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[VisualizerBackground] Error: {ex.Message}");
        }
    }

    private void PickBackgroundBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose Visualizer Background (Photo or Video) - NJ Player",
            Filter = "All Supported Backgrounds (*.jpg;*.png;*.mp4;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.mp4;*.mkv;*.mov|Image Files (*.jpg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|Video Files (*.mp4;*.mkv;*.mov)|*.mp4;*.mkv;*.mov|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) == true)
        {
            SetVisualizerBackground(dlg.FileName);
        }
    }

    private void VisualizerCycleBtn_Click(object sender, RoutedEventArgs e)
    {
        CycleVisualizerStyle();
    }

    private void ToggleMusicVisualizer_Click(object sender, RoutedEventArgs e)
    {
        ToggleVisualizer();
    }

    public NJPlayer.Features.Audio.IAudioSuite GetOrCreateAudioSuite()
    {
        if (_audioSuite == null)
        {
            _audioSuite = new NJPlayer.Features.Audio.AudioSuiteService(new NJPlayer.Features.Audio.AudioSettings(_settingsManager.Settings.Audio));
            _audioSuite.SettingsChanged += (_, s) =>
            {
                _settingsManager.Settings.Audio = s.ToConfig();
                ApplyAudioFilters();
            };
        }
        return _audioSuite;
    }

    public void ApplyAudioFilters()
    {
        if (_player == null || !_isDllAvailable) return;
        var filterStr = _audioSuite?.BuildMpvFilterString() ?? string.Empty;
        try
        {
            _player.SetPropertyString("af", filterStr);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Error updating audio filter: {ex.Message}");
        }
    }

    public void ToggleEqualizer()
    {
        var suite = GetOrCreateAudioSuite();
        if (_audioPanel == null)
        {
            _audioPanel = new Views.AudioPanelWindow(suite) { Owner = this };
        }

        if (_audioPanel.IsVisible)
        {
            _audioPanel.Hide();
        }
        else
        {
            _audioPanel.Show();
            _audioPanel.Activate();
            _overlay?.ShowToast("Equalizer", "🎚");
        }
    }

    public void OpenSubtitlePicker()
    {
        if (_subtitlePicker == null)
        {
            _subtitlePicker = new Views.SubtitlePickerWindow(_player, _subtitleService, _currentFilePath)
            {
                Owner = this
            };
            _subtitlePicker.SubtitleLoaded += name =>
            {
                _overlay?.ShowToast($"Subtitles: {name}", "💬");
            };
        }

        _subtitlePicker.RefreshTracks();
        if (_subtitlePicker.IsVisible)
        {
            _subtitlePicker.Hide();
        }
        else
        {
            _subtitlePicker.Show();
            _subtitlePicker.Activate();
            _overlay?.ShowToast("Subtitles", "💬");
        }
    }

    public void OpenTrimmer()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !File.Exists(_currentFilePath))
        {
            _overlay?.ShowToast("Open a video file first to trim", "✂", isError: true);
            return;
        }

        if (_trimmerWindow == null)
        {
            _trimmerWindow = new Views.TrimmerWindow(_player, _trimmerService, _currentFilePath)
            {
                Owner = this
            };
            _trimmerWindow.ClipExported += path =>
            {
                _overlay?.ShowToast($"Clip saved: {Path.GetFileName(path)}", "✂");
            };
        }

        if (_trimmerWindow.IsVisible)
        {
            _trimmerWindow.Hide();
        }
        else
        {
            _trimmerWindow.Show();
            _trimmerWindow.Activate();
            _overlay?.ShowToast("Trimmer", "✂");
        }
    }

    public void OpenDownloader()
    {
        if (_downloaderWindow == null)
        {
            _downloaderWindow = new Views.DownloaderWindow(_downloaderService)
            {
                Owner = this
            };
            _downloaderWindow.MediaDownloaded += path =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    LoadMediaFile(path);
                    _overlay?.ShowToast($"Playing: {Path.GetFileName(path)}", "▶");
                });
            };
        }

        if (_downloaderWindow.IsVisible)
        {
            _downloaderWindow.Hide();
        }
        else
        {
            _downloaderWindow.Show();
            _downloaderWindow.Activate();
            _overlay?.ShowToast("Downloader", "📥");
        }
    }

    public void OpenRadioBrowser()
    {
        if (_radioService == null)
        {
            _radioService = new Features.Radio.RadioBrowserService(initialFavorites: _settingsManager.Settings.RadioFavorites);
        }

        if (_radioPanelWindow == null)
        {
            _radioPanelWindow = new Views.RadioPanelWindow(_radioService, _settingsManager.Settings)
            {
                Owner = this
            };
            _radioPanelWindow.StationSelected += (station, vizMode) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    PlayRadioStation(station, vizMode);
                });
            };
            _radioPanelWindow.VisualizerModeChanged += mode =>
            {
                _currentVisualizerMode = mode;
                ApplyVisualizerFilter(mode);
            };
        }

        if (_radioPanelWindow.IsVisible)
        {
            _radioPanelWindow.Hide();
        }
        else
        {
            _radioPanelWindow.Show();
            _radioPanelWindow.Activate();
            _overlay?.ShowToast("Online Radio", "📻");
        }
    }

    public void PlayRadioStation(Features.Radio.RadioStation station, string vizMode)
    {
        if (!_isDllAvailable || _player == null)
        {
            MessageBox.Show(
                "Cannot open stream: 'libmpv-2.dll' was not found.\n\nPlease place 64-bit libmpv-2.dll into the 'libs' folder or next to NJPlayer.App.exe.",
                "libmpv Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(station.StreamUrl)) return;

        _currentFilePath = station.StreamUrl;
        _currentVisualizerMode = vizMode;

        PlayerHost.Visibility = Visibility.Visible;
        EmptyStateOverlay.Visibility = Visibility.Collapsed;
        _overlay?.UpdateTitle($"📻 {station.Name}", station.StreamUrl);

        try
        {
            _player.Load(station.StreamUrl, startPaused: false);
            ApplyAudioFilters();
            ApplyVisualizerFilter(vizMode);
            _overlay?.ShowToast($"Tuning into {station.Name}", "📻");
        }
        catch (Exception ex)
        {
            NJPlayer.Core.Diagnostics.LogService.LogError("Radio.PlayStation", ex);
            MessageBox.Show($"Error playing radio station: {ex.Message}", "Radio Stream Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ApplyVisualizerFilter(string mode)
    {
        if (_player == null || !_isDllAvailable) return;

        // If mode is off, clear lavfi-complex immediately
        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _player.SetPropertyString("lavfi-complex", "");
            }
            catch { }
            return;
        }

        // Only apply lavfi-complex if there is an active media/stream playing
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _currentVisualizerMode = mode;
            return;
        }

        var bgPath = _settingsManager.Settings.VisualizerBackgroundPath;
        var filter = NJPlayer.Features.Audio.VisualizerFilterBuilder.BuildFilter(mode, bgPath);
        try
        {
            _player.SetPropertyString("lavfi-complex", filter);
        }
        catch (Exception ex)
        {
            NJPlayer.Core.Diagnostics.LogService.LogError("Visualizer.ApplyFilter", ex);
            // Fallback: try standard visualizer without custom background if it failed
            if (!string.IsNullOrEmpty(bgPath))
            {
                try
                {
                    var fallback = NJPlayer.Features.Audio.VisualizerFilterBuilder.BuildFilter(mode, null);
                    _player.SetPropertyString("lavfi-complex", fallback);
                    _overlay?.ShowToast("Background failed; using standard visualizer", "⚡", isError: true);
                    return;
                }
                catch { }
            }

            try
            {
                _player.SetPropertyString("lavfi-complex", "");
            }
            catch { }
            _overlay?.ShowToast("Visualizer unavailable for this stream", "⚡", isError: true);
        }
    }

    private void Player_MediaTitleChanged(object? sender, string mediaTitle)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(mediaTitle))
            {
                _overlay?.UpdateTitle($"📻 {mediaTitle}", _currentFilePath ?? string.Empty);
                _overlay?.ShowToast(mediaTitle, "🎵");
            }
            if (_player != null)
            {
                _overlay?.UpdateCodecBadges(NJPlayer.Core.Playback.CodecInfo.FromPlayer(_player));
            }
        });
    }

    public void HandleCommandLineArgs(string[] args)
    {
        if (args == null || args.Length == 0) return;

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(arg, "--bench", StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine("[BENCH] Benchmark flag passed in arguments.");
                }
                continue;
            }

            if (File.Exists(arg) || Directory.Exists(arg) || arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(arg))
                {
                    _playlistManager.AddFolder(arg, recursive: true);
                }
                else
                {
                    _playlistManager.AddFile(arg);
                }
                LoadMediaFile(arg);
                break;
            }
        }
    }

    public void OpenSettings()
    {
        var win = new Views.SettingsWindow(_settingsManager)
        {
            Owner = this
        };
        if (win.ShowDialog() == true)
        {
            _overlay?.ShowToast("Settings Saved", "⚙");
        }
    }

    public void OpenGeneralSettings() => OpenSettings();

    private VrProjectionMode _vrMode = VrProjectionMode.Off;
    private double _vrYaw = 0.0;
    private double _vrPitch = 0.0;
    private double _vrRoll = 0.0;
    private double _vrFov = VrFilterBuilder.DefaultFov;

    public bool IsVrActive => _vrMode != VrProjectionMode.Off;
    public VrProjectionMode CurrentVrMode => _vrMode;

    public void ToggleVr180Mode()
    {
        var nextMode = _vrMode switch
        {
            VrProjectionMode.Off => VrProjectionMode.Vr180Sbs,
            VrProjectionMode.Vr180Sbs => VrProjectionMode.Vr180Mono,
            VrProjectionMode.Vr180Mono => VrProjectionMode.Vr360Mono,
            VrProjectionMode.Vr360Mono => VrProjectionMode.Off,
            _ => VrProjectionMode.Off
        };
        SetVrMode(nextMode);
    }

    public void SetVrMode(VrProjectionMode mode)
    {
        if (_player == null || !_player.IsInitialized) return;
        _vrMode = mode;

        try
        {
            if (_vrMode != VrProjectionMode.Off)
            {
                var filter = VrFilterBuilder.BuildFilterString(_vrMode, _vrYaw, _vrPitch, _vrRoll, _vrFov);
                _player.Command("vf", "set", filter);
                _overlay?.ShowToast($"{VrFilterBuilder.GetModeLabel(_vrMode)}: ON (Drag to Pan)", "360°");
            }
            else
            {
                _player.Command("vf", "clr", "");
                _overlay?.ShowToast("VR Projection: OFF", "360°");
            }
            _overlay?.UpdateVrButtonVisuals(_vrMode);
        }
        catch (Exception ex)
        {
            _overlay?.ShowToast("VR Error: " + ex.Message, "360°", isError: true);
        }
    }

    public void PanVr(double deltaYaw, double deltaPitch)
    {
        if (!IsVrActive || _player == null || !_player.IsInitialized) return;

        _vrYaw += deltaYaw;
        _vrPitch -= deltaPitch; // Invert Y so mouse dragging down tilts camera up
        ApplyVrFilterLive();
    }

    public void ZoomVrFov(double deltaFov)
    {
        if (!IsVrActive || _player == null || !_player.IsInitialized) return;

        _vrFov = Math.Clamp(_vrFov + deltaFov, VrFilterBuilder.MinFov, VrFilterBuilder.MaxFov);
        ApplyVrFilterLive();
        _overlay?.ShowToast($"FOV: {_vrFov:0}°", "🔍");
    }

    public void ResetVrView()
    {
        _vrYaw = 0.0;
        _vrPitch = 0.0;
        _vrRoll = 0.0;
        _vrFov = VrFilterBuilder.DefaultFov;
        if (IsVrActive)
        {
            ApplyVrFilterLive();
            _overlay?.ShowToast("VR Camera Reset (Yaw: 0°, Pitch: 0°)", "🎯");
        }
    }

    private void ApplyVrFilterLive()
    {
        try
        {
            var filter = VrFilterBuilder.BuildFilterString(_vrMode, _vrYaw, _vrPitch, _vrRoll, _vrFov);
            _player?.Command("vf", "set", filter);
        }
        catch { }
    }

    private bool _isThreeDActive = false;
    public void ToggleThreeDMode()
    {
        if (_player == null) return;
        _isThreeDActive = !_isThreeDActive;
        try
        {
            if (_isThreeDActive)
            {
                _player.Command("vf", "set", "stereo3d=sbsl:ml");
                _overlay?.ShowToast("3D Stereo (Side-by-Side): ON", "3D");
            }
            else
            {
                _player.Command("vf", "clr", "");
                _overlay?.ShowToast("3D Mode: OFF", "3D");
            }
        }
        catch (Exception ex)
        {
            _overlay?.ShowToast("3D mode error: " + ex.Message, "3D", isError: true);
        }
    }

    public void AdjustVideoProperty(string property, double delta, string label, string icon)
    {
        if (_player == null || !_player.IsInitialized) return;
        try
        {
            var cur = _player.GetPropertyDouble(property) ?? 0.0;
            var val = Math.Clamp(Math.Round(cur + delta), -100.0, 100.0);
            _player.SetPropertyDouble(property, val);
            _overlay?.ShowToast($"{label}: {val:+0;-0;0}%", icon);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AdjustVideoProperty] Error: {ex.Message}");
        }
    }

    public void ResetVideoColors(bool showToast = true)
    {
        if (_player == null || !_player.IsInitialized) return;
        try
        {
            _player.SetPropertyDouble("brightness", 0.0);
            _player.SetPropertyDouble("contrast", 0.0);
            _player.SetPropertyDouble("saturation", 0.0);
            _player.SetPropertyDouble("gamma", 0.0);
            if (showToast)
            {
                _overlay?.ShowToast("Video Colors Reset (0%)", "🔄");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ResetVideoColors] Error: {ex.Message}");
        }
    }

    public void OpenPlaylistSearch()
    {
        _overlay?.OpenPlaylistSearch();
    }

    public void OpenCodecManager()
    {
        var win = new Views.CodecManagerWindow(_player, _settingsManager)
        {
            Owner = this
        };
        if (win.ShowDialog() == true)
        {
            _overlay?.ShowToast("Codec Settings Saved", "🎞");
        }
    }

    public void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _savedState = WindowState;
            _savedStyle = WindowStyle;
            _savedResize = ResizeMode;
            _savedLeft = Left;
            _savedTop = Top;
            _savedWidth = Width;
            _savedHeight = Height;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hMonitor, ref mi);

            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var fullLeft = mi.rcMonitor.Left / dpi.DpiScaleX;
            var fullTop = mi.rcMonitor.Top / dpi.DpiScaleY;
            var fullWidth = mi.rcMonitor.Width / dpi.DpiScaleX;
            var fullHeight = mi.rcMonitor.Height / dpi.DpiScaleY;

            _isFullscreen = true;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            Topmost = true;

            Left = fullLeft;
            Top = fullTop;
            Width = fullWidth;
            Height = fullHeight;

            WindowOutline.BorderThickness = new Thickness(0);
            _overlay?.EnterFullscreen();
        }
        else
        {
            _isFullscreen = false;
            Topmost = _isAlwaysOnTop;
            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;

            if (_savedState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                Left = _savedLeft;
                Top = _savedTop;
                Width = _savedWidth;
                Height = _savedHeight;
                WindowState = WindowState.Normal;
            }

            WindowOutline.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
            _overlay?.ExitFullscreen();
        }

        _overlay?.SyncWithMainWindow();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    public void ToggleMaximize()
    {
        if (_isFullscreen)
        {
            ToggleFullscreen();
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        _overlay?.SyncWithMainWindow();
    }

    private void Player_EndFile(object? sender, EndFileEventArgs e)
    {
        if (e.Reason == EndFileReason.Eof)
        {
            // Clear resume position on natural EOF completion
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _settingsManager.SetResumePosition(_currentFilePath, 0, 0);
            }

            Dispatcher.InvokeAsync(() =>
            {
                // Auto-advance to next playlist item
                if (!_playlistManager.Next())
                {
                    _overlay?.ShowToast("Playlist Finished", "⏹");
                }
            });
        }
        else if (e.Reason == EndFileReason.Stop)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isAudioMode = false;
                _musicVisualizerActive = false;
                _currentFilePath = null;
                _currentLyrics = null;
                MusicHeroSurface.Visibility = Visibility.Collapsed;
                PlayerHost.Visibility = Visibility.Collapsed;
                EmptyStateOverlay.Visibility = Visibility.Visible;
                _overlay?.UpdateTitle("NJ Player", string.Empty);
                _overlay?.UpdateCodecBadges(null);
            });
        }
    }

    private void Player_Error(object? sender, MpvErrorEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Trace.WriteLine($"[NJPlayer MPV ERROR] {e.Message} (code: {e.ErrorCode})");
        });
    }

    public void StopPlayback()
    {
        try
        {
            if (_player != null && _player.IsInitialized)
            {
                _player.Stop();
            }
            _overlay?.ShowToast("Stopped", "⏹");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[StopPlayback] Error: {ex.Message}");
        }
    }

    public void PromptOpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Media or Playlist File - NJ Player",
            Filter = "All Supported Media|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.ts;*.m4v;*.mpd;*.mp3;*.flac;*.wav;*.m4a;*.opus;*.ogg;*.m3u;*.m3u8|HLS & DASH Streams (*.m3u8;*.mpd)|*.m3u8;*.mpd|M3U Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenPathOrPlaylist(dialog.FileName);
        }
    }

    public void OpenPathOrPlaylist(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (NJPlayer.Core.Playlist.PlaylistManager.IsPlaylistFile(path))
        {
            var initialCount = _playlistManager.Items.Count;
            var added = _playlistManager.LoadM3u(path);
            if (added > 0)
            {
                _playlistManager.SetCurrentIndex(initialCount);
                _overlay?.ShowToast($"Playlist: {Path.GetFileName(path)} ({added} items)", "📋");
            }
            else
            {
                _overlay?.ShowToast("Playlist is empty or invalid", "⚠️", isError: true);
            }
        }
        else
        {
            LoadMediaFile(path);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    public void HandleDropFiles(string[]? files)
    {
        if (files == null || files.Length == 0) return;

        var initialIndex = _playlistManager.Items.Count;
        foreach (var file in files)
        {
            if (Directory.Exists(file))
            {
                _playlistManager.AddFolder(file, recursive: true);
            }
            else if (NJPlayer.Core.Playlist.PlaylistManager.IsPlaylistFile(file))
            {
                _playlistManager.LoadM3u(file);
            }
            else if (File.Exists(file))
            {
                _playlistManager.AddFile(file);
            }
        }

        if (_playlistManager.CurrentIndex < 0 && _playlistManager.Items.Count > initialIndex)
        {
            _playlistManager.SetCurrentIndex(initialIndex);
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && _musicVisualizerActive)
            {
                var first = files[0];
                var ext = Path.GetExtension(first).ToLowerInvariant();
                if (ext is ".jpg" or ".jpeg" or ".png" or ".mp4" or ".mkv")
                {
                    SetVisualizerBackground(first);
                    e.Handled = true;
                    return;
                }
            }
            HandleDropFiles(files);
        }
    }

    public void OpenHotkeySettings()
    {
        var win = new Views.HotkeySettingsWindow(_hotkeyManager)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    public void ForwardKeyDown(KeyEventArgs e)
    {
        Window_KeyDown(this, e);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ignore global player hotkeys while typing in any text box (e.g. Browse PC address, search box)
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        // Direct trimmer In/Out hotkey shortcuts when Trimmer window is active or visible
        if (_trimmerWindow != null && _trimmerWindow.IsVisible && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (key == Key.I)
            {
                _trimmerWindow.SetInPoint(_player?.Position ?? TimeSpan.Zero);
                _overlay?.ShowToast("In-Point Set", "✂");
                e.Handled = true;
                return;
            }
            if (key == Key.O)
            {
                _trimmerWindow.SetOutPoint(_player?.Position ?? TimeSpan.Zero);
                _overlay?.ShowToast("Out-Point Set", "✂");
                e.Handled = true;
                return;
            }
        }

        // Escape exits fullscreen per Issue 3
        if (key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        // Enter key toggles fullscreen per DESIGN.md and Issue 3
        if ((key == Key.Return || key == Key.Enter) && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        // F4 or Ctrl+V toggles visualizer / cover view in music mode
        if ((key == Key.F4 || (key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)) && _isAudioMode)
        {
            ToggleVisualizer();
            e.Handled = true;
            return;
        }

        // Ctrl+M toggles Player Mode (Video <-> Music)
        if (key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TogglePlayerMode();
            e.Handled = true;
            return;
        }

        var modifiers = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) modifiers.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) modifiers.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) modifiers.Add("Alt");

        modifiers.Add(key.ToString());
        var gesture = string.Join("+", modifiers);

        if (_hotkeyManager.TryGetCommand(gesture, out var command))
        {
            ExecuteCommand(command);
            e.Handled = true;
        }
    }

    public void ExecuteCommand(NJPlayer.Core.Hotkeys.CommandId command)
    {
        try
        {
            var isMediaLoaded = _player != null && _player.Duration > TimeSpan.Zero;

            switch (command)
            {
                case NJPlayer.Core.Hotkeys.CommandId.PlayPause:
                    if (!isMediaLoaded)
                    {
                        if (_playlistManager.Items.Count > 0)
                        {
                            _playlistManager.SetCurrentIndex(Math.Max(0, _playlistManager.CurrentIndex));
                        }
                        else
                        {
                            _overlay?.ShowToast("No media loaded", "⏸");
                        }
                    }
                    else
                    {
                        _player?.TogglePause();
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.VolumeUp5:
                    var vUp = Math.Min(150, (_player?.Volume ?? 100) + 5);
                    _player?.SetVolume(vUp);
                    _overlay?.ShowToast($"Volume {vUp:F0}%", "🔊");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.VolumeDown5:
                    var vDown = Math.Max(0, (_player?.Volume ?? 100) - 5);
                    _player?.SetVolume(vDown);
                    _overlay?.ShowToast($"Volume {vDown:F0}%", vDown > 0 ? "🔉" : "🔇");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.Mute:
                    var curVol = _player?.Volume ?? 100;
                    if (curVol > 0)
                    {
                        _lastNonMuteVolume = curVol;
                        _player?.SetVolume(0);
                        _overlay?.ShowToast("Mute", "🔇");
                    }
                    else
                    {
                        var targetVol = _lastNonMuteVolume > 0 ? _lastNonMuteVolume : 100;
                        _player?.SetVolume(targetVol);
                        _overlay?.ShowToast($"Volume {targetVol:F0}%", "🔊");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekForward10s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(10, exact: false);
                        _overlay?.ShowToast("+10 s", "⏩");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekBackward10s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(-10, exact: false);
                        _overlay?.ShowToast("-10 s", "⏪");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekForward5s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(5, exact: false);
                        _overlay?.ShowToast("+5 s", "⏩");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekBackward5s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(-5, exact: false);
                        _overlay?.ShowToast("-5 s", "⏪");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekForward1s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(1, exact: true);
                        _overlay?.ShowToast("+1 s", "⏩");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekBackward1s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(-1, exact: true);
                        _overlay?.ShowToast("-1 s", "⏪");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekForward30s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(30, exact: false);
                        _overlay?.ShowToast("+30 s", "⏩");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SeekBackward30s:
                    if (isMediaLoaded)
                    {
                        _player?.SeekRelative(-30, exact: false);
                        _overlay?.ShowToast("-30 s", "⏪");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SpeedUp:
                    if (isMediaLoaded)
                    {
                        var spdUp = Math.Round((_player?.Speed ?? 1.0) + 0.1, 2);
                        _player?.SetSpeed(spdUp);
                        _overlay?.ShowToast($"Speed {spdUp:0.0#}x", "⚡");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SpeedDown:
                    if (isMediaLoaded)
                    {
                        var spdDown = Math.Round(Math.Max(0.1, (_player?.Speed ?? 1.0) - 0.1), 2);
                        _player?.SetSpeed(spdDown);
                        _overlay?.ShowToast($"Speed {spdDown:0.0#}x", "⚡");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ResetSpeed:
                    if (isMediaLoaded)
                    {
                        _player?.SetSpeed(1.0);
                        _overlay?.ShowToast("Speed 1.0x", "⚡");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SubtitleDelayPlus:
                    if (isMediaLoaded)
                    {
                        _subDelayMs += 100;
                        _player?.Command("add", "sub-delay", "0.1");
                        _overlay?.ShowToast($"Sub Delay +{_subDelayMs} ms", "💬");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SubtitleDelayMinus:
                    if (isMediaLoaded)
                    {
                        _subDelayMs -= 100;
                        _player?.Command("add", "sub-delay", "-0.1");
                        _overlay?.ShowToast($"Sub Delay {_subDelayMs} ms", "💬");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ToggleSubtitles:
                    if (isMediaLoaded)
                    {
                        _player?.Command("cycle", "sub-visibility");
                        _overlay?.ShowToast("Toggle Subtitles", "💬");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SubtitleDownload:
                    OpenSubtitlePicker();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.CycleAudioTrack:
                    if (isMediaLoaded)
                    {
                        _player?.Command("cycle", "audio");
                        _overlay?.ShowToast("Cycle Audio Track", "🎵");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.TogglePlaylist:
                    _overlay?.TogglePlaylist();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.NextFile:
                    if (_playlistManager.Next())
                    {
                        _overlay?.ShowToast("Next Track", "⏭");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.PrevFile:
                    if (_playlistManager.Previous())
                    {
                        _overlay?.ShowToast("Previous Track", "⏮");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ToggleFullscreen:
                    ToggleFullscreen();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ExitFullscreen:
                    if (_isFullscreen) ToggleFullscreen();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.OpenFile:
                    PromptOpenFile();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.OpenUrl:
                    OpenDownloader();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ToggleAlwaysOnTop:
                    _isAlwaysOnTop = !_isAlwaysOnTop;
                    Topmost = _isAlwaysOnTop;
                    if (_overlay != null) _overlay.Topmost = _isAlwaysOnTop;
                    _overlay?.ShowToast(_isAlwaysOnTop ? "Always on Top: ON" : "Always on Top: OFF", "📌");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ToggleEqualizer:
                    ToggleEqualizer();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.JumpToStart:
                    if (isMediaLoaded)
                    {
                        _player?.Seek(0, exact: true);
                        _overlay?.ShowToast("Start", "⏮");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.JumpToEnd:
                    var dur = _player?.Duration.TotalSeconds ?? 0;
                    if (dur > 0)
                    {
                        _player?.Seek(dur, exact: true);
                        _overlay?.ShowToast("End", "⏭");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.FrameStepForward:
                    if (isMediaLoaded)
                    {
                        _player?.Command("frame-step");
                        _overlay?.ShowToast("Frame Step +", "▶");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.FrameStepBackward:
                    if (isMediaLoaded)
                    {
                        _player?.Command("frame-back-step");
                        _overlay?.ShowToast("Frame Step -", "◀");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.NextChapter:
                    if (isMediaLoaded)
                    {
                        _player?.Command("add", "chapter", "1");
                        _overlay?.ShowToast("Next Chapter", "⏭");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.PrevChapter:
                    if (isMediaLoaded)
                    {
                        _player?.Command("add", "chapter", "-1");
                        _overlay?.ShowToast("Previous Chapter", "⏮");
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.Screenshot:
                    if (isMediaLoaded)
                    {
                        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                        var njDir = Path.Combine(pictures, "NJPlayer");
                        Directory.CreateDirectory(njDir);
                        var filename = Path.Combine(njDir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                        _player?.Command("screenshot-to-file", filename);
                        _overlay?.ShowToast("Screenshot Saved", "📸");
                    }
                    else
                    {
                        _overlay?.ShowToast("No media loaded", "📸", isError: true);
                    }
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.MediaInfo:
                    OpenCodecManager();
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.BrightnessUp:
                    AdjustVideoProperty("brightness", 5, "Brightness", "☀️");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.BrightnessDown:
                    AdjustVideoProperty("brightness", -5, "Brightness", "☀️");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ContrastUp:
                    AdjustVideoProperty("contrast", 5, "Contrast", "🌓");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ContrastDown:
                    AdjustVideoProperty("contrast", -5, "Contrast", "🌓");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SaturationUp:
                    AdjustVideoProperty("saturation", 5, "Saturation", "🎨");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.SaturationDown:
                    AdjustVideoProperty("saturation", -5, "Saturation", "🎨");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.GammaUp:
                    AdjustVideoProperty("gamma", 5, "Gamma", "✨");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.GammaDown:
                    AdjustVideoProperty("gamma", -5, "Gamma", "✨");
                    break;

                case NJPlayer.Core.Hotkeys.CommandId.ResetVideoColors:
                    ResetVideoColors();
                    break;

                case >= NJPlayer.Core.Hotkeys.CommandId.JumpTo0Percent and <= NJPlayer.Core.Hotkeys.CommandId.JumpTo90Percent:
                    var pct = ((int)command - (int)NJPlayer.Core.Hotkeys.CommandId.JumpTo0Percent) * 10;
                    var totalDur = _player?.Duration.TotalSeconds ?? 0;
                    if (totalDur > 0)
                    {
                        _player?.Seek(totalDur * (pct / 100.0), exact: true);
                        _overlay?.ShowToast($"Jump to {pct}%", "⏩");
                    }
                    break;

                default:
                    _overlay?.ShowToast(command.ToString(), "⚡");
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Hotkey execution error for {command}: {ex.Message}");
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        _overlay?.NotifyMouseMove();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Save current resume position
        if (_player != null && !string.IsNullOrEmpty(_currentFilePath))
        {
            var pos = _player.Position.TotalSeconds;
            var dur = _player.Duration.TotalSeconds;
            _settingsManager.SetResumePosition(_currentFilePath, pos, dur);
        }

        // Save window geometry if enabled
        if (_settingsManager.Settings.RememberWindowPosition)
        {
            if (WindowState == WindowState.Maximized)
            {
                _settingsManager.Settings.IsWindowMaximized = true;
            }
            else
            {
                _settingsManager.Settings.IsWindowMaximized = false;
                _settingsManager.SaveWindowGeometry(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height, false);
            }
        }

        if (_audioSuite != null)
        {
            _settingsManager.Settings.Audio = _audioSuite.CurrentSettings.ToConfig();
        }

        _audioPanel?.ForceClose();
        _audioPanel = null;

        _subtitlePicker?.ForceClose();
        _subtitlePicker = null;

        _trimmerWindow?.ForceClose();
        _trimmerWindow = null;

        _downloaderWindow?.ForceClose();
        _downloaderWindow = null;

        _radioPanelWindow?.ForceClose();
        _radioPanelWindow = null;

        try
        {
            _settingsManager.Save();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not save settings.json: {ex.Message}");
        }

        try
        {
            _videoPlaylistManager.SaveToJson();
            _musicPlaylistManager.SaveToJson(GetMusicQueueFilePath());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not save playlists: {ex.Message}");
        }

        _overlay?.Close();
        _overlay = null;

        _player?.Dispose();
        _player = null;
    }
}
