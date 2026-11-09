using System;
using System.Text.Json.Serialization;

namespace NJPlayer.Features.Radio;

/// <summary>
/// Model representing a country filter item from the radio-browser.info API.
/// </summary>
public sealed class RadioCountry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("iso_3166_1")]
    public string IsoCode { get; set; } = string.Empty;

    [JsonPropertyName("stationcount")]
    public int StationCount { get; set; }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(IsoCode) ? $"{Name} ({IsoCode.ToUpperInvariant()}) - {StationCount}" : Name;
}

