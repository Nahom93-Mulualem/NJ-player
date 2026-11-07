using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NJPlayer.Core.Thumbnails;

namespace NJPlayer.App.Views;

public sealed class SeekEventArgs : EventArgs
{
    public double TargetSeconds { get; }
    public bool IsExact { get; }

    public SeekEventArgs(double targetSeconds, bool isExact)
    {
        TargetSeconds = targetSeconds;
        IsExact = isExact;
    }
}

/// <summary>
/// Custom interactive seekbar with grow-on-hover track, buffered range, chapter gaps,
/// 80ms debounced thumbnail hover previews, live scrubbing, and keyboard support.
/// </summary>
public partial class PlayerSeekbar : UserControl
{
    private readonly DoubleAnimation _growAnimation;
    private readonly DoubleAnimation _shrinkAnimation;
    private readonly DoubleAnimation _thumbFadeIn;
    private readonly DoubleAnimation _thumbFadeOut;
    private readonly DispatcherTimer _thumbnailDebounceTimer;
    private readonly Stopwatch _seekStopwatch = new();
    private readonly IThumbnailService _thumbnailService = new FfmpegThumbnailService();

    private CancellationTokenSource? _thumbnailCts;
    private bool _isScrubbing = false;
    private double _positionSeconds = 0;
    private double _durationSeconds = 0;
    private double _bufferedSeconds = 0;
    private double _pendingHoverSeconds = 0;
    private string? _currentFilePath;

    public event EventHandler<SeekEventArgs>? SeekRequested;

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            _positionSeconds = value;
            if (!_isScrubbing)
            {
                UpdatePlayedVisual();
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            _durationSeconds = Math.Max(0, value);
            UpdatePlayedVisual();
            UpdateBufferedVisual();
        }
    }

    public double BufferedSeconds
    {
        get => _bufferedSeconds;
        set
        {
            _bufferedSeconds = Math.Max(0, value);
            UpdateBufferedVisual();
        }
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set
        {
            if (_currentFilePath != value)
            {
                _currentFilePath = value;
                _thumbnailService.ClearCache();
            }
        }
    }

    public PlayerSeekbar()
    {
        InitializeComponent();

        // Fast motion animations (120ms per DESIGN.md motion spec)
        _growAnimation = new DoubleAnimation(8.0, TimeSpan.FromMilliseconds(120));
        _shrinkAnimation = new DoubleAnimation(4.0, TimeSpan.FromMilliseconds(120));
        _thumbFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
        _thumbFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));

        // 80ms debounce timer for thumbnail requests per Prompt 5 spec
        _thumbnailDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _thumbnailDebounceTimer.Tick += ThumbnailDebounceTimer_Tick;
    }

    public void SetChapters(IEnumerable<double> chapterSeconds)
    {
        ChapterCanvas.Children.Clear();
        if (_durationSeconds <= 0 || chapterSeconds == null) return;

        foreach (var chap in chapterSeconds)
        {
            if (chap <= 0 || chap >= _durationSeconds) continue;
            var fraction = chap / _durationSeconds;
            var gap = new System.Windows.Shapes.Rectangle
            {
                Width = 2,
                Height = 8,
                Fill = (System.Windows.Media.Brush)FindResource("BgBrush")
            };
            Canvas.SetLeft(gap, fraction * ActualWidth - 1);
            ChapterCanvas.Children.Add(gap);
        }
    }

    private void UpdatePlayedVisual()
    {
        if (_durationSeconds <= 0 || ActualWidth <= 0)
        {
            PlayedTrack.Width = 0;
            Canvas.SetLeft(Thumb, -7);
            return;
        }

        var fraction = Math.Clamp(_positionSeconds / _durationSeconds, 0.0, 1.0);
        var playedPx = fraction * ActualWidth;
        PlayedTrack.Width = playedPx;
        Canvas.SetLeft(Thumb, playedPx - 7);
    }

    private void UpdateBufferedVisual()
    {
        if (_durationSeconds <= 0 || ActualWidth <= 0)
        {
            BufferedTrack.Width = 0;
            return;
        }

        var fraction = Math.Clamp(_bufferedSeconds / _durationSeconds, 0.0, 1.0);
        BufferedTrack.Width = fraction * ActualWidth;
    }

    private void Seekbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePlayedVisual();
        UpdateBufferedVisual();
    }

    // --- Hover Interactions ---

    private void Seekbar_MouseEnter(object sender, MouseEventArgs e)
    {
        TrackGrid.BeginAnimation(HeightProperty, _growAnimation);
        Thumb.BeginAnimation(OpacityProperty, _thumbFadeIn);
    }

    private void Seekbar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isScrubbing)
        {
            TrackGrid.BeginAnimation(HeightProperty, _shrinkAnimation);
            Thumb.BeginAnimation(OpacityProperty, _thumbFadeOut);
        }

        _thumbnailDebounceTimer.Stop();
        HoverPopup.IsOpen = false;
    }

    private void Seekbar_MouseMove(object sender, MouseEventArgs e)
    {
        var mouseX = e.GetPosition(this).X;
        var fraction = Math.Clamp(mouseX / Math.Max(1, ActualWidth), 0.0, 1.0);
        var targetSeconds = fraction * _durationSeconds;

        if (_isScrubbing)
        {
            _positionSeconds = targetSeconds;
            UpdatePlayedVisual();

            // Live scrubbing with fast (non-exact) seek
            SeekRequested?.Invoke(this, new SeekEventArgs(targetSeconds, isExact: false));
        }

        // Show Instant Time Label above cursor
        _pendingHoverSeconds = targetSeconds;
        var timeSpan = TimeSpan.FromSeconds(targetSeconds);
        PopupTimeText.Text = timeSpan.TotalHours >= 1 ? timeSpan.ToString(@"hh\:mm\:ss") : timeSpan.ToString(@"mm\:ss");

        // Clamp popup within seekbar horizontal boundary (160px width popup)
        var popupLeft = Math.Clamp(mouseX - 80, 0, Math.Max(0, ActualWidth - 160));
        HoverPopup.HorizontalOffset = popupLeft;

        if (!HoverPopup.IsOpen)
        {
            HoverPopup.IsOpen = true;
        }

        // Restart 80ms debounce timer for thumbnail image loading
        _thumbnailDebounceTimer.Stop();
        _thumbnailDebounceTimer.Start();
    }

    private async void ThumbnailDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _thumbnailDebounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(_currentFilePath) || !File.Exists(_currentFilePath))
        {
            PopupImage.Source = null;
            PopupLoadingText.Visibility = Visibility.Visible;
            return;
        }

        _thumbnailCts?.Cancel();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            var bytes = await _thumbnailService.GetThumbnailAsync(_currentFilePath, _pendingHoverSeconds, token);
            if (bytes != null && bytes.Length > 0 && !token.IsCancellationRequested)
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                PopupImage.Source = bitmap;
                PopupLoadingText.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer Thumbnail Preview Error] {ex.Message}");
        }
    }

    // --- Mouse Scrubbing ---

    private void Seekbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CaptureMouse();
        Focus();
        _isScrubbing = true;

        var mouseX = e.GetPosition(this).X;
        var fraction = Math.Clamp(mouseX / Math.Max(1, ActualWidth), 0.0, 1.0);
        var targetSeconds = fraction * _durationSeconds;

        _positionSeconds = targetSeconds;
        UpdatePlayedVisual();

        _seekStopwatch.Restart();
        SeekRequested?.Invoke(this, new SeekEventArgs(targetSeconds, isExact: false));
    }

    private void Seekbar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isScrubbing)
        {
            _isScrubbing = false;
            ReleaseMouseCapture();

            var mouseX = e.GetPosition(this).X;
            var fraction = Math.Clamp(mouseX / Math.Max(1, ActualWidth), 0.0, 1.0);
            var targetSeconds = fraction * _durationSeconds;

            _positionSeconds = targetSeconds;
            UpdatePlayedVisual();

            // Release: perform one exact seek and record latency
            SeekRequested?.Invoke(this, new SeekEventArgs(targetSeconds, isExact: true));
            _seekStopwatch.Stop();
            Trace.WriteLine($"[NJPlayer Seek Latency] Exact seek to {targetSeconds:F2}s executed in {_seekStopwatch.ElapsedMilliseconds}ms.");

            if (!IsMouseOver)
            {
                TrackGrid.BeginAnimation(HeightProperty, _shrinkAnimation);
                Thumb.BeginAnimation(OpacityProperty, _thumbFadeOut);
                HoverPopup.IsOpen = false;
            }
        }
    }

    // --- Keyboard Navigation ---

    private void Seekbar_KeyDown(object sender, KeyEventArgs e)
    {
        if (_durationSeconds <= 0) return;

        double stepSeconds = 5.0;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            stepSeconds = 1.0;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            stepSeconds = 30.0;
        }

        if (e.Key == Key.Left)
        {
            var target = Math.Max(0, _positionSeconds - stepSeconds);
            _seekStopwatch.Restart();
            SeekRequested?.Invoke(this, new SeekEventArgs(target, isExact: true));
            _seekStopwatch.Stop();
            Trace.WriteLine($"[NJPlayer Seek Latency] Keyboard step to {target:F2}s executed in {_seekStopwatch.ElapsedMilliseconds}ms.");
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            var target = Math.Min(_durationSeconds, _positionSeconds + stepSeconds);
            _seekStopwatch.Restart();
            SeekRequested?.Invoke(this, new SeekEventArgs(target, isExact: true));
            _seekStopwatch.Stop();
            Trace.WriteLine($"[NJPlayer Seek Latency] Keyboard step to {target:F2}s executed in {_seekStopwatch.ElapsedMilliseconds}ms.");
            e.Handled = true;
        }
    }

    private void Seekbar_GotFocus(object sender, RoutedEventArgs e)
    {
        FocusRing.Opacity = 1.0;
    }

    private void Seekbar_LostFocus(object sender, RoutedEventArgs e)
    {
        FocusRing.Opacity = 0.0;
    }
}
