using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NJPlayer.App.Overlay;
using NJPlayer.Core.Playback;

namespace NJPlayer.App;

/// <summary>
/// Frameless main player window hosting the Win32 mpv video surface and managing the airspace controls overlay.
/// </summary>
public partial class MainWindow : Window
{
    private MpvPlayer? _player;
    private ControlsOverlayWindow? _overlay;
    private readonly NJPlayer.Core.Hotkeys.HotkeyManager _hotkeyManager = new();
    private bool _isDllAvailable = true;
    private bool _isFullscreen = false;
    private double _lastNonMuteVolume = 100;
    private int _subDelayMs = 0;

    private WindowState _savedState = WindowState.Normal;
    private WindowStyle _savedStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _savedResize = ResizeMode.CanResize;

    public bool IsFullscreen => _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();

        LocationChanged += (_, _) => _overlay?.SyncWithMainWindow();
        SizeChanged += (_, _) => _overlay?.SyncWithMainWindow();
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        WindowOutline.BorderThickness = WindowState == WindowState.Maximized || _isFullscreen
            ? new Thickness(0)
            : new Thickness(1);

        _overlay?.SyncWithMainWindow();
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
        _overlay.Show();
        _overlay.SyncWithMainWindow();
    }

    private void InitializePlayer()
    {
        try
        {
            _player = new MpvPlayer();
            _player.Initialize(options =>
            {
                options.WithWindowHandle(PlayerHost.Hwnd);
                options.WithHardwareDecoding("auto-safe");
                options.WithKeepOpen(true);
                options.WithIdle(true);
            });

            _player.Error += Player_Error;
            Trace.WriteLine("[NJPlayer] mpv engine initialized with auto-safe hardware decoding.");
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

        var fileName = Path.GetFileName(filePath);
        EmptyStateOverlay.Visibility = Visibility.Collapsed;
        _overlay?.UpdateTitle(fileName, filePath);

        try
        {
            _player.Load(filePath, startPaused: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading media file: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _savedState = WindowState;
            _savedStyle = WindowStyle;
            _savedResize = ResizeMode;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            WindowOutline.BorderThickness = new Thickness(0);
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            WindowState = _savedState;
            WindowOutline.BorderThickness = new Thickness(1);
            _isFullscreen = false;
        }

        _overlay?.SyncWithMainWindow();
    }

    public void ToggleMaximize()
    {
        if (_isFullscreen)
        {
            ToggleFullscreen();
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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
            Title = "Select Media File - NJ Player",
            Filter = "Media Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.ts;*.m4v;*.mp3;*.flac;*.wav;*.m4a;*.opus;*.ogg|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadMediaFile(dialog.FileName);
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

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                LoadMediaFile(files[0]);
            }
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = new System.Collections.Generic.List<string>();
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
        switch (command)
        {
            case NJPlayer.Core.Hotkeys.CommandId.PlayPause:
                _player?.TogglePause();
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

            case NJPlayer.Core.Hotkeys.CommandId.SeekForward5s:
                _player?.SeekRelative(5, exact: false);
                _overlay?.ShowToast("+5 s", "⏩");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SeekBackward5s:
                _player?.SeekRelative(-5, exact: false);
                _overlay?.ShowToast("-5 s", "⏪");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SeekForward1s:
                _player?.SeekRelative(1, exact: true);
                _overlay?.ShowToast("+1 s", "⏩");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SeekBackward1s:
                _player?.SeekRelative(-1, exact: true);
                _overlay?.ShowToast("-1 s", "⏪");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SeekForward30s:
                _player?.SeekRelative(30, exact: false);
                _overlay?.ShowToast("+30 s", "⏩");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SeekBackward30s:
                _player?.SeekRelative(-30, exact: false);
                _overlay?.ShowToast("-30 s", "⏪");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SpeedUp:
                var spdUp = Math.Round((_player?.Speed ?? 1.0) + 0.1, 2);
                _player?.SetSpeed(spdUp);
                _overlay?.ShowToast($"Speed {spdUp:0.0#}x", "⚡");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SpeedDown:
                var spdDown = Math.Round(Math.Max(0.1, (_player?.Speed ?? 1.0) - 0.1), 2);
                _player?.SetSpeed(spdDown);
                _overlay?.ShowToast($"Speed {spdDown:0.0#}x", "⚡");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.ResetSpeed:
                _player?.SetSpeed(1.0);
                _overlay?.ShowToast("Speed 1.0x", "⚡");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SubtitleDelayPlus:
                _subDelayMs += 100;
                _player?.Command("add", "sub-delay", "0.1");
                _overlay?.ShowToast($"Sub Delay +{_subDelayMs} ms", "💬");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.SubtitleDelayMinus:
                _subDelayMs -= 100;
                _player?.Command("add", "sub-delay", "-0.1");
                _overlay?.ShowToast($"Sub Delay {_subDelayMs} ms", "💬");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.ToggleSubtitles:
                _player?.Command("cycle", "sub-visibility");
                _overlay?.ShowToast("Toggle Subtitles", "💬");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.CycleAudioTrack:
                _player?.Command("cycle", "audio");
                _overlay?.ShowToast("Cycle Audio Track", "🎵");
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

            case NJPlayer.Core.Hotkeys.CommandId.ToggleAlwaysOnTop:
                Topmost = !Topmost;
                if (_overlay != null) _overlay.Topmost = Topmost;
                _overlay?.ShowToast(Topmost ? "Always on Top: ON" : "Always on Top: OFF", "📌");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.JumpToStart:
                _player?.Seek(0, exact: true);
                _overlay?.ShowToast("Start", "⏮");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.JumpToEnd:
                var dur = _player?.Duration.TotalSeconds ?? 0;
                if (dur > 0) _player?.Seek(dur, exact: true);
                _overlay?.ShowToast("End", "⏭");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.FrameStepForward:
                _player?.Command("frame-step");
                _overlay?.ShowToast("Frame Step +", "▶");
                break;

            case NJPlayer.Core.Hotkeys.CommandId.FrameStepBackward:
                _player?.Command("frame-back-step");
                _overlay?.ShowToast("Frame Step -", "◀");
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

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        _overlay?.NotifyMouseMove();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _overlay?.Close();
        _overlay = null;

        _player?.Dispose();
        _player = null;
    }
}
