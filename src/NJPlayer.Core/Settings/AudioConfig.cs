using System;

namespace NJPlayer.Core.Settings;

/// <summary>
/// Serializable audio configuration model for settings persistence.
/// </summary>
public sealed class AudioConfig
{
    public bool IsEnabled { get; set; } = true;
    public string ActivePreset { get; set; } = "Flat";
    public double[] BandGains { get; set; } = new double[10];
    public double BassBoost { get; set; } = 0.0;
    public double Treble { get; set; } = 0.0;
    public bool NightMode { get; set; } = false;

    public AudioConfig Clone()
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
}
