using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NJPlayer.Core.Settings;
using NJPlayer.Features.Radio;
using Xunit;

namespace NJPlayer.Tests;

public class RadioTests
{
    [Fact]
    public void BuildVisualizerFilter_ShowCqt_ReturnsValidLavfiGraph()
    {
        var filter = RadioBrowserService.BuildVisualizerFilter("showcqt", 1280, 720);
        Assert.Contains("showcqt=s=1280x720", filter);
        Assert.Contains("[aid1]asplit", filter);
        Assert.Contains("[vo]", filter);
    }

    [Fact]
    public void BuildVisualizerFilter_ShowWaves_ReturnsValidLavfiGraph()
    {
        var filter = RadioBrowserService.BuildVisualizerFilter("showwaves", 1920, 1080);
        Assert.Contains("showwaves=s=1920x1080", filter);
        Assert.Contains("mode=line", filter);
        Assert.Contains("0x00E5FF", filter); // Electric cyan accent token
    }

    [Fact]
    public void BuildVisualizerFilter_OffOrNull_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, RadioBrowserService.BuildVisualizerFilter("off"));
        Assert.Equal(string.Empty, RadioBrowserService.BuildVisualizerFilter(""));
        Assert.Equal(string.Empty, RadioBrowserService.BuildVisualizerFilter(null!));
    }

    [Fact]
    public void Favorites_Tracking_AddAndRemove()
    {
        var initial = new[] { "uuid-1", "uuid-2" };
        var service = new RadioBrowserService(initialFavorites: initial);

        Assert.True(service.IsFavorite("uuid-1"));
        Assert.True(service.IsFavorite("uuid-2"));
        Assert.False(service.IsFavorite("uuid-3"));

        service.SetFavorite("uuid-3", true);
        Assert.True(service.IsFavorite("uuid-3"));

        service.SetFavorite("uuid-1", false);
        Assert.False(service.IsFavorite("uuid-1"));
    }

    [Fact]
    public async Task GetCountriesAsync_PinsEthiopiaAtTop()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NJPlayer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Seed a local cache file with mock countries where Ethiopia is not first
            var mockCountriesJson = """
            [
                { "name": "United States", "iso_3166_1": "US", "stationcount": 1200 },
                { "name": "Germany", "iso_3166_1": "DE", "stationcount": 800 },
                { "name": "Ethiopia", "iso_3166_1": "ET", "stationcount": 25 },
                { "name": "France", "iso_3166_1": "FR", "stationcount": 600 }
            ]
            """;
            await File.WriteAllTextAsync(Path.Combine(tempDir, "radio_countries.json"), mockCountriesJson);

            var service = new RadioBrowserService(cacheDirectory: tempDir);
            var countries = await service.GetCountriesAsync();

            Assert.NotEmpty(countries);
            Assert.Equal("ET", countries[0].IsoCode);
            Assert.Equal("Ethiopia", countries[0].Name);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RadioStation_Model_FormatProperties()
    {
        var station = new RadioStation
        {
            StationUuid = "station-abc",
            Name = "Sheger FM",
            Country = "Ethiopia",
            CountryCode = "et",
            Bitrate = 128,
            Codec = "MP3",
            Url = "http://stream.example.com/sheger"
        };

        Assert.Equal("128 kbps", station.BitrateDisplay);
        Assert.Equal("[ET] Ethiopia", station.DisplayCountry);
        Assert.Equal("http://stream.example.com/sheger", station.StreamUrl);
    }

    [Fact]
    public void AppSettings_RadioFavoritesAndVisualizer_Serialized()
    {
        var settings = new AppSettings
        {
            VisualizerMode = "showwaves",
            RadioFavorites = new List<string> { "fav-1", "fav-2" }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("showwaves", deserialized.VisualizerMode);
        Assert.Equal(2, deserialized.RadioFavorites.Count);
        Assert.Contains("fav-1", deserialized.RadioFavorites);
        Assert.Contains("fav-2", deserialized.RadioFavorites);
    }
}

