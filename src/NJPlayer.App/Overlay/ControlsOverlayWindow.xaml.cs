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
    private readonly DoubleAnimation _fadeInAnimation;
    private readonly DoubleAnimation _fadeOutAnimation;

    private MpvPlayer? _player;
    private bool _areControlsVisible = true;
    private bool _isUserScrubbing = false;
    private bool _isAlwaysOnTop = false;

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

    public void SyncWithMainWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        Left = _mainWindow.Left;
        Top = _mainWindow.Top;
        Width = Math.Max(100, _mainWindow.ActualWidth);
        Height = Math.Max(100, _mainWindow.ActualHeight);

        MaxIcon.Text = _mainWindow.WindowState == WindowState.Maximized ? "❐" : "⬜";
    }

    public void UpdateTitle(string title)
    {
        OverlayTitleText.Text = string.IsNullOrWhiteSpace(title) ? "NJ Player - Fast. Dark. Yours." : title;
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

        // Do not auto-hide while scrubbing the seekbar or if mouse is currently over interactive controls
        if (_isUserScrubbing || TopBar.IsMouseOver || BottomBar.IsMouseOver)
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

            if (!_isUserScrubbing && dur.TotalSeconds > 0)
            {
                SeekSlider.Value = (pos.TotalSeconds / dur.TotalSeconds) * 100.0;
            }
        });
    }

    private void Player_DurationChanged(object? sender, TimeSpan dur)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var pos = _player?.Position ?? TimeSpan.Zero;
            UpdateTimeDisplay(pos, dur);
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
            SeekSlider.Value = 0;
            ShowControls();
        });
    }

    // --- Interactive Control Handlers ---

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _player?.TogglePause();
        ShowControls();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _player?.Stop();
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

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserScrubbing = true;
        _autoHideTimer.Stop();
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isUserScrubbing && _player != null)
        {
            _isUserScrubbing = false;
            var dur = _player.Duration.TotalSeconds;
            if (dur > 0)
            {
                var targetSeconds = (SeekSlider.Value / 100.0) * dur;
                _player.Seek(targetSeconds, exact: true);
            }
            RestartAutoHideTimer();
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUserScrubbing && _player != null)
        {
            var dur = _player.Duration.TotalSeconds;
            if (dur > 0)
            {
                var targetSeconds = (e.NewValue / 100.0) * dur;
                _player.Seek(targetSeconds, exact: false);
            }
        }
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ToggleFullscreen();
        ShowControls();
    }

    // --- Window Caption Handlers ---

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _mainWindow.ToggleMaximize();
        }
        else
        {
            _mainWindow.DragMove();
        }
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
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.Close();
    }
}

