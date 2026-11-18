using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NJPlayer.Core.Settings;

namespace NJPlayer.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;

    public SettingsWindow(SettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsManager.Settings;

        ResumeCheckBox.IsChecked = s.ResumePlayback;
        RememberBoundsCheckBox.IsChecked = s.RememberWindowPosition;
        Mpeg4DxvaCheckBox.IsChecked = s.EnableMpeg4Dxva;

        // Playlist Search configuration
        SearchCaseSensitiveCheckBox.IsChecked = s.PlaylistSearchCaseSensitive;

        foreach (ComboBoxItem item in SearchScopeCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.PlaylistSearchScope.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                SearchScopeCombo.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in SearchModeCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.PlaylistSearchMode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                SearchModeCombo.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in HwdecCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.HardwareDecoder, StringComparison.OrdinalIgnoreCase))
            {
                HwdecCombo.SelectedItem = item;
                break;
            }
        }

        if (_settingsManager.IsPortable)
        {
            ModeText.Text = "Portable Mode (./data/)";
        }
        else
        {
            ModeText.Text = "Standard Mode (%APPDATA%)";
        }

        StoragePathText.Text = _settingsManager.StorageDirectory;
        UpdateStats();
        UpdateContextMenuState();
    }

    private void UpdateContextMenuState()
    {
        var isReg = ContextMenuHelper.IsRegistered();
        ToggleContextMenuBtn.Content = isReg ? "Uninstall" : "Install";
        ContextMenuStatusText.Text = isReg
            ? "Registered in Windows Explorer context menu."
            : "Not currently registered in Explorer context menu.";
    }

    private void ToggleContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ContextMenuHelper.IsRegistered())
        {
            if (ContextMenuHelper.Unregister())
            {
                MessageBox.Show(this, "Explorer context menu entry removed.", "Context Menu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else
        {
            if (ContextMenuHelper.Register())
            {
                MessageBox.Show(this, "Explorer context menu entry registered!", "Context Menu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "Failed to register context menu.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        UpdateContextMenuState();
    }

    private async void SettingsUpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        SettingsUpdateYtDlpBtn.IsEnabled = false;
        SettingsUpdateYtDlpBtn.Content = "Updating...";

        try
        {
            var downloader = new NJPlayer.Features.Downloader.MediaDownloaderService();
            var output = await downloader.UpdateYtDlpAsync();
            MessageBox.Show(this, output, "yt-dlp Update", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"yt-dlp update error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SettingsUpdateYtDlpBtn.IsEnabled = true;
            SettingsUpdateYtDlpBtn.Content = "🔄 Update yt-dlp";
        }
    }

    private void UpdateStats()
    {
        var s = _settingsManager.Settings;
        CacheStatsText.Text = $"{s.RecentFiles.Count} recent file(s), {s.ResumePositions.Count} saved position(s)";
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        _settingsManager.ClearRecentFiles();
        UpdateStats();
    }

    private void ClearResume_Click(object sender, RoutedEventArgs e)
    {
        _settingsManager.ClearResumePositions();
        UpdateStats();
    }

    private void ViewSnippet_Click(object sender, RoutedEventArgs e)
    {
        var snippet = FileAssociationHelper.GenerateInnoSetupScriptSnippet();
        MessageBox.Show(snippet, "Inno Setup File Association Script", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveApply_Click(object sender, RoutedEventArgs e)
    {
        var s = _settingsManager.Settings;
        s.ResumePlayback = ResumeCheckBox.IsChecked == true;
        s.RememberWindowPosition = RememberBoundsCheckBox.IsChecked == true;
        s.EnableMpeg4Dxva = Mpeg4DxvaCheckBox.IsChecked == true;
        s.HwdecCodecs = s.EnableMpeg4Dxva ? "all" : "h264,vc1,wmv3,hevc";

        if (HwdecCombo.SelectedItem is ComboBoxItem selected)
        {
            s.HardwareDecoder = selected.Tag?.ToString() ?? "auto-safe";
        }

        // Playlist Search configuration
        s.PlaylistSearchCaseSensitive = SearchCaseSensitiveCheckBox.IsChecked == true;
        if (SearchScopeCombo.SelectedItem is ComboBoxItem selectedScope &&
            Enum.TryParse<NJPlayer.Core.Playlist.PlaylistSearchScope>(selectedScope.Tag?.ToString(), out var parsedScope))
        {
            s.PlaylistSearchScope = parsedScope;
        }

        if (SearchModeCombo.SelectedItem is ComboBoxItem selectedMode &&
            Enum.TryParse<NJPlayer.Core.Playlist.PlaylistSearchMode>(selectedMode.Tag?.ToString(), out var parsedMode))
        {
            s.PlaylistSearchMode = parsedMode;
        }

        _settingsManager.Save();
        DialogResult = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

