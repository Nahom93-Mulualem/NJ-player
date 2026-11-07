using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Implements IAudioSuite for real-time 10-band equalization, presets, tone controls, and dynamic range normalization.
/// </summary>
public sealed class AudioSuiteService : IAudioSuite
{
    private static readonly Dictionary<string, double[]> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["Rock"] = [4.5, 3.0, -1.0, -2.0, -0.5, 2.0, 3.5, 4.0, 4.5, 5.0],
        ["Pop"] = [1.5, 2.5, 4.0, 2.5, 0.0, -1.5, 1.5, 2.5, 3.0, 3.5],
        ["Jazz"] = [3.0, 2.0, 1.0, 2.0, -1.5, -1.5, 0.0, 1.5, 3.0, 3.5],
        ["Classical"] = [4.0, 3.0, 2.5, 2.0, -1.5, -1.5, 0.0, 2.5, 3.5, 3.5],
        ["Dance / EDM"] = [5.5, 4.5, 2.0, 0.0, 2.0, 3.5, 4.0, 3.5, 4.5, 4.0],
        ["Vocal Boost"] = [-2.0, -3.0, -1.5, 1.5, 4.0, 4.5, 3.5, 1.5, 0.0, -1.5],
        ["Bass Heavy"] = [7.0, 6.0, 4.5, 2.5, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0]
    };

    private readonly AudioSettings _settings;

    public AudioSettings CurrentSettings => _settings;
    public IReadOnlyList<string> AvailablePresets => Presets.Keys.ToList();

    public event EventHandler<AudioSettings>? SettingsChanged;

    public AudioSuiteService(AudioSettings? initialSettings = null)
    {
        _settings = initialSettings?.Clone() ?? new AudioSettings();
    }

    public void SetBandGain(int bandIndex, double gainDb)
    {
        if (bandIndex < 0 || bandIndex >= _settings.BandGains.Length) return;

        var clamped = Math.Clamp(gainDb, AudioSettings.MinGainDb, AudioSettings.MaxGainDb);
        if (Math.Abs(_settings.BandGains[bandIndex] - clamped) > 0.05)
        {
            _settings.BandGains[bandIndex] = Math.Round(clamped, 1);
            _settings.ActivePreset = "Custom";
            RaiseSettingsChanged();
        }
    }

    public void SetBassBoost(double gainDb)
    {
        var clamped = Math.Clamp(gainDb, 0.0, AudioSettings.MaxGainDb);
        if (Math.Abs(_settings.BassBoost - clamped) > 0.05)
        {
            _settings.BassBoost = Math.Round(clamped, 1);
            RaiseSettingsChanged();
        }
    }

    public void SetTreble(double gainDb)
    {
        var clamped = Math.Clamp(gainDb, AudioSettings.MinGainDb, AudioSettings.MaxGainDb);
        if (Math.Abs(_settings.Treble - clamped) > 0.05)
        {
            _settings.Treble = Math.Round(clamped, 1);
            RaiseSettingsChanged();
        }
    }

    public void SetNightMode(bool enabled)
    {
        if (_settings.NightMode != enabled)
        {
            _settings.NightMode = enabled;
            RaiseSettingsChanged();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_settings.IsEnabled != enabled)
        {
            _settings.IsEnabled = enabled;
            RaiseSettingsChanged();
        }
    }

    public void ApplyPreset(string presetName)
    {
        if (Presets.TryGetValue(presetName, out var presetGains))
        {
            _settings.ActivePreset = presetName;
            Array.Copy(presetGains, _settings.BandGains, presetGains.Length);
            RaiseSettingsChanged();
        }
    }

    public void Reset()
    {
        _settings.Reset();
        RaiseSettingsChanged();
    }

    public string BuildMpvFilterString()
    {
        if (!_settings.IsEnabled || _settings.IsFlat())
        {
            return string.Empty;
        }

        var filters = new List<string>();

        // 1. Equalizer bands (skip 0 dB bands for zero overhead)
        for (int i = 0; i < AudioSettings.CenterFrequencies.Length; i++)
        {
            var gain = _settings.BandGains[i];
            if (Math.Abs(gain) >= 0.1)
            {
                var freq = AudioSettings.CenterFrequencies[i];
                filters.Add(string.Create(CultureInfo.InvariantCulture, $"equalizer=f={freq:0}:width_type=o:w=1:g={gain:0.0}"));
            }
        }

        // 2. Bass Boost (low-shelf at 100 Hz)
        if (_settings.BassBoost >= 0.1)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"bass=g={_settings.BassBoost:0.0}:f=100"));
        }

        // 3. Treble (high-shelf at 3 kHz)
        if (Math.Abs(_settings.Treble) >= 0.1)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"treble=g={_settings.Treble:0.0}:f=3000"));
        }

        // 4. Night Mode (Dynamic Audio Normalizer)
        if (_settings.NightMode)
        {
            filters.Add("dynaudnorm=f=150:g=15:m=8:r=0.9");
        }

        if (filters.Count == 0)
        {
            return string.Empty;
        }

        return $"lavfi=[{string.Join(",", filters)}]";
    }

    private void RaiseSettingsChanged()
    {
        SettingsChanged?.Invoke(this, _settings);
    }
}

