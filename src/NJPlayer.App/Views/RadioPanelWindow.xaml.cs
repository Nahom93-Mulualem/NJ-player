using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NJPlayer.Core.Settings;
using NJPlayer.Features.Radio;

namespace NJPlayer.App.Views;

public partial class RadioPanelWindow : Window
{
    private readonly RadioBrowserService _radioService;
    private readonly AppSettings _settings;
    private readonly List<RadioStation> _allLoadedStations = new();
    private CancellationTokenSource? _searchCts;
    private bool _showFavoritesOnly;

    public event Action<RadioStation, string>? StationSelected;
    public event Action<string>? VisualizerModeChanged;

    public string SelectedVisualizerMode
    {
        get
        {
            if (VizCqtRadio.IsChecked == true) return "showcqt";
            if (VizWavesRadio.IsChecked == true) return "showwaves";
            return "off";
        }
    }

    public RadioPanelWindow(RadioBrowserService radioService, AppSettings settings)
    {
        InitializeComponent();
        _radioService = radioService;
        _settings = settings;

        // Restore visualizer mode from settings
        var mode = _settings.VisualizerMode?.ToLowerInvariant() ?? "showcqt";
        switch (mode)
        {
            case "showwaves":
                VizWavesRadio.IsChecked = true;
                break;
            case "off":
                VizOffRadio.IsChecked = true;
                break;
            default:
                VizCqtRadio.IsChecked = true;
                break;
        }

        Loaded += RadioPanelWindow_Loaded;
    }

    private async void RadioPanelWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCountriesAsync();
        await TriggerSearchAsync();
    }

    private async Task LoadCountriesAsync()
    {
        try
        {
            var countries = await _radioService.GetCountriesAsync().ConfigureAwait(true);

            CountryComboBox.Items.Clear();

            // Pinned Ethiopia at index 0 per requirement
            var ethiopia = countries.FirstOrDefault(c =>
                string.Equals(c.IsoCode, "ET", StringComparison.OrdinalIgnoreCase) ||
                c.Name.StartsWith("Ethiopia", StringComparison.OrdinalIgnoreCase));

            CountryComboBox.Items.Add(new ComboBoxItem
            {
                Content = "🇪🇹 Ethiopia (ET)",
                Tag = "ET"
            });

            // Option for all countries
            CountryComboBox.Items.Add(new ComboBoxItem
            {
                Content = "🌐 All Countries",
                Tag = "ALL"
            });

            foreach (var country in countries)
            {
                if (string.Equals(country.IsoCode, "ET", StringComparison.OrdinalIgnoreCase)) continue;
                CountryComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{country.Name} ({country.IsoCode})",
                    Tag = country.IsoCode
                });
            }

            CountryComboBox.SelectedIndex = 0; // Default to Ethiopia
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Radio] Country load error: {ex.Message}");
            CountryComboBox.Items.Add(new ComboBoxItem { Content = "🇪🇹 Ethiopia (ET)", Tag = "ET" });
            CountryComboBox.Items.Add(new ComboBoxItem { Content = "🌐 All Countries", Tag = "ALL" });
            CountryComboBox.SelectedIndex = 0;
        }
    }

    private async Task TriggerSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var query = SearchBox.Text.Trim();
        var selectedItem = CountryComboBox.SelectedItem as ComboBoxItem;
        var countryCode = selectedItem?.Tag?.ToString() ?? "ET";

        StatusMessageText.Text = "Searching radio stations...";
        StatusMessageText.Visibility = Visibility.Visible;
        StationListBox.ItemsSource = null;

        try
        {
            var stations = await _radioService.SearchStationsAsync(query, countryCode, limit: 100, ct).ConfigureAwait(true);
            _allLoadedStations.Clear();
            _allLoadedStations.AddRange(stations);

            ApplyFilterAndDisplay();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessageText.Text = $"Failed to load stations: {ex.Message}";
            StatusMessageText.Visibility = Visibility.Visible;
            StationCountText.Text = "0 stations";
        }
    }

    private void ApplyFilterAndDisplay()
    {
        IEnumerable<RadioStation> result = _allLoadedStations;

        if (_showFavoritesOnly)
        {
            result = result.Where(s => _radioService.IsFavorite(s.StationUuid));
        }

        var list = result.ToList();
        StationListBox.ItemsSource = list;
        StationCountText.Text = $"{list.Count} stations";

        if (list.Count == 0)
        {
            StatusMessageText.Text = _showFavoritesOnly ? "No favorite stations saved yet." : "No stations found matching criteria.";
            StatusMessageText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusMessageText.Visibility = Visibility.Collapsed;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await TriggerSearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TriggerSearchAsync();
        }
    }

    private async void CountryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await TriggerSearchAsync();
        }
    }

    private void FavoritesFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _showFavoritesOnly = !_showFavoritesOnly;
        FavoritesFilterIcon.Foreground = _showFavoritesOnly
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("TextMutedBrush");

        ApplyFilterAndDisplay();
    }

    private void PlayStation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RadioStation station)
        {
            PlaySelectedStation(station);
        }
    }

    private void StationListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StationListBox.SelectedItem is RadioStation station)
        {
            PlaySelectedStation(station);
        }
    }

    private void PlaySelectedStation(RadioStation station)
    {
        if (station == null || string.IsNullOrWhiteSpace(station.StreamUrl)) return;
        StationSelected?.Invoke(station, SelectedVisualizerMode);
    }

    private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RadioStation station)
        {
            station.IsFavorite = !station.IsFavorite;
            _radioService.SetFavorite(station.StationUuid, station.IsFavorite);

            // Synchronize with AppSettings
            if (station.IsFavorite)
            {
                if (!_settings.RadioFavorites.Contains(station.StationUuid))
                    _settings.RadioFavorites.Add(station.StationUuid);
            }
            else
            {
                _settings.RadioFavorites.Remove(station.StationUuid);
            }

            // Refresh UI bindings
            ApplyFilterAndDisplay();
        }
    }

    private void VisualizerMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var mode = SelectedVisualizerMode;
        _settings.VisualizerMode = mode;
        VisualizerModeChanged?.Invoke(mode);
    }
}

