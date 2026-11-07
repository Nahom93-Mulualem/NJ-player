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
    private bool _isDllAvailable = true;
    private bool _isFullscreen = false;

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
        _overlay?.UpdateTitle(fileName);

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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            PromptOpenFile();
            e.Handled = true;
        }
        else if (e.Key == Key.F || e.Key == Key.Enter)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            _player?.TogglePause();
            e.Handled = true;
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
