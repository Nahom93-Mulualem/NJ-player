using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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
    private readonly NJPlayer.Core.Playlist.PlaylistManager _playlistManager = new();
    private readonly SettingsManager _settingsManager = new();
    private readonly NJPlayer.Features.Subtitles.SubtitleService _subtitleService = new();
    private readonly NJPlayer.Features.Trimmer.MediaTrimmerService _trimmerService = new();
    private readonly NJPlayer.Features.Downloader.MediaDownloaderService _downloaderService = new();
    private NJPlayer.Features.Audio.IAudioSuite? _audioSuite;
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
            _playlistManager.LoadFromJson();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not load playlist.json: {ex.Message}");
        }

        _overlay.AttachPlaylist(_playlistManager);
        _playlistManager.CurrentItemChanged += PlaylistManager_CurrentItemChanged;

        _overlay.Show();
        _overlay.SyncWithMainWindow();

        // Hook Win32 window message loop for tear-free synchronous overlay tracking
        var source = (System.Windows.Interop.HwndSource?)PresentationSource.FromVisual(this);
        source?.AddHook(WndProc);

        UpdateRecentFilesList();

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
            _player = new MpvPlayer();
            _player.Initialize(options =>
            {
                options.WithWindowHandle(PlayerHost.Hwnd);
                options.WithHardwareDecoding(string.IsNullOrWhiteSpace(hwdec) ? "auto-safe" : hwdec);
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

        // Save position of previous file before switching
        if (!string.IsNullOrEmpty(_currentFilePath))
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
        PlayerHost.Visibility = Visibility.Visible;
        EmptyStateOverlay.Visibility = Visibility.Collapsed;
        _overlay?.UpdateTitle(fileName, filePath);

        try
        {
            ApplyVisualizerFilter("off");
            _player.Load(filePath, startPaused: false);
            ApplyAudioFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading media file: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        var filter = Features.Radio.RadioBrowserService.BuildVisualizerFilter(mode);
        try
        {
            _player.SetPropertyString("lavfi-complex", filter);
        }
        catch (Exception ex)
        {
            NJPlayer.Core.Diagnostics.LogService.LogError("Visualizer.ApplyFilter", ex);
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
    }

    private void Player_Error(object? sender, MpvErrorEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Trace.WriteLine($"[NJPlayer MPV ERROR] {e.Message} (code: {e.ErrorCode})");
        });
    }

    public void PromptOpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Media or Playlist File - NJ Player",
            Filter = "Media & Playlists|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.ts;*.m4v;*.mp3;*.flac;*.wav;*.m4a;*.opus;*.ogg;*.m3u;*.m3u8|M3U Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All Files (*.*)|*.*",
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

    private void ExecuteCommand(NJPlayer.Core.Hotkeys.CommandId command)
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
                    if (isMediaLoaded)
                    {
                        var title = Path.GetFileName(_currentFilePath) ?? "Media";
                        var hwdec = _player?.HardwareDecoder ?? "Software";
                        _overlay?.ShowToast($"{title} ({hwdec})", "ℹ");
                    }
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
            _playlistManager.SaveToJson();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer] Could not save playlist.json: {ex.Message}");
        }

        _overlay?.Close();
        _overlay = null;

        _player?.Dispose();
        _player = null;
    }
}
