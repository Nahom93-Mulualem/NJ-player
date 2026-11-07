using System;
using NJPlayer.Features.Audio;
using Xunit;

namespace NJPlayer.Tests;

public class AudioSuiteTests
{
    [Fact]
    public void DefaultSettings_ProduceEmptyFilterString_ZeroOverhead()
    {
        var suite = new AudioSuiteService();

        Assert.True(suite.CurrentSettings.IsFlat());
        Assert.Equal(string.Empty, suite.BuildMpvFilterString());
    }

    [Fact]
    public void EqualizerBand_GeneratesCorrectLavfiFilter()
    {
        var suite = new AudioSuiteService();
        suite.SetBandGain(0, 4.5); // 31 Hz

        var filter = suite.BuildMpvFilterString();
        Assert.StartsWith("lavfi=[", filter);
        Assert.EndsWith("]", filter);
        Assert.Contains("equalizer=f=31:width_type=o:w=1:g=4.5", filter);
    }

    [Fact]
    public void BassTrebleNightMode_GeneratesCombinedFilter()
    {
        var suite = new AudioSuiteService();
        suite.SetBassBoost(6.0);
        suite.SetTreble(-2.5);
        suite.SetNightMode(true);

        var filter = suite.BuildMpvFilterString();
        Assert.Contains("bass=g=6.0:f=100", filter);
        Assert.Contains("treble=g=-2.5:f=3000", filter);
        Assert.Contains("dynaudnorm=f=150:g=15:m=8:r=0.9", filter);
    }

    [Fact]
    public void Presets_ApplyCorrectly_AndSwitchToCustomOnEdit()
    {
        var suite = new AudioSuiteService();
        Assert.Contains("Rock", suite.AvailablePresets);

        suite.ApplyPreset("Rock");
        Assert.Equal("Rock", suite.CurrentSettings.ActivePreset);
        Assert.Equal(4.5, suite.CurrentSettings.BandGains[0]); // Band 0 of Rock is +4.5 dB

        // Modifying any band changes active preset to "Custom"
        suite.SetBandGain(0, 2.0);
        Assert.Equal("Custom", suite.CurrentSettings.ActivePreset);
    }

    [Fact]
    public void GainClamping_EnforcesDbLimits()
    {
        var suite = new AudioSuiteService();

        suite.SetBandGain(1, 25.0); // Above 12 dB limit
        Assert.Equal(AudioSettings.MaxGainDb, suite.CurrentSettings.BandGains[1]);

        suite.SetBandGain(1, -30.0); // Below -12 dB limit
        Assert.Equal(AudioSettings.MinGainDb, suite.CurrentSettings.BandGains[1]);

        suite.SetBassBoost(-5.0); // Bass boost cannot be negative
        Assert.Equal(0.0, suite.CurrentSettings.BassBoost);
    }

    [Fact]
    public void Reset_RestoresFlatDefaults()
    {
        var suite = new AudioSuiteService();
        suite.ApplyPreset("Dance / EDM");
        suite.SetBassBoost(5.0);
        suite.SetNightMode(true);

        suite.Reset();

        Assert.Equal("Flat", suite.CurrentSettings.ActivePreset);
        Assert.True(suite.CurrentSettings.IsFlat());
        Assert.Equal(string.Empty, suite.BuildMpvFilterString());
    }

    [Fact]
    public void DisabledSuite_ReturnsEmptyFilterString()
    {
        var suite = new AudioSuiteService();
        suite.SetBassBoost(5.0);
        Assert.NotEmpty(suite.BuildMpvFilterString());

        suite.SetEnabled(false);
        Assert.Equal(string.Empty, suite.BuildMpvFilterString());
    }
}
