using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NJPlayer.Core.Playback;

namespace NJPlayer.App;

/// <summary>
/// Main video playback window hosting the mpv child window and debug telemetry.
/// </summary>
public partial class MainWindow : Window
{
    private MpvPlayer? _player;
    private bool _isDllAvailable = true;
    private string _currentFilePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializePlayer();
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

            _player.TimePositionChanged += Player_TimePositionChanged;
            _player.DurationChanged += Player_DurationChanged;
            _player.PauseChanged += Player_PauseChanged;
            _player.EndFile += Player_EndFile;
            _player.Error += Player_Error;

            HwdecText.Text = "hwdec: auto-safe (ready)";
            Trace.WriteLine("[NJPlayer] mpv engine initialized with auto-safe hardware decoding.");
        }
        catch (DllNotFoundException ex)
        {
            _isDllAvailable = false;
            DllNoticeBorder.Visibility = Visibility.Visible;
            HwdecText.Text = "hwdec: missing libmpv-2.dll";
            StatusBadge.Text = "NO MPV";
            StatusBadge.Background = System.Windows.Media.Brushes.Crimson;
            StatusBadge.Foreground = System.Windows.Media.Brushes.White;
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

        _currentFilePath = filePath;
        FileTitleText.Text = Path.GetFileName(filePath);
        EmptyStateOverlay.Visibility = Visibility.Collapsed;
        StatusBadge.Text = "LOADING";

        try
        {
            _player.Load(filePath, startPaused: false);
            CheckHardwareDecoder();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading media file: {ex.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CheckHardwareDecoder()
    {
        if (_player == null) return;

        Dispatcher.InvokeAsync(async () =>
        {
            // Give mpv a brief moment to initialize the codec and negotiate hardware decoding
            await System.Threading.Tasks.Task.Delay(300);

            var activeHwdec = _player.HardwareDecoder ?? "none/auto";
            HwdecText.Text = $"hwdec: {activeHwdec}";
            Trace.WriteLine($"[NJPlayer] Active hardware decoder reported by mpv: '{activeHwdec}' for '{Path.GetFileName(_currentFilePath)}'");
        });
    }

    private void Player_TimePositionChanged(object? sender, TimeSpan pos)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateTimeDisplay(pos, _player?.Duration ?? TimeSpan.Zero);
            CheckHardwareDecoderOnFirstFrame();
        });
    }

    private bool _hwdecLogged = false;
    private void CheckHardwareDecoderOnFirstFrame()
    {
        if (!_hwdecLogged && _player != null)
        {
            var hwdec = _player.HardwareDecoder;
            if (!string.IsNullOrEmpty(hwdec) && !string.Equals(hwdec, "no", StringComparison.OrdinalIgnoreCase))
            {
                _hwdecLogged = true;
                HwdecText.Text = $"hwdec: {hwdec}";
                Trace.WriteLine($"[NJPlayer] Playback confirmed with active hardware decoder: {hwdec}");
            }
        }
    }

    private void Player_DurationChanged(object? sender, TimeSpan dur)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateTimeDisplay(_player?.Position ?? TimeSpan.Zero, dur);
        });
    }

    private void UpdateTimeDisplay(TimeSpan pos, TimeSpan dur)
    {
        var posStr = pos.TotalHours >= 1 ? pos.ToString(@"hh\:mm\:ss") : pos.ToString(@"mm\:ss");
        var durStr = dur.TotalHours >= 1 ? dur.ToString(@"hh\:mm\:ss") : dur.ToString(@"mm\:ss");
        TimeDebugText.Text = $"{posStr} / {durStr}";
    }

    private void Player_PauseChanged(object? sender, bool isPaused)
    {
        Dispatcher.InvokeAsync(() =>
        {
            StatusBadge.Text = isPaused ? "PAUSED" : "PLAYING";
            StatusBadge.Background = isPaused
                ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#8B98A5")!
                : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#00E5FF")!;
        });
    }

    private void Player_EndFile(object? sender, EndFileEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            StatusBadge.Text = e.Reason == EndFileReason.Eof ? "COMPLETED" : "STOPPED";
            _hwdecLogged = false;
        });
    }

    private void Player_Error(object? sender, MpvErrorEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Trace.WriteLine($"[NJPlayer MPV ERROR] {e.Message} (code: {e.ErrorCode})");
        });
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        PromptOpenFile();
    }

    private void PromptOpenFile()
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            PromptOpenFile();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            _player?.TogglePause();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _player?.Dispose();
        _player = null;
    }
}
