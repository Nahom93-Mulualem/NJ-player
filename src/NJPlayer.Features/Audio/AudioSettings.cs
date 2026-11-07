using System;
using System.Linq;
using NJPlayer.Core.Settings;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Audio settings model containing 10-band equalizer gains, bass boost, treble, and night mode.
/// </summary>
public sealed class AudioSettings
{
    public static readonly double[] CenterFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public const double MinGainDb = -12.0;
    public const double MaxGainDb = 12.0;

    public bool IsEnabled { get; set; } = true;
    public string ActivePreset { get; set; } = "Flat";
    public double[] BandGains { get; set; } = new double[10];
    public double BassBoost { get; set; } = 0.0;
    public double Treble { get; set; } = 0.0;
    public bool NightMode { get; set; } = false;

    public AudioSettings() { }

    public AudioSettings(AudioConfig config)
    {
        if (config == null) return;
        IsEnabled = config.IsEnabled;
        ActivePreset = config.ActivePreset;
        BandGains = config.BandGains != null && config.BandGains.Length == 10
            ? (double[])config.BandGains.Clone()
            : new double[10];
        BassBoost = config.BassBoost;
        Treble = config.Treble;
        NightMode = config.NightMode;
    }

    public AudioConfig ToConfig()
    {
        return new AudioConfig
        {
            IsEnabled = IsEnabled,
            ActivePreset = ActivePreset,
            BandGains = (double[])BandGains.Clone(),
            BassBoost = BassBoost,
            Treble = Treble,
            NightMode = NightMode
        };
    }

    public AudioSettings Clone()
    {
        return new AudioSettings
        {
            IsEnabled = IsEnabled,
            ActivePreset = ActivePreset,
            BandGains = (double[])BandGains.Clone(),
            BassBoost = BassBoost,
            Treble = Treble,
            NightMode = NightMode
        };
    }

    public void Reset()
    {
        IsEnabled = true;
        ActivePreset = "Flat";
        Array.Clear(BandGains, 0, BandGains.Length);
        BassBoost = 0.0;
        Treble = 0.0;
        NightMode = false;
    }

    public bool IsFlat()
    {
        return BandGains.All(g => Math.Abs(g) < 0.05) &&
               Math.Abs(BassBoost) < 0.05 &&
               Math.Abs(Treble) < 0.05 &&
               !NightMode;
    }
}
