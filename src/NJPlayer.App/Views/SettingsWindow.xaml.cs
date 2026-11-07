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

        if (HwdecCombo.SelectedItem is ComboBoxItem selected)
        {
            s.HardwareDecoder = selected.Tag?.ToString() ?? "auto-safe";
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
