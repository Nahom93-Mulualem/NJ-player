using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NJPlayer.Features.Audio;

namespace NJPlayer.App.Views;

/// <summary>
/// Modeless floating audio suite panel for 10-band equalizer, presets, tone controls, and night mode.
/// </summary>
public partial class AudioPanelWindow : Window
{
    private readonly IAudioSuite _audioSuite;
    private readonly Slider[] _bandSliders;
    private readonly TextBlock[] _gainTexts;
    private bool _isUpdatingUi = false;

    public AudioPanelWindow(IAudioSuite audioSuite)
    {
        InitializeComponent();
        _audioSuite = audioSuite ?? throw new ArgumentNullException(nameof(audioSuite));

        _bandSliders =
        [
            Slider0, Slider1, Slider2, Slider3, Slider4,
            Slider5, Slider6, Slider7, Slider8, Slider9
        ];

        _gainTexts =
        [
            GainText0, GainText1, GainText2, GainText3, GainText4,
            GainText5, GainText6, GainText7, GainText8, GainText9
        ];

        PopulatePresets();
        SyncUiWithSettings();

        _audioSuite.SettingsChanged += (_, _) => Dispatcher.InvokeAsync(SyncUiWithSettings);
    }

    private void PopulatePresets()
    {
        PresetsCombo.Items.Clear();
        foreach (var preset in _audioSuite.AvailablePresets)
        {
            PresetsCombo.Items.Add(preset);
        }
    }

    private void SyncUiWithSettings()
    {
        if (_isUpdatingUi) return;
        _isUpdatingUi = true;

        try
        {
            var s = _audioSuite.CurrentSettings;
            EnableEqCheckBox.IsChecked = s.IsEnabled;

            for (int i = 0; i < _bandSliders.Length && i < s.BandGains.Length; i++)
            {
                var gain = s.BandGains[i];
                _bandSliders[i].Value = gain;
                _gainTexts[i].Text = FormatGain(gain);
            }

            BassSlider.Value = s.BassBoost;
            BassText.Text = FormatGain(s.BassBoost);

            TrebleSlider.Value = s.Treble;
            TrebleText.Text = FormatGain(s.Treble);

            NightModeCheckBox.IsChecked = s.NightMode;

            // Update presets combo selection
            PresetsCombo.SelectedItem = s.ActivePreset;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private static string FormatGain(double gain)
    {
        if (Math.Abs(gain) < 0.05) return "0 dB";
        return gain > 0 ? $"+{gain:0.0} dB" : $"{gain:0.0} dB";
    }

    private void BandSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;

        if (sender is Slider slider && slider.Tag is string tagStr && int.TryParse(tagStr, out var index))
        {
            if (index >= 0 && index < _gainTexts.Length)
            {
                _gainTexts[index].Text = FormatGain(e.NewValue);
            }
            _audioSuite.SetBandGain(index, e.NewValue);

            _isUpdatingUi = true;
            try
            {
                PresetsCombo.SelectedItem = null;
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }
    }

    private void BassSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        BassText.Text = FormatGain(e.NewValue);
        _audioSuite.SetBassBoost(e.NewValue);
    }

    private void TrebleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        TrebleText.Text = FormatGain(e.NewValue);
        _audioSuite.SetTreble(e.NewValue);
    }

    private void NightMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _audioSuite.SetNightMode(NightModeCheckBox.IsChecked == true);
    }

    private void EnableEq_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        _audioSuite.SetEnabled(EnableEqCheckBox.IsChecked == true);
    }

    private void PresetsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;

        if (PresetsCombo.SelectedItem is string presetName)
        {
            _audioSuite.ApplyPreset(presetName);
            SyncUiWithSettings();
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _audioSuite.Reset();
        SyncUiWithSettings();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private bool _isForceClosing = false;

    public void ForceClose()
    {
        _isForceClosing = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            base.OnClosing(e);
            return;
        }

        // Keep modeless window alive for instant re-opening
        e.Cancel = true;
        Hide();
    }
}
