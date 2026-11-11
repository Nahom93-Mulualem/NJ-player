using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NJPlayer.Core.Playback;
using NJPlayer.Core.Settings;

namespace NJPlayer.App.Views;

public partial class CodecManagerWindow : Window
{
    private readonly MpvPlayer? _player;
    private readonly SettingsManager _settingsManager;

    public CodecManagerWindow(MpvPlayer? player, SettingsManager settingsManager)
    {
        InitializeComponent();
        _player = player;
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

        PopulateActiveCodecMetrics();
        PopulateSettings();
    }

    private void PopulateActiveCodecMetrics()
    {
        if (_player != null && _player.Duration > TimeSpan.Zero)
        {
            var info = CodecInfo.FromPlayer(_player);
            VideoDecoderText.Text = $"{info.VideoCodec} ({info.HardwareDecoder})";
            ResolutionText.Text = $"{info.Resolution} @ {info.FrameRate} ({info.PixelFormat})";
            AudioDecoderText.Text = $"{info.AudioCodec} / {info.AudioSampleRate} ({info.AudioChannels})";
            ContainerText.Text = $"{info.Container} / {info.VideoBitrate}";
        }
        else
        {
            VideoDecoderText.Text = "No active media stream";
            ResolutionText.Text = "N/A";
            AudioDecoderText.Text = "N/A";
            ContainerText.Text = "N/A";
        }
    }

    private void PopulateSettings()
    {
        var s = _settingsManager.Settings;

        // Select HWDEC option
        foreach (ComboBoxItem item in HwdecCombo.Items)
        {
            if (string.Equals(item.Tag as string, s.HardwareDecoder, StringComparison.OrdinalIgnoreCase))
            {
                HwdecCombo.SelectedItem = item;
                break;
            }
        }

        VideoDecoderPriorityBox.Text = string.IsNullOrWhiteSpace(s.VideoDecoderPriority) ? "auto" : s.VideoDecoderPriority;
        AudioDecoderPriorityBox.Text = string.IsNullOrWhiteSpace(s.AudioDecoderPriority) ? "auto" : s.AudioDecoderPriority;
        CustomLavcBox.Text = s.CustomLavcOptions ?? string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _settingsManager.Settings;

        if (HwdecCombo.SelectedItem is ComboBoxItem item && item.Tag is string hwdec)
        {
            s.HardwareDecoder = hwdec;
            if (_player != null && _player.Duration > TimeSpan.Zero)
            {
                _player.SetPropertyString("hwdec", hwdec);
            }
        }

        s.VideoDecoderPriority = VideoDecoderPriorityBox.Text.Trim();
        s.AudioDecoderPriority = AudioDecoderPriorityBox.Text.Trim();
        s.CustomLavcOptions = CustomLavcBox.Text.Trim();

        _settingsManager.Save();
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

