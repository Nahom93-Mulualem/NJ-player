using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NJPlayer.Features.Downloader;

namespace NJPlayer.App.Views;

/// <summary>
/// Downloader dialog wrapping yt-dlp for downloading online streams and videos.
/// </summary>
public partial class DownloaderWindow : Window
{
    private readonly MediaDownloaderService _downloaderService;
    private CancellationTokenSource? _downloadCts;
    private bool _isForceClosing = false;

    public event Action<string>? MediaDownloaded;

    public DownloaderWindow(MediaDownloaderService downloaderService)
    {
        InitializeComponent();
        _downloaderService = downloaderService ?? throw new ArgumentNullException(nameof(downloaderService));

        InitDefaults();
    }

    private void InitDefaults()
    {
        MissingYtDlpBanner.Visibility = _downloaderService.IsYtDlpAvailable ? Visibility.Collapsed : Visibility.Visible;
        DownloadButton.IsEnabled = _downloaderService.IsYtDlpAvailable;

        var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        FolderTextBox.Text = downloadsFolder;

        // Auto-paste from clipboard if valid URL is present
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    UrlTextBox.Text = text;
                }
            }
        }
        catch { }
    }

    private void HowToFixYtDlp_Click(object sender, RoutedEventArgs e)
    {
        var result = _downloaderService.LookupResult;
        var searchedList = string.Join("\n • ", result.SearchedPaths);
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var recommendedPath = System.IO.Path.Combine(baseDir, "libs", "yt-dlp.exe");

        var msg = "yt-dlp was not found.\n\n" +
                  "How to fix:\n" +
                  "1. Download 'yt-dlp.exe' from github.com/yt-dlp/yt-dlp/releases\n" +
                  $"2. Place 'yt-dlp.exe' into:\n   {recommendedPath}\n" +
                  "   (or add it to your Windows system PATH)\n" +
                  "3. Re-open this Downloader window.\n\n" +
                  $"Searched locations ({result.SearchedPaths.Count}):\n • {searchedList}";

        MessageBox.Show(this, msg, "How to Fix yt-dlp - NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_downloaderService.IsYtDlpAvailable)
        {
            DownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Download Directory - NJ Player",
            InitialDirectory = FolderTextBox.Text
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderTextBox.Text = dialog.FolderName;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            MessageBox.Show("Please enter a valid HTTP/HTTPS URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folder = FolderTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        var formatPreset = DownloadFormatPreset.BestAvailable;
        if (FormatCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out var tagVal))
        {
            formatPreset = (DownloadFormatPreset)tagVal;
        }

        var options = new DownloadOptions
        {
            Url = url,
            OutputDirectory = folder,
            Format = formatPreset,
            PlayWhenDone = PlayWhenDoneCheckBox.IsChecked == true
        };

        _downloadCts = new CancellationTokenSource();
        ProgressSection.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = false;

        var progressReporter = new Progress<DownloadProgress>(p =>
        {
            DownloadProgressBar.Value = p.Percentage;
            ProgressPctText.Text = $"{p.Percentage:F0}%";
            ProgressStatusText.Text = p.StatusMessage;
        });

        try
        {
            var downloadedFile = await _downloaderService.DownloadAsync(options, progressReporter, _downloadCts.Token);

            ProgressStatusText.Text = "Download complete!";
            if (!string.IsNullOrWhiteSpace(downloadedFile) && File.Exists(downloadedFile))
            {
                if (options.PlayWhenDone)
                {
                    MediaDownloaded?.Invoke(downloadedFile);
                    Hide();
                }
                else
                {
                    var res = MessageBox.Show(
                        $"Download complete!\nSaved to:\n{downloadedFile}\n\nWould you like to play it now?",
                        "Download Complete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes)
                    {
                        MediaDownloaded?.Invoke(downloadedFile);
                        Hide();
                    }
                }
            }
            else
            {
                MessageBox.Show("Download completed, but output file could not be determined. Check your downloads folder.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStatusText.Text = "Download cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Download failed: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ProgressStatusText.Text = "Error during download.";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    public void ForceClose()
    {
        _isForceClosing = true;
        _downloadCts?.Cancel();
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

