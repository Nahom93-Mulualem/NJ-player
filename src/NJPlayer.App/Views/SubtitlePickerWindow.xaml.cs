using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NJPlayer.Core.Playback;
using NJPlayer.Features.Subtitles;

namespace NJPlayer.App.Views;

/// <summary>
/// Subtitle picker dialog for loading local subtitles, track switching,
/// timing/font customization (including Ge'ez/Amharic), and online downloading.
/// </summary>
public partial class SubtitlePickerWindow : Window
{
    private readonly MpvPlayer? _player;
    private readonly SubtitleService _subtitleService;
    private readonly string? _currentMediaFilePath;
    private CancellationTokenSource? _searchCts;
    private bool _isUpdatingUi = false;
    private bool _isForceClosing = false;

    public event Action<string>? SubtitleLoaded;

    public SubtitlePickerWindow(MpvPlayer? player, SubtitleService subtitleService, string? currentMediaFilePath = null)
    {
        InitializeComponent();
        _player = player;
        _subtitleService = subtitleService ?? throw new ArgumentNullException(nameof(subtitleService));
        _currentMediaFilePath = currentMediaFilePath;

        if (!string.IsNullOrWhiteSpace(_currentMediaFilePath))
        {
            OnlineSearchBox.Text = Path.GetFileNameWithoutExtension(_currentMediaFilePath);
        }

        RefreshTracks();
        SyncUiFromPlayer();
    }

    public void RefreshTracks()
    {
        TrackCombo.Items.Clear();
        TrackCombo.Items.Add(new ComboBoxItem { Content = "None (Subtitles Off)", Tag = "no" });

        if (_player != null)
        {
            // Add automatic track items
            TrackCombo.Items.Add(new ComboBoxItem { Content = "Track 1 (Default / Auto)", Tag = "auto", IsSelected = true });
            TrackCombo.Items.Add(new ComboBoxItem { Content = "Track 2", Tag = "2" });
            TrackCombo.Items.Add(new ComboBoxItem { Content = "Track 3", Tag = "3" });
        }
    }

    private void SyncUiFromPlayer()
    {
        if (_player == null) return;
        _isUpdatingUi = true;

        try
        {
            var delay = _player.GetPropertyDouble("sub-delay") ?? 0.0;
            var delayMs = (int)(delay * 1000.0);
            DelaySlider.Value = delayMs;
            DelayText.Text = $"{delayMs} ms";

            var fontSize = _player.GetPropertyDouble("sub-font-size") ?? 48.0;
            FontSizeSlider.Value = fontSize;
            FontSizeText.Text = $"{fontSize:0} px";
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void BrowseSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Subtitle File - NJ Player",
            Filter = "Subtitle Files (*.srt;*.ass;*.ssa;*.vtt;*.sub)|*.srt;*.ass;*.ssa;*.vtt;*.sub|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadSubtitleFile(dialog.FileName);
        }
    }

    public void LoadSubtitleFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            _player?.AddSubtitle(filePath, select: true);
            SubtitleLoaded?.Invoke(Path.GetFileName(filePath));

            var item = new ComboBoxItem
            {
                Content = $"External: {Path.GetFileName(filePath)}",
                Tag = filePath,
                IsSelected = true
            };
            TrackCombo.Items.Add(item);
            TrackCombo.SelectedItem = item;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load subtitle: {ex.Message}", "Subtitle Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TrackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _player == null) return;

        if (TrackCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (tag == "no")
            {
                _player.SetPropertyFlag("sub-visibility", false);
            }
            else if (tag == "auto")
            {
                _player.SetPropertyFlag("sub-visibility", true);
                _player.SetPropertyString("sid", "auto");
            }
            else if (File.Exists(tag))
            {
                // External file already added
                _player.SetPropertyFlag("sub-visibility", true);
            }
            else
            {
                _player.SetPropertyFlag("sub-visibility", true);
                _player.SetPropertyString("sid", tag);
            }
        }
    }

    private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        var ms = (int)e.NewValue;
        DelayText.Text = $"{ms} ms";
        _player?.SetSubtitleDelay(ms / 1000.0);
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        var size = (int)e.NewValue;
        FontSizeText.Text = $"{size} px";
        _player?.SetSubtitleFontSize(size);
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (FontCombo.SelectedItem is ComboBoxItem item && item.Tag is string font)
        {
            _player?.SetPropertyString("sub-font", font);
        }
    }

    private async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        await PerformOnlineSearchAsync();
    }

    private async void OnlineSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformOnlineSearchAsync();
        }
    }

    private async Task PerformOnlineSearchAsync()
    {
        var query = OnlineSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        SearchStatusText.Text = "Searching online via Podnapisi...";
        OnlineResultsList.ItemsSource = null;

        try
        {
            var results = await _subtitleService.Provider.SearchByQueryAsync(query, null, ct);
            if (results.Count == 0)
            {
                SearchStatusText.Text = "No subtitles found for this title.";
            }
            else
            {
                SearchStatusText.Text = $"Found {results.Count} result(s). Click Load to apply.";
                OnlineResultsList.ItemsSource = results;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch
        {
            SearchStatusText.Text = "Network error: Could not reach subtitle provider.";
        }
    }

    private async void DownloadSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SubtitleSearchResult result)
        {
            SearchStatusText.Text = $"Downloading {result.DisplayTitle}...";
            var targetDir = Path.Combine(Path.GetTempPath(), "NJPlayer", "Subtitles");

            try
            {
                var filePath = await _subtitleService.Provider.DownloadSubtitleAsync(result, targetDir);
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    LoadSubtitleFile(filePath);
                    SearchStatusText.Text = $"Loaded: {Path.GetFileName(filePath)}";
                }
                else
                {
                    SearchStatusText.Text = "Failed to extract downloaded subtitle.";
                }
            }
            catch
            {
                SearchStatusText.Text = "Download error: Check internet connection.";
            }
        }
    }

    public void ForceClose()
    {
        _isForceClosing = true;
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

