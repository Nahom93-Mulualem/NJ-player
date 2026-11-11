using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NJPlayer.Core.Playback;

namespace NJPlayer.App.Overlay;

/// <summary>
/// Transparent owned overlay window that solves the WPF airspace issue over the Win32 video child window.
/// Synchronizes position, DPI, state with MainWindow and manages the 2.5s auto-hide controls.
/// </summary>
public partial class ControlsOverlayWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _seekRepeatTimer;
    private readonly DoubleAnimation _fadeInAnimation;
    private readonly DoubleAnimation _fadeOutAnimation;

    private MpvPlayer? _player;
    private NJPlayer.Core.Playlist.PlaylistManager? _playlistManager;
    private bool _areControlsVisible = true;
    private bool _isUserScrubbing = false;
    private bool _isAlwaysOnTop = false;
    private double _seekRepeatDelta = 0;
    private bool _isRepeating = false;

    public ControlsOverlayWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        Owner = _mainWindow;

        // Auto-hide timer configured to 2500ms per DESIGN.md
        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;

        // Long-press repeat timer for +/-10s seek buttons
        _seekRepeatTimer = new DispatcherTimer();
        _seekRepeatTimer.Tick += SeekRepeatTimer_Tick;

        // Fast motion animations (120ms per DESIGN.md)
        _fadeInAnimation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
        _fadeOutAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));

        Loaded += ControlsOverlayWindow_Loaded;
    }

    private void ControlsOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SyncWithMainWindow();
        RestartAutoHideTimer();
    }

    public void AttachPlayer(MpvPlayer player)
    {
        _player = player;
        if (_player == null) return;

        _player.TimePositionChanged += Player_TimePositionChanged;
        _player.DurationChanged += Player_DurationChanged;
        _player.PauseChanged += Player_PauseChanged;
        _player.VolumeChanged += Player_VolumeChanged;
        _player.EndFile += Player_EndFile;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public void SyncWithMainWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
        var overlayHelper = new System.Windows.Interop.WindowInteropHelper(this);

        if (helper.Handle != IntPtr.Zero && GetWindowRect(helper.Handle, out RECT rect))
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_mainWindow);
            var dipLeft = rect.Left / dpi.DpiScaleX;
            var dipTop = rect.Top / dpi.DpiScaleY;
            var dipWidth = Math.Max(100.0, (rect.Right - rect.Left) / dpi.DpiScaleX);
            var dipHeight = Math.Max(100.0, (rect.Bottom - rect.Top) / dpi.DpiScaleY);

            if (overlayHelper.Handle != IntPtr.Zero)
            {
                SetWindowPos(overlayHelper.Handle, IntPtr.Zero,
                    rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }

            Left = dipLeft;
            Top = dipTop;
            Width = dipWidth;
            Height = dipHeight;
        }
        else
        {
            Left = _mainWindow.Left;
            Top = _mainWindow.Top;
            Width = Math.Max(100, _mainWindow.ActualWidth);
            Height = Math.Max(100, _mainWindow.ActualHeight);
        }

        MaxIcon.Text = _mainWindow.WindowState == WindowState.Maximized ? "❐" : "⬜";

        // Responsive reflow down to 480x270 per Issue 3
        var currentWidth = Width;
        bool isCompact = currentWidth < 680;
        TrimmerButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        DownloaderButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        OnlineRadioButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        EqualizerButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        GeneralSettingsButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        OverflowButton.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;

        bool isNarrow = currentWidth < 540;
        VolumeSlider.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;
    }

    public void EnterFullscreen()
    {
        TopBar.Visibility = Visibility.Collapsed;
        FullscreenIcon.Text = "⤦";
        FullscreenButton.ToolTip = "Exit Fullscreen (F, Enter, Esc)";
    }

    public void ExitFullscreen()
    {
        TopBar.Visibility = Visibility.Visible;
        FullscreenIcon.Text = "⛶";
        FullscreenButton.ToolTip = "Fullscreen (F, Enter)";
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Double clicking empty surface area outside controls toggles fullscreen
        if (e.ClickCount == 2 && !TopBar.IsMouseOver && !BottomBar.IsMouseOver && !Drawer.IsOpen)
        {
            _mainWindow.ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (OverflowButton.ContextMenu != null)
        {
            OverflowButton.ContextMenu.PlacementTarget = OverflowButton;
            OverflowButton.ContextMenu.IsOpen = true;
        }
    }

    public void UpdateTitle(string title, string? filePath = null)
    {
        OverlayTitleText.Text = string.IsNullOrWhiteSpace(title) ? "NJ Player - Fast. Dark. Yours." : title;
        Seekbar.CurrentFilePath = filePath;
    }

    public void NotifyMouseMove()
    {
        ShowControls();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_areControlsVisible && !_isUserScrubbing)
        {
            RestartAutoHideTimer();
        }
    }

    private void ShowControls()
    {
        _autoHideTimer.Stop();

        if (!_areControlsVisible)
        {
            _areControlsVisible = true;
            TopBar.BeginAnimation(OpacityProperty, _fadeInAnimation);
            BottomBar.BeginAnimation(OpacityProperty, _fadeInAnimation);
        }

        // Restore cursor if it was hidden in fullscreen
        if (Mouse.OverrideCursor != null)
        {
            Mouse.OverrideCursor = null;
        }

        RestartAutoHideTimer();
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();

        // Do not auto-hide while scrubbing the seekbar, drawer is open, or mouse is over interactive controls
        if (_isUserScrubbing || TopBar.IsMouseOver || BottomBar.IsMouseOver || Drawer.IsOpen || Drawer.IsMouseOver)
        {
            RestartAutoHideTimer();
            return;
        }

        if (_areControlsVisible)
        {
            _areControlsVisible = false;
            TopBar.BeginAnimation(OpacityProperty, _fadeOutAnimation);
            BottomBar.BeginAnimation(OpacityProperty, _fadeOutAnimation);

            // Hide cursor in fullscreen mode per DESIGN.md section 2
            if (_mainWindow.IsFullscreen)
            {
                Mouse.OverrideCursor = Cursors.None;
            }
        }
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    // --- Media Player Event Handlers ---

    private void Player_TimePositionChanged(object? sender, TimeSpan pos)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var dur = _player?.Duration ?? TimeSpan.Zero;
            UpdateTimeDisplay(pos, dur);
            Seekbar.PositionSeconds = pos.TotalSeconds;
        });
    }

    private void Player_DurationChanged(object? sender, TimeSpan dur)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var pos = _player?.Position ?? TimeSpan.Zero;
            UpdateTimeDisplay(pos, dur);
            Seekbar.DurationSeconds = dur.TotalSeconds;
        });
    }

    private void UpdateTimeDisplay(TimeSpan pos, TimeSpan dur)
    {
        var posStr = pos.TotalHours >= 1 ? pos.ToString(@"hh\:mm\:ss") : pos.ToString(@"mm\:ss");
        var durStr = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
        OverlayTimeText.Text = $"{posStr} / {durStr}";
    }

    private void Player_PauseChanged(object? sender, bool isPaused)
    {
        Dispatcher.InvokeAsync(() =>
        {
            PlayPauseIcon.Text = isPaused ? "▶" : "⏸";
        });
    }

    private void Player_VolumeChanged(object? sender, double vol)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (Math.Abs(VolumeSlider.Value - vol) > 0.5)
            {
                VolumeSlider.Value = Math.Clamp(vol, 0, 100);
            }
            VolumeIcon.Text = vol <= 0 ? "🔇" : (vol < 50 ? "🔉" : "🔊");
        });
    }

    private void Player_EndFile(object? sender, EndFileEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            PlayPauseIcon.Text = "▶";
            Seekbar.PositionSeconds = 0;
            ShowControls();
        });
    }

    // --- Drag & Drop Delegation ---

    private void Overlay_DragOver(object sender, DragEventArgs e)
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

    private void Overlay_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            _mainWindow.HandleDropFiles(files);
            e.Handled = true;
        }
    }

    // --- Interactive Control Handlers ---

    private void Seekbar_SeekRequested(object? sender, Views.SeekEventArgs e)
    {
        if (_player != null && _player.Duration > TimeSpan.Zero)
        {
            _player.Seek(e.TargetSeconds, exact: e.IsExact);
        }
        RestartAutoHideTimer();
    }

    private void StartSeekHold(double deltaSeconds)
    {
        if (_player == null || _player.Duration <= TimeSpan.Zero) return;

        _seekRepeatDelta = deltaSeconds;
        _isRepeating = false;

        // Perform immediate 10s seek on first click
        _player.SeekRelative(deltaSeconds, exact: false);
        ShowToast(deltaSeconds > 0 ? "+10 s" : "-10 s", deltaSeconds > 0 ? "⏩" : "⏪");
        ShowControls();

        // 400ms threshold before repeating
        _seekRepeatTimer.Stop();
        _seekRepeatTimer.Interval = TimeSpan.FromMilliseconds(400);
        _seekRepeatTimer.Start();
    }

    private void SeekRepeatTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null || _player.Duration <= TimeSpan.Zero)
        {
            _seekRepeatTimer.Stop();
            return;
        }

        if (!_isRepeating)
        {
            _isRepeating = true;
            _seekRepeatTimer.Interval = TimeSpan.FromMilliseconds(120); // Smooth repeat tick rate
        }

        _player.SeekRelative(_seekRepeatDelta, exact: false);
        ShowToast(_seekRepeatDelta > 0 ? "+10 s" : "-10 s", _seekRepeatDelta > 0 ? "⏩" : "⏪");
        ShowControls();
    }

    private void StopSeekHold()
    {
        _seekRepeatTimer.Stop();
        _isRepeating = false;
        RestartAutoHideTimer();
    }

    private void SkipBackward_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartSeekHold(-10.0);
    }

    private void SkipForward_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartSeekHold(10.0);
    }

    private void SeekButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopSeekHold();
    }

    private void SeekButton_MouseLeave(object sender, MouseEventArgs e)
    {
        StopSeekHold();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.Duration <= TimeSpan.Zero)
        {
            if (_playlistManager != null && _playlistManager.Items.Count > 0)
            {
                _playlistManager.SetCurrentIndex(Math.Max(0, _playlistManager.CurrentIndex));
            }
            return;
        }
        _player.TogglePause();
        ShowControls();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null || _player.Duration <= TimeSpan.Zero) return;
        _player.Stop();
        ShowControls();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (VolumeSlider.Value > 0)
        {
            _player.SetVolume(0);
        }
        else
        {
            _player.SetVolume(100);
        }
        ShowControls();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _player?.SetVolume(e.NewValue);
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ToggleFullscreen();
        ShowControls();
    }

    // --- Window Caption Handlers ---

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        _mainWindow.ForwardKeyDown(e);
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _mainWindow.ToggleMaximize();
            SyncWithMainWindow();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            ReleaseCapture();
            var helper = new System.Windows.Interop.WindowInteropHelper(_mainWindow);
            SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            SyncWithMainWindow();
        }
    }

    public void AttachPlaylist(NJPlayer.Core.Playlist.PlaylistManager manager)
    {
        _playlistManager = manager;
        Drawer.AttachManager(manager);
        Drawer.ItemSelected += (_, item) =>
        {
            _mainWindow.LoadMediaFile(item.FilePath);
        };
    }

    public void AttachWatchLater(NJPlayer.Core.Playlist.WatchLaterManager watchLaterManager)
    {
        Drawer.AttachWatchLater(watchLaterManager);
        Drawer.MediaPathRequested += path =>
        {
            _mainWindow.LoadMediaFile(path);
        };
    }

    public void TogglePlaylist()
    {
        Drawer.ToggleDrawer();
        ShowControls();
    }

    private void SubtitleButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenSubtitlePicker();
        ShowControls();
    }

    private void PlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlaylist();
    }

    public void ShowToast(string message, string icon = "⚡", bool isError = false)
    {
        Toast.ShowToast(message, icon, isError);
    }

    private void TrimmerButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenTrimmer();
        ShowControls();
    }

    private void DownloaderButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenDownloader();
        ShowControls();
    }

    private void OnlineRadioButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenRadioBrowser();
        ShowControls();
    }

    private void EqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ToggleEqualizer();
        ShowControls();
    }

    private void GeneralSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenSettings();
        ShowControls();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenHotkeySettings();
        ShowControls();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isAlwaysOnTop = !_isAlwaysOnTop;
        _mainWindow.Topmost = _isAlwaysOnTop;
        Topmost = _isAlwaysOnTop;
        PinButton.Foreground = _isAlwaysOnTop
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.WindowState = WindowState.Minimized;
    }

    private void MaxButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ToggleMaximize();
        SyncWithMainWindow();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.Close();
    }
}

