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
    private WatchLaterManager? _watchLaterManager;
    private readonly LocalMediaBrowserService _browserService = new();
    private string _currentBrowsePath = string.Empty;
    private bool _isOpen = false;
    private int _activeTabIndex = 0; // 0: Playlist, 1: Browse PC, 2: Watch Later
    private bool _isWatchLaterTab = false;
    private ICollectionView? _filteredView;
    private Point _dragStartPoint;
    private PlaylistItem? _draggedItem;
    private readonly PlaylistSearchOptions _searchOptions = new();

    private readonly DoubleAnimation _openAnimation;
    private readonly DoubleAnimation _closeAnimation;

    public bool IsOpen => _isOpen;
    public event EventHandler<PlaylistItem>? ItemSelected;
    public event Action<string>? MediaPathRequested;

    public PlaylistDrawer()
    {
        InitializeComponent();
        InitSearchOptions();

        // 180ms base easing animation per DESIGN.md section 5
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        _openAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing };
        _closeAnimation = new DoubleAnimation(340.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing };

        InitQuickPlaces();
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

    private NJPlayer.Core.Playback.PlayerMode _currentMode = NJPlayer.Core.Playback.PlayerMode.Video;

    public void SetMode(NJPlayer.Core.Playback.PlayerMode mode)
    {
        _currentMode = mode;
        if (mode == NJPlayer.Core.Playback.PlayerMode.Music)
        {
            PlaylistTabBtn.Content = "🎵 Music Queue";
            SearchPlaceholder.Text = "🔍 Search music queue...";
            WatchLaterTabBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            PlaylistTabBtn.Content = "📋 Playlist";
            SearchPlaceholder.Text = "🔍 Search playlist...";
            WatchLaterTabBtn.Visibility = Visibility.Visible;
        }
    }

    public void AttachWatchLater(WatchLaterManager watchLaterManager)
    {
        _watchLaterManager = watchLaterManager ?? throw new ArgumentNullException(nameof(watchLaterManager));
        WatchLaterListBox.ItemsSource = _watchLaterManager.Items;
        _watchLaterManager.QueueChanged += (_, _) => UpdateWatchLaterCount();
        UpdateWatchLaterCount();
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

    // --- Tab Switching & PC Browser ---

    private void InitQuickPlaces()
    {
        QuickPlacesCombo.Items.Clear();
        foreach (var place in _browserService.GetQuickPlaces())
        {
            QuickPlacesCombo.Items.Add(new ComboBoxItem { Content = $"📁 {place.Name}", Tag = place.Path });
        }
        foreach (var drive in _browserService.GetDrives())
        {
            QuickPlacesCombo.Items.Add(new ComboBoxItem { Content = $"💾 {drive.Name}", Tag = drive.Path });
        }
    }

    private void SetActiveTab(int tabIndex)
    {
        _activeTabIndex = tabIndex;
        _isWatchLaterTab = (tabIndex == 2);

        var activeBrush = (Brush)FindResource("SurfaceRaisedBrush");
        var accentBrush = (Brush)FindResource("AccentBrush");
        var mutedBrush = (Brush)FindResource("TextMutedBrush");

        PlaylistTabBtn.Background = tabIndex == 0 ? activeBrush : Brushes.Transparent;
        PlaylistTabBtn.Foreground = tabIndex == 0 ? accentBrush : mutedBrush;
        PlaylistTabBtn.BorderBrush = tabIndex == 0 ? accentBrush : Brushes.Transparent;

        BrowsePcTabBtn.Background = tabIndex == 1 ? activeBrush : Brushes.Transparent;
        BrowsePcTabBtn.Foreground = tabIndex == 1 ? accentBrush : mutedBrush;
        BrowsePcTabBtn.BorderBrush = tabIndex == 1 ? accentBrush : Brushes.Transparent;

        WatchLaterTabBtn.Background = tabIndex == 2 ? activeBrush : Brushes.Transparent;
        WatchLaterTabBtn.Foreground = tabIndex == 2 ? accentBrush : mutedBrush;
        WatchLaterTabBtn.BorderBrush = tabIndex == 2 ? accentBrush : Brushes.Transparent;

        PlaylistHeaderPanel.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PcBrowserHeaderBar.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        WatchLaterActionBar.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        PlaylistListBox.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PcBrowserListBox.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        WatchLaterListBox.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        PlaylistFooter.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PcBrowserFooter.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        WatchLaterFooter.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (tabIndex == 1 && string.IsNullOrEmpty(_currentBrowsePath))
        {
            var preferredFolder = _currentMode == NJPlayer.Core.Playback.PlayerMode.Music
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            if (string.IsNullOrEmpty(preferredFolder) || !Directory.Exists(preferredFolder))
            {
                preferredFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            _ = NavigateToFolderAsync(preferredFolder);
        }
        else if (tabIndex == 2)
        {
            UpdateWatchLaterCount();
        }
    }

    private void PlaylistTab_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(0);
    }

    private void BrowsePcTab_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(1);
    }

    private void WatchLaterTab_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(2);
    }

    public async Task NavigateToFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        try
        {
            _currentBrowsePath = folderPath;
            CurrentPathTextBox.Text = folderPath;

            var items = await _browserService.EnumerateDirectoryAsync(folderPath);
            PcBrowserListBox.ItemsSource = items;

            var mediaCount = items.Count(i => !i.IsDirectory);
            var folderCount = items.Count(i => i.IsDirectory);
            PcBrowserCountText.Text = $"{folderCount} folders, {mediaCount} media files";
        }
        catch (Exception ex)
        {
            PcBrowserCountText.Text = $"Error: {ex.Message}";
        }
    }

    private async void PcBrowserListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PcBrowserListBox.SelectedItem is LocalFileSystemItem item)
        {
            if (item.IsDirectory)
            {
                await NavigateToFolderAsync(item.FullPath);
            }
            else
            {
                // Play media directly
                MediaPathRequested?.Invoke(item.FullPath);
            }
        }
    }

    private void MenuPcBrowserPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PcBrowserListBox.SelectedItem is LocalFileSystemItem item && !item.IsDirectory)
        {
            MediaPathRequested?.Invoke(item.FullPath);
        }
    }

    private void MenuPcBrowserAdd_Click(object sender, RoutedEventArgs e)
    {
        if (PcBrowserListBox.SelectedItem is LocalFileSystemItem item && _manager != null)
        {
            if (item.IsDirectory)
            {
                var added = _manager.AddFolder(item.FullPath, recursive: true);
                MessageBox.Show($"Added {added} media files to playlist.", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _manager.AddFile(item.FullPath);
            }
        }
    }

    private void MenuPcBrowserWatchLater_Click(object sender, RoutedEventArgs e)
    {
        if (PcBrowserListBox.SelectedItem is LocalFileSystemItem item && _watchLaterManager != null)
        {
            if (item.IsDirectory)
            {
                _watchLaterManager.AddFolder(item.FullPath, recursive: true);
            }
            else
            {
                _watchLaterManager.Add(item.FullPath);
            }
        }
    }

    private void MenuPcBrowserShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PcBrowserListBox.SelectedItem is LocalFileSystemItem item)
        {
            try
            {
                Process.Start("explorer.exe", item.IsDirectory ? $"\"{item.FullPath}\"" : $"/select,\"{item.FullPath}\"");
            }
            catch { }
        }
    }

    private async void BrowseUp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentBrowsePath)) return;
        var parent = Directory.GetParent(_currentBrowsePath);
        if (parent != null)
        {
            await NavigateToFolderAsync(parent.FullName);
        }
    }

    private async void BrowseRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentBrowsePath))
        {
            await NavigateToFolderAsync(_currentBrowsePath);
        }
    }

    private async void CurrentPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = CurrentPathTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                await NavigateToFolderAsync(path);
            }
        }
    }

    private async void QuickPlacesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuickPlacesCombo.SelectedItem is ComboBoxItem item && item.Tag is string path && Directory.Exists(path))
        {
            await NavigateToFolderAsync(path);
        }
    }

    private void AddCurrentFolderToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentBrowsePath) || !Directory.Exists(_currentBrowsePath) || _manager == null) return;

        var added = _manager.AddFolder(_currentBrowsePath, recursive: false);
        MessageBox.Show($"Added {added} media files to the playlist.", "PC Browser - NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // --- Playlist Quick Actions (+ Add Files, + Add Folder, Clear) ---

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Media Files to Add - NJ Player",
            Filter = "Media Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.ts;*.m4v;*.mp3;*.flac;*.wav;*.m4a;*.opus;*.ogg;*.aac;*.wma;*.alac;*.aiff;*.ape|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (ofd.ShowDialog() == true && _manager != null)
        {
            int added = 0;
            foreach (var file in ofd.FileNames)
            {
                _manager.AddFile(file);
                added++;
            }
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Media Folder to Add - NJ Player"
        };

        if (dialog.ShowDialog() == true && _manager != null)
        {
            _manager.AddFolder(dialog.FolderName, recursive: true);
        }
    }

    private void ClearPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null || _manager.Items.Count == 0) return;

        var res = MessageBox.Show(
            "Are you sure you want to clear the playlist?",
            "Clear Playlist - NJ Player",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            _manager.Clear();
        }
    }

    private void UpdateWatchLaterCount()
    {
        var count = _watchLaterManager?.Items.Count ?? 0;
        WatchLaterCountText.Text = count == 1 ? "1 video queued" : $"{count} videos queued";
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

    private void InitSearchOptions()
    {
        try
        {
            var settings = NJPlayer.Core.Settings.SettingsManager.Instance.Settings;
            _searchOptions.Scope = settings.PlaylistSearchScope;
            _searchOptions.Mode = settings.PlaylistSearchMode;
            _searchOptions.CaseSensitive = settings.PlaylistSearchCaseSensitive;
            UpdateSearchPlaceholderAndBadge();
        }
        catch { }
    }

    private void UpdateSearchPlaceholderAndBadge()
    {
        var scopeDesc = _searchOptions.Scope switch
        {
            PlaylistSearchScope.TitleOnly => "Title",
            PlaylistSearchScope.TitleAndPath => "Path",
            PlaylistSearchScope.DirectoryOnly => "Folder",
            _ => "Title"
        };

        var modeDesc = _searchOptions.Mode switch
        {
            PlaylistSearchMode.Contains => "Substring",
            PlaylistSearchMode.ExactMatch => "Exact",
            PlaylistSearchMode.StartsWith => "Starts With",
            PlaylistSearchMode.Regex => "Regex",
            _ => "Substring"
        };

        if (SearchMethodBadgeText != null)
        {
            SearchMethodBadgeText.Text = _searchOptions.Mode == PlaylistSearchMode.Regex ? ".*" :
                                         _searchOptions.Mode == PlaylistSearchMode.ExactMatch ? "==" :
                                         _searchOptions.CaseSensitive ? "Aa" : "aA";
        }

        if (SearchPlaceholder != null)
        {
            SearchPlaceholder.Text = $"🔍 Search playlist ({scopeDesc} • {modeDesc})...";
        }
    }

    private void SetSearchScope(PlaylistSearchScope scope)
    {
        _searchOptions.Scope = scope;
        SaveSearchSettings();
        UpdateSearchPlaceholderAndBadge();
        _filteredView?.Refresh();
    }

    private void SetSearchMode(PlaylistSearchMode mode)
    {
        _searchOptions.Mode = mode;
        SaveSearchSettings();
        UpdateSearchPlaceholderAndBadge();
        _filteredView?.Refresh();
    }

    private void ToggleCaseSensitivity()
    {
        _searchOptions.CaseSensitive = !_searchOptions.CaseSensitive;
        SaveSearchSettings();
        UpdateSearchPlaceholderAndBadge();
        _filteredView?.Refresh();
    }

    private void SaveSearchSettings()
    {
        try
        {
            var mgr = NJPlayer.Core.Settings.SettingsManager.Instance;
            mgr.Settings.PlaylistSearchScope = _searchOptions.Scope;
            mgr.Settings.PlaylistSearchMode = _searchOptions.Mode;
            mgr.Settings.PlaylistSearchCaseSensitive = _searchOptions.CaseSensitive;
            mgr.Save();
        }
        catch { }
    }

    private void SearchMethodBtn_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = SearchMethodBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        // Header 1: Scope
        var scopeHeader = new MenuItem { Header = "SEARCH TARGET / SCOPE", IsEnabled = false, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush") };
        menu.Items.Add(scopeHeader);

        var miTitleOnly = new MenuItem { Header = "Title Only (File Name)", IsChecked = _searchOptions.Scope == PlaylistSearchScope.TitleOnly };
        miTitleOnly.Click += (_, _) => SetSearchScope(PlaylistSearchScope.TitleOnly);
        menu.Items.Add(miTitleOnly);

        var miTitleAndPath = new MenuItem { Header = "Title & Full File Path", IsChecked = _searchOptions.Scope == PlaylistSearchScope.TitleAndPath };
        miTitleAndPath.Click += (_, _) => SetSearchScope(PlaylistSearchScope.TitleAndPath);
        menu.Items.Add(miTitleAndPath);

        var miDirOnly = new MenuItem { Header = "Directory / Folder Name Only", IsChecked = _searchOptions.Scope == PlaylistSearchScope.DirectoryOnly };
        miDirOnly.Click += (_, _) => SetSearchScope(PlaylistSearchScope.DirectoryOnly);
        menu.Items.Add(miDirOnly);

        menu.Items.Add(new Separator());

        // Header 2: Mode
        var modeHeader = new MenuItem { Header = "MATCHING ALGORITHM", IsEnabled = false, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush") };
        menu.Items.Add(modeHeader);

        var miContains = new MenuItem { Header = "Contains Substring (Default)", IsChecked = _searchOptions.Mode == PlaylistSearchMode.Contains };
        miContains.Click += (_, _) => SetSearchMode(PlaylistSearchMode.Contains);
        menu.Items.Add(miContains);

        var miExact = new MenuItem { Header = "Exact Full Match", IsChecked = _searchOptions.Mode == PlaylistSearchMode.ExactMatch };
        miExact.Click += (_, _) => SetSearchMode(PlaylistSearchMode.ExactMatch);
        menu.Items.Add(miExact);

        var miStartsWith = new MenuItem { Header = "Starts With", IsChecked = _searchOptions.Mode == PlaylistSearchMode.StartsWith };
        miStartsWith.Click += (_, _) => SetSearchMode(PlaylistSearchMode.StartsWith);
        menu.Items.Add(miStartsWith);

        var miRegex = new MenuItem { Header = "Regular Expression (Regex)", IsChecked = _searchOptions.Mode == PlaylistSearchMode.Regex };
        miRegex.Click += (_, _) => SetSearchMode(PlaylistSearchMode.Regex);
        menu.Items.Add(miRegex);

        menu.Items.Add(new Separator());

        // Case Sensitive
        var miCase = new MenuItem { Header = "Match Case (Case Sensitive)", IsCheckable = true, IsChecked = _searchOptions.CaseSensitive };
        miCase.Click += (_, _) => ToggleCaseSensitivity();
        menu.Items.Add(miCase);

        menu.IsOpen = true;
    }

    private bool FilterItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
        if (obj is PlaylistItem item)
        {
            return _searchOptions.Matches(item, SearchBox.Text);
        }
        return false;
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

    // --- Watch Later Actions ---

    private void AddWatchLater_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Videos to Add to Watch Later - NJ Player",
            Filter = "Media Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.ts;*.m4v;*.mp3;*.flac;*.wav;*.m4a;*.opus;*.ogg|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && _watchLaterManager != null)
        {
            int added = 0;
            foreach (var file in dialog.FileNames)
            {
                if (_watchLaterManager.Add(file)) added++;
            }
        }
    }

    private void ClearWatchLater_Click(object sender, RoutedEventArgs e)
    {
        if (_watchLaterManager != null && _watchLaterManager.Items.Count > 0)
        {
            var res = MessageBox.Show("Clear all videos from Watch Later queue?", "Watch Later - NJ Player", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _watchLaterManager.Clear();
            }
        }
    }

    private void PlayWatchLaterQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_watchLaterManager == null || _watchLaterManager.Items.Count == 0) return;

        // Take the first queue item to play
        var first = _watchLaterManager.Items[0];
        MediaPathRequested?.Invoke(first.FilePath);
    }

    private void WatchLaterListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WatchLaterListBox.SelectedItem is WatchLaterItem item)
        {
            MediaPathRequested?.Invoke(item.FilePath);
        }
    }

    private void MenuWatchLaterPlay_Click(object sender, RoutedEventArgs e)
    {
        if (WatchLaterListBox.SelectedItem is WatchLaterItem item)
        {
            MediaPathRequested?.Invoke(item.FilePath);
        }
    }

    private void MenuWatchLaterRemove_Click(object sender, RoutedEventArgs e)
    {
        if (WatchLaterListBox.SelectedItem is WatchLaterItem item && _watchLaterManager != null)
        {
            _watchLaterManager.Remove(item);
        }
    }

    private void MenuWatchLaterShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (WatchLaterListBox.SelectedItem is WatchLaterItem item && File.Exists(item.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch { }
        }
    }

    private void MenuAddToWatchLater_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is PlaylistItem item && _watchLaterManager != null)
        {
            _watchLaterManager.Add(item.FilePath, item.Title, item.Duration.TotalSeconds);
            MessageBox.Show($"Added '{item.Title}' to Watch Later.", "Watch Later - NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // --- Drag and Drop ---

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
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var entries = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (entries != null)
            {
                if (_isWatchLaterTab && _watchLaterManager != null)
                {
                    foreach (var entry in entries)
                    {
                        if (Directory.Exists(entry))
                        {
                            _watchLaterManager.AddFolder(entry, recursive: true);
                        }
                        else if (File.Exists(entry))
                        {
                            _watchLaterManager.Add(entry);
                        }
                    }
                }
                else if (_manager != null)
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
            RepeatMode.RepeatOne => RepeatMode.None,
            _ => RepeatMode.None
        };
    }

    private void M3uMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;

        var cm = new ContextMenu
        {
            Background = (Brush)FindResource("SurfaceRaisedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1)
        };

        var saveItem = new MenuItem { Header = "💾 Save Playlist as M3U..." };
        saveItem.Click += (_, _) =>
        {
            var sfd = new SaveFileDialog
            {
                Title = "Save Playlist - NJ Player",
                Filter = "M3U8 Playlist (*.m3u8)|*.m3u8|M3U Playlist (*.m3u)|*.m3u",
                DefaultExt = ".m3u8",
                FileName = "playlist.m3u8"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _manager.SaveM3u(sfd.FileName);
                    MessageBox.Show("Playlist exported successfully.", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save playlist: {ex.Message}", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        };

        var loadItem = new MenuItem { Header = "📂 Load M3U Playlist..." };
        loadItem.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog
            {
                Title = "Open M3U Playlist - NJ Player",
                Filter = "M3U Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                var added = _manager.LoadM3u(ofd.FileName);
                MessageBox.Show($"Loaded {added} tracks from playlist.", "NJ Player", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };

        cm.Items.Add(saveItem);
        cm.Items.Add(loadItem);
        cm.IsOpen = true;
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
