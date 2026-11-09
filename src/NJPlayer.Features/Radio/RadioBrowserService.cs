using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Features.Radio;

/// <summary>
/// Service providing radio station search, country filtering (with Ethiopia at top),
/// caching, and real-time audio visualizer filter generation.
/// </summary>
public sealed class RadioBrowserService
{
    private const string DiscoveryHostname = "all.api.radio-browser.info";
    private static readonly string[] FallbackServers =
    [
        "de1.api.radio-browser.info",
        "nl1.api.radio-browser.info",
        "at1.api.radio-browser.info"
    ];

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static RadioBrowserService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NJPlayer/1.0 (Windows; x64) RadioBrowser");
    }

    private readonly string _cacheDirectory;
    private string? _activeServer;
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> FavoriteIds => _favorites;

    public RadioBrowserService(string? cacheDirectory = null, IEnumerable<string>? initialFavorites = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NJPlayer");

        if (initialFavorites != null)
        {
            foreach (var fav in initialFavorites)
            {
                if (!string.IsNullOrWhiteSpace(fav)) _favorites.Add(fav);
            }
        }

        try
        {
            if (!Directory.Exists(_cacheDirectory)) Directory.CreateDirectory(_cacheDirectory);
        }
        catch { }
    }

    public void SetFavorite(string stationUuid, bool isFavorite)
    {
        if (string.IsNullOrWhiteSpace(stationUuid)) return;
        if (isFavorite) _favorites.Add(stationUuid);
        else _favorites.Remove(stationUuid);
    }

    public bool IsFavorite(string stationUuid)
    {
        return !string.IsNullOrWhiteSpace(stationUuid) && _favorites.Contains(stationUuid);
    }

    /// <summary>
    /// Resolves active API server dynamically via DNS lookup per radio-browser.info documentation.
    /// </summary>
    public async Task<string> GetActiveServerAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_activeServer)) return _activeServer;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(DiscoveryHostname, ct).ConfigureAwait(false);
            if (addresses != null && addresses.Length > 0)
            {
                foreach (var ip in addresses)
                {
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(ip.ToString(), ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName.Contains("radio-browser.info", StringComparison.OrdinalIgnoreCase))
                        {
                            _activeServer = entry.HostName;
                            return _activeServer;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Fallback to primary mirrors
        _activeServer = FallbackServers[0];
        return _activeServer;
    }

    /// <summary>
    /// Fetches all countries from API with Ethiopia (ET) pinned at the top.
    /// </summary>
    public async Task<IReadOnlyList<RadioCountry>> GetCountriesAsync(CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(_cacheDirectory, "radio_countries.json");
        List<RadioCountry>? countries = null;

        try
        {
            var server = await GetActiveServerAsync(ct).ConfigureAwait(false);
            var url = $"https://{server}/json/countries";
            var json = await HttpClient.GetStringAsync(url, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(json))
            {
                countries = JsonSerializer.Deserialize<List<RadioCountry>>(json);
                if (countries != null && countries.Count > 0)
                {
                    await File.WriteAllTextAsync(cacheFile, json, ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Offline or error: load from cache if available
            if (File.Exists(cacheFile))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile, ct).ConfigureAwait(false);
                    countries = JsonSerializer.Deserialize<List<RadioCountry>>(cachedJson);
                }
                catch { }
            }
        }

        countries ??= [];

        // Pin Ethiopia (ET) at the top per Prompt 12 requirement
        var etIndex = countries.FindIndex(c =>
            string.Equals(c.IsoCode, "ET", StringComparison.OrdinalIgnoreCase) ||
            c.Name.StartsWith("Ethiopia", StringComparison.OrdinalIgnoreCase));

        if (etIndex > 0)
        {
            var et = countries[etIndex];
            countries.RemoveAt(etIndex);
            countries.Insert(0, et);
        }
        else if (etIndex < 0)
        {
            countries.Insert(0, new RadioCountry { Name = "Ethiopia", IsoCode = "ET", StationCount = 20 });
        }

        return countries;
    }

    /// <summary>
    /// Searches radio stations by query and country code.
    /// </summary>
    public async Task<IReadOnlyList<RadioStation>> SearchStationsAsync(string? query = null, string? countryCode = null, int limit = 60, CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(_cacheDirectory, "radio_stations_cache.json");
        List<RadioStation>? stations = null;

        try
        {
            var server = await GetActiveServerAsync(ct).ConfigureAwait(false);
            string url;

            if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(countryCode))
            {
                url = $"https://{server}/json/stations/topclick/{limit}";
            }
            else
            {
                var queryParams = new List<string>
                {
                    $"limit={limit}",
                    "order=clickcount",
                    "reverse=true"
                };

                if (!string.IsNullOrWhiteSpace(query))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(query.Trim())}");
                }

                if (!string.IsNullOrWhiteSpace(countryCode) && !string.Equals(countryCode, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    queryParams.Add($"countrycode={Uri.EscapeDataString(countryCode.Trim())}");
                }

                url = $"https://{server}/json/stations/search?{string.Join("&", queryParams)}";
            }

            var json = await HttpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                stations = JsonSerializer.Deserialize<List<RadioStation>>(json);
                if (stations != null && stations.Count > 0)
                {
                    // Cache the results
                    await File.WriteAllTextAsync(cacheFile, json, ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Offline fallback
            if (File.Exists(cacheFile))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile, ct).ConfigureAwait(false);
                    stations = JsonSerializer.Deserialize<List<RadioStation>>(cachedJson);
                }
                catch { }
            }
        }

        stations ??= [];

        // Apply favorite flags
        foreach (var st in stations)
        {
            st.IsFavorite = _favorites.Contains(st.StationUuid);
        }

        return stations;
    }

    /// <summary>
    /// Generates mpv lavfi-complex audio visualizer filter string.
    /// </summary>
    public static string BuildVisualizerFilter(string mode, int width = 1280, int height = 720)
    {
        return mode?.ToLowerInvariant() switch
        {
            "showcqt" => $"[aid1]asplit[ao][v];[v]showcqt=s={width}x{height}:fps=30:bar_g=2:count=1[vo]",
            "showwaves" => $"[aid1]asplit[ao][v];[v]showwaves=s={width}x{height}:mode=line:colors=0x00E5FF[vo]",
            _ => string.Empty
        };
    }
}
