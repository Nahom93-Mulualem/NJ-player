using System;
using System.Text.Json.Serialization;

namespace NJPlayer.Features.Radio;

/// <summary>
/// Model representing a radio station from the radio-browser.info API.
/// </summary>
public sealed class RadioStation
{
    [JsonPropertyName("stationuuid")]
    public string StationUuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("url_resolved")]
    public string UrlResolved { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("favicon")]
    public string Favicon { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("countrycode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("clickcount")]
    public int ClickCount { get; set; }

    [JsonIgnore]
    public bool IsFavorite { get; set; }

    [JsonIgnore]
    public string StreamUrl => !string.IsNullOrWhiteSpace(UrlResolved) ? UrlResolved : Url;

    [JsonIgnore]
    public string BitrateDisplay => Bitrate > 0 ? $"{Bitrate} kbps" : (!string.IsNullOrWhiteSpace(Codec) ? Codec.ToUpperInvariant() : "Stream");

    [JsonIgnore]
    public string DisplayCountry => !string.IsNullOrWhiteSpace(CountryCode) ? $"[{CountryCode.ToUpperInvariant()}] {Country}" : Country;
}

