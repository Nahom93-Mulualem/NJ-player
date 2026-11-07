using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NJPlayer.Core.Hotkeys;

namespace NJPlayer.App.Views;

public partial class HotkeySettingsWindow : Window
{
    private readonly HotkeyManager _hotkeyManager;
    private readonly ObservableCollection<HotkeyBinding> _bindingsList;

    public HotkeySettingsWindow(HotkeyManager hotkeyManager)
    {
        InitializeComponent();
        _hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
        _bindingsList = new ObservableCollection<HotkeyBinding>(_hotkeyManager.Bindings.Values.OrderBy(b => b.Category).ThenBy(b => b.Name));
        HotkeysGrid.ItemsSource = _bindingsList;

        CheckConflicts();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var selected = HotkeysGrid.SelectedItem as HotkeyBinding;
        if (selected == null) return;

        // Skip standalone modifier keys
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        // Construct key gesture string
        var modifiers = new System.Collections.Generic.List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) modifiers.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) modifiers.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) modifiers.Add("Alt");

        modifiers.Add(key.ToString());
        var gesture = string.Join("+", modifiers);

        selected.Gesture = HotkeyManager.NormalizeGesture(gesture);
        _hotkeyManager.Rebind(selected.Command, selected.Gesture);

        // Refresh UI row
        HotkeysGrid.Items.Refresh();
        CheckConflicts();
        e.Handled = true;
    }

    private void CheckConflicts()
    {
        var conflicts = _hotkeyManager.DetectConflicts();
        if (conflicts.Count > 0)
        {
            var conflictDesc = string.Join(", ", conflicts.Select(c => $"'{c.Gesture}' assigned to {c.Commands.Count} actions ({string.Join(" & ", c.Commands)})"));
            ConflictText.Text = $"⚠️ Conflict Detected: {conflictDesc}";
            ConflictBanner.Visibility = Visibility.Visible;
        }
        else
        {
            ConflictBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void HotkeysGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = HotkeysGrid.SelectedItem as HotkeyBinding;
        if (selected != null)
        {
            RecordingPrompt.Text = $"Press key combination for '{selected.Name}'...";
            RecordingPrompt.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }
        else
        {
            RecordingPrompt.Text = "Select a row, then press any key combination to rebind...";
            RecordingPrompt.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyManager.ResetToDefaults();
        _bindingsList.Clear();
        foreach (var b in _hotkeyManager.Bindings.Values.OrderBy(b => b.Category).ThenBy(b => b.Name))
        {
            _bindingsList.Add(b);
        }
        CheckConflicts();
    }

    private void SaveApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _hotkeyManager.SaveToFile();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save hotkeys.json: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

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
