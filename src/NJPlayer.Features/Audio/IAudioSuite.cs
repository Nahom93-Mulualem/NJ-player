using System;
using System.Collections.Generic;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Interface for the modular audio suite feature (10-band equalizer, presets, bass/treble, night mode).
/// </summary>
public interface IAudioSuite
{
    AudioSettings CurrentSettings { get; }
    IReadOnlyList<string> AvailablePresets { get; }

    event EventHandler<AudioSettings>? SettingsChanged;

    void SetBandGain(int bandIndex, double gainDb);
    void SetBassBoost(double gainDb);
    void SetTreble(double gainDb);
    void SetNightMode(bool enabled);
    void SetEnabled(bool enabled);
    void ApplyPreset(string presetName);
    void Reset();

    string BuildMpvFilterString();
}

