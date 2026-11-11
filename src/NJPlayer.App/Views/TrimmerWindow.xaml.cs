using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NJPlayer.Core.Playback;
using NJPlayer.Features.Trimmer;

namespace NJPlayer.App.Views;

/// <summary>
/// Trimmer dialog for lossless stream-copy and re-encoded video trimming.
/// </summary>
public partial class TrimmerWindow : Window
{
    private readonly MpvPlayer? _player;
    private readonly MediaTrimmerService _trimmerService;
    private readonly string _inputFilePath;
    private CancellationTokenSource? _trimCts;
    private bool _isForceClosing = false;

    public event Action<string>? ClipExported;

    public TrimmerWindow(MpvPlayer? player, MediaTrimmerService trimmerService, string inputFilePath)
    {
        InitializeComponent();
        _player = player;
        _trimmerService = trimmerService ?? throw new ArgumentNullException(nameof(trimmerService));
        _inputFilePath = inputFilePath ?? string.Empty;

        InitDefaults();
    }

    private void InitDefaults()
    {
        MissingFfmpegBanner.Visibility = _trimmerService.IsFfmpegAvailable ? Visibility.Collapsed : Visibility.Visible;
        ExportButton.IsEnabled = _trimmerService.IsFfmpegAvailable;

        if (File.Exists(_inputFilePath))
        {
            FileTitleText.Text = Path.GetFileName(_inputFilePath);
            var dir = Path.GetDirectoryName(_inputFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(_inputFilePath);
            var ext = Path.GetExtension(_inputFilePath);
            OutputFileBox.Text = Path.Combine(dir, $"{nameWithoutExt}_trimmed{ext}");

            var currentPos = _player?.Position ?? TimeSpan.Zero;
            var totalDur = _player?.Duration ?? TimeSpan.FromSeconds(60);

            InPointBox.Text = FormatTime(currentPos);
            OutPointBox.Text = FormatTime(currentPos + TimeSpan.FromSeconds(30) < totalDur ? currentPos + TimeSpan.FromSeconds(30) : totalDur);
            UpdateDurationSummary();
        }
    }

    private void HowToFixFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var result = _trimmerService.LookupResult;
        var searchedList = string.Join("\n • ", result.SearchedPaths);
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var recommendedPath = System.IO.Path.Combine(baseDir, "libs", "ffmpeg.exe");

        var msg = "ffmpeg was not found.\n\n" +
                  "How to fix:\n" +
                  "1. Download 'ffmpeg.exe' from ffmpeg.org or gyan.dev/ffmpeg/builds\n" +
                  $"2. Place 'ffmpeg.exe' into:\n   {recommendedPath}\n" +
                  "   (or add it to your Windows system PATH)\n" +
                  "3. Re-open this Trimmer window.\n\n" +
                  $"Searched locations ({result.SearchedPaths.Count}):\n • {searchedList}";

        MessageBox.Show(this, msg, "How to Fix FFmpeg - NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void SetInPoint(TimeSpan time)
    {
        InPointBox.Text = FormatTime(time);
        UpdateDurationSummary();
    }

    public void SetOutPoint(TimeSpan time)
    {
        OutPointBox.Text = FormatTime(time);
        UpdateDurationSummary();
    }

    private void SetInCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_player != null)
        {
            SetInPoint(_player.Position);
        }
    }

    private void SetOutCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_player != null)
        {
            SetOutPoint(_player.Position);
        }
    }

    private void SeekIn_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseTime(InPointBox.Text, out var time))
        {
            _player?.Seek(time.TotalSeconds, exact: true);
        }
    }

    private void SeekOut_Click(object sender, RoutedEventArgs e)
    {
        if (TryParseTime(OutPointBox.Text, out var time))
        {
            _player?.Seek(time.TotalSeconds, exact: true);
        }
    }

    private void UpdateDurationSummary()
    {
        if (TryParseTime(InPointBox.Text, out var inT) && TryParseTime(OutPointBox.Text, out var outT))
        {
            var diff = outT > inT ? outT - inT : TimeSpan.Zero;
            DurationSummaryText.Text = FormatTime(diff);
        }
    }

    private static string FormatTime(TimeSpan ts)
    {
        return ts.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time);
    }

    private void ReEncode_Changed(object sender, RoutedEventArgs e)
    {
        if (ReEncodeCheckBox.IsChecked == true)
        {
            ModeNoteText.Text = "🎬 Re-encode: Frame-accurate cut with H.264/AAC re-encoding. Slightly slower than stream copy.";
        }
        else
        {
            ModeNoteText.Text = "⚡ Stream Copy: Cuts snap to nearest keyframe (I-frame) for instantaneous export without re-encoding or quality degradation.";
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var ext = Path.GetExtension(_inputFilePath);
        var sfd = new SaveFileDialog
        {
            Title = "Save Trimmed Clip As - NJ Player",
            Filter = $"Video File (*{ext})|*{ext}|All Files (*.*)|*.*",
            FileName = Path.GetFileName(OutputFileBox.Text)
        };

        if (sfd.ShowDialog(this) == true)
        {
            OutputFileBox.Text = sfd.FileName;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseTime(InPointBox.Text, out var inPoint))
        {
            MessageBox.Show("Please enter a valid Start Point format (HH:MM:SS.mmm).", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseTime(OutPointBox.Text, out var outPoint) || outPoint <= inPoint)
        {
            MessageBox.Show("End Point must be greater than Start Point.", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outputPath = OutputFileBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            MessageBox.Show("Please specify a valid destination file path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = new TrimOptions
        {
            InputFilePath = _inputFilePath,
            OutputFilePath = outputPath,
            InPoint = inPoint,
            OutPoint = outPoint,
            ReEncode = ReEncodeCheckBox.IsChecked == true
        };

        _trimCts = new CancellationTokenSource();
        ProgressSection.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        ExportButton.IsEnabled = false;

        var progressReporter = new Progress<TrimProgress>(p =>
        {
            TrimProgressBar.Value = p.Percentage;
            ProgressPctText.Text = $"{p.Percentage:F0}%";
            ProgressStatusText.Text = p.StatusMessage;
        });

        try
        {
            await _trimmerService.TrimAsync(options, progressReporter, _trimCts.Token);
            ClipExported?.Invoke(outputPath);

            var result = MessageBox.Show(
                $"Trim complete!\nSaved to:\n{outputPath}\n\nWould you like to play this clip now?",
                "Export Complete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                _player?.Load(outputPath, startPaused: false);
            }

            Hide();
        }
        catch (OperationCanceledException)
        {
            ProgressStatusText.Text = "Trimming cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Trimming failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ProgressStatusText.Text = "Error during export.";
        }
        finally
        {
            ExportButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _trimCts?.Cancel();
    }

    public void ForceClose()
    {
        _isForceClosing = true;
        _trimCts?.Cancel();
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
    }
}

