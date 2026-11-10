using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using NJPlayer.Core.Playlist;

namespace NJPlayer.App.Views;

public partial class PlaylistDrawer : UserControl
{
    private PlaylistManager? _manager;
    private bool _isOpen = false;
    private ICollectionView? _filteredView;
    private Point _dragStartPoint;
    private PlaylistItem? _draggedItem;

    private readonly DoubleAnimation _openAnimation;
    private readonly DoubleAnimation _closeAnimation;

    public bool IsOpen => _isOpen;
    public event EventHandler<PlaylistItem>? ItemSelected;

    public PlaylistDrawer()
    {
        InitializeComponent();

        // 180ms base easing animation per DESIGN.md section 5
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        _openAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing };
        _closeAnimation = new DoubleAnimation(340.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing };
    }

    public void AttachManager(PlaylistManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _filteredView = CollectionViewSource.GetDefaultView(_manager.Items);
        _filteredView.Filter = FilterItem;
        PlaylistListBox.ItemsSource = _filteredView;

        _manager.PlaylistStateChanged += (_, _) => UpdateStateVisuals();
        _manager.CurrentItemChanged += (_, _) => UpdateActiveItemVisuals();

        UpdateStateVisuals();
        UpdateActiveItemVisuals();
    }

    public void ToggleDrawer()
    {
        if (_isOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    public void OpenDrawer()
    {
        _isOpen = true;
        Visibility = Visibility.Visible;
        DrawerTransform.BeginAnimation(TranslateTransform.XProperty, _openAnimation);
        SearchBox.Focus();
    }

    public void CloseDrawer()
    {
        _isOpen = false;
        var anim = _closeAnimation.Clone();
        anim.Completed += (_, _) =>
        {
            if (!_isOpen) Visibility = Visibility.Collapsed;
        };
        DrawerTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void UpdateStateVisuals()
    {
        if (_manager == null) return;

        ShuffleButton.Foreground = _manager.IsShuffled
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextMutedBrush");

        RepeatButton.Foreground = _manager.RepeatMode != RepeatMode.None
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextMutedBrush");

        RepeatIcon.Text = _manager.RepeatMode switch
        {
            RepeatMode.RepeatOne => "🔂",
            RepeatMode.RepeatAll => "🔁",
            _ => "🔁"
        };
    }

    private void UpdateActiveItemVisuals()
    {
        if (_manager == null) return;

        for (var i = 0; i < PlaylistListBox.Items.Count; i++)
        {
            if (PlaylistListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
            {
                var item = PlaylistListBox.Items[i] as PlaylistItem;
                container.Tag = (item != null && item == _manager.CurrentItem) ? "Active" : string.Empty;
            }
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not PlaylistItem item) return false;
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _filteredView?.Refresh();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawer();
    }

    private void PlaylistListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item && _manager != null)
        {
            var actualIndex = _manager.Items.IndexOf(item);
            if (actualIndex >= 0)
            {
                _manager.SetCurrentIndex(actualIndex);
                ItemSelected?.Invoke(this, item);
            }
        }
    }

    // --- Drag and Drop (External Files / Folders & Reordering) ---

    private void Drawer_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Drawer_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && _manager != null)
        {
            var entries = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (Directory.Exists(entry))
                    {
                        _manager.AddFolder(entry, recursive: true);
                    }
                    else if (NJPlayer.Core.Playlist.PlaylistManager.IsPlaylistFile(entry))
                    {
                        _manager.LoadM3u(entry);
                    }
                    else if (File.Exists(entry))
                    {
                        _manager.AddFile(entry);
                    }
                }
            }
        }
    }

    private void PlaylistListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItem = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>()?.DataContext as PlaylistItem;
    }

    private void PlaylistListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
        {
            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(PlaylistListBox, _draggedItem, DragDropEffects.Move);
                _draggedItem = null;
            }
        }
    }

    private void PlaylistListBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PlaylistItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void PlaylistListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(PlaylistItem)) && _manager != null)
        {
            var droppedData = e.Data.GetData(typeof(PlaylistItem)) as PlaylistItem;
            var targetContainer = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>();
            var targetItem = targetContainer?.DataContext as PlaylistItem;

            if (droppedData != null && targetItem != null && droppedData != targetItem)
            {
                var fromIndex = _manager.Items.IndexOf(droppedData);
                var toIndex = _manager.Items.IndexOf(targetItem);
                if (fromIndex >= 0 && toIndex >= 0)
                {
                    _manager.Move(fromIndex, toIndex);
                }
            }
        }
    }

    // --- Footer Controls (Shuffle, Repeat, M3U) ---

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager != null) _manager.IsShuffled = !_manager.IsShuffled;
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;
        _manager.RepeatMode = _manager.RepeatMode switch
        {
            RepeatMode.None => RepeatMode.RepeatAll,
            RepeatMode.RepeatAll => RepeatMode.RepeatOne,
            _ => RepeatMode.None
        };
    }

    private void M3uMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;

        var menu = new ContextMenu();
        var importItem = new MenuItem { Header = "📂 Load M3U / M3U8 Playlist..." };
        importItem.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "M3U/M3U8 Playlists|*.m3u;*.m3u8|All Files (*.*)|*.*" };
            if (dlg.ShowDialog() == true) _manager.LoadM3u(dlg.FileName);
        };

        var exportItem = new MenuItem { Header = "💾 Save M3U / M3U8 Playlist..." };
        exportItem.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "M3U8 Playlist (*.m3u8)|*.m3u8|M3U Playlist (*.m3u)|*.m3u",
                DefaultExt = ".m3u8"
            };
            if (dlg.ShowDialog() == true) _manager.SaveM3u(dlg.FileName);
        };

        menu.Items.Add(importItem);
        menu.Items.Add(exportItem);
        menu.IsOpen = true;
    }

    // --- Context Menu Actions ---

    private void MenuPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item && _manager != null)
        {
            var idx = _manager.Items.IndexOf(item);
            if (idx >= 0)
            {
                _manager.SetCurrentIndex(idx);
                ItemSelected?.Invoke(this, item);
            }
        }
    }

    private void MenuRemove_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item && _manager != null)
        {
            _manager.Remove(item);
        }
    }

    private void MenuShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item && File.Exists(item.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not show file in folder: {ex.Message}", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item)
        {
            MessageBox.Show(
                $"Title: {item.Title}\nDuration: {item.FormattedDuration}\nLocation: {item.FilePath}",
                "Media Properties - NJ Player",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestor<T>(this DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
