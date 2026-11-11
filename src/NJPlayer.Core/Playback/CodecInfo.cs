using System;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Detailed codec, decoder, and container telemetry snapshot for active playback.
/// </summary>
public sealed class CodecInfo
{
    public string VideoCodec { get; init; } = "None";
    public string VideoDecoder { get; init; } = "None";
    public string HardwareDecoder { get; init; } = "Software";
    public string Resolution { get; init; } = "N/A";
    public string FrameRate { get; init; } = "N/A";
    public string PixelFormat { get; init; } = "N/A";
    public string VideoBitrate { get; init; } = "N/A";
    public string AudioCodec { get; init; } = "None";
    public string AudioChannels { get; init; } = "N/A";
    public string AudioSampleRate { get; init; } = "N/A";
    public string Container { get; init; } = "N/A";
    public bool IsHardwareAccelerated => !string.IsNullOrWhiteSpace(HardwareDecoder) &&
                                          !string.Equals(HardwareDecoder, "no", StringComparison.OrdinalIgnoreCase) &&
                                          !string.Equals(HardwareDecoder, "software", StringComparison.OrdinalIgnoreCase);

    public static CodecInfo FromPlayer(MpvPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var videoCodec = player.GetPropertyString("video-format") ?? player.GetPropertyString("video-codec") ?? "None";
        var hwdec = player.GetPropertyString("hwdec-current") ?? player.HardwareDecoder ?? "software";
        var width = player.GetPropertyDouble("video-params/w");
        var height = player.GetPropertyDouble("video-params/h");
        var fps = player.GetPropertyDouble("estimated-vf-fps") ?? player.GetPropertyDouble("container-fps");
        var pixFmt = player.GetPropertyString("video-params/pixelformat") ?? "N/A";
        var vBitrate = player.GetPropertyDouble("video-bitrate");

        var audioCodec = player.GetPropertyString("audio-codec") ?? player.GetPropertyString("audio-format") ?? "None";
        var channels = player.GetPropertyDouble("audio-params/channel-count");
        var sampleRate = player.GetPropertyDouble("audio-params/samplerate");
        var container = player.GetPropertyString("file-format") ?? "N/A";

        var resolutionStr = (width > 0 && height > 0) ? $"{width:0}x{height:0}" : "N/A";
        var fpsStr = fps.HasValue && fps.Value > 0 ? $"{fps.Value:0.00} fps" : "N/A";
        var vBitrateStr = vBitrate.HasValue && vBitrate.Value > 0 ? $"{vBitrate.Value / 1_000_000:0.0} Mbps" : "N/A";
        var channelsStr = channels.HasValue && channels.Value > 0 ? $"{channels.Value:0} channels" : "N/A";
        var sampleRateStr = sampleRate.HasValue && sampleRate.Value > 0 ? $"{sampleRate.Value:0} Hz" : "N/A";

        return new CodecInfo
        {
            VideoCodec = videoCodec.ToUpperInvariant(),
            VideoDecoder = $"{videoCodec} ({hwdec})",
            HardwareDecoder = hwdec,
            Resolution = resolutionStr,
            FrameRate = fpsStr,
            PixelFormat = pixFmt,
            VideoBitrate = vBitrateStr,
            AudioCodec = audioCodec.ToUpperInvariant(),
            AudioChannels = channelsStr,
            AudioSampleRate = sampleRateStr,
            Container = container
        };
    }
}

