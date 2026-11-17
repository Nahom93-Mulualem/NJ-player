using System;
using System.IO;

namespace NJPlayer.Features.Audio;

/// <summary>
/// Generates real-time animated FFmpeg lavfi-complex visualizer filter strings for mpv.
/// Supports animated frequency bars, waveforms, constant-Q spectrums, and custom image/video backgrounds.
/// </summary>
public static class VisualizerFilterBuilder
{
    /// <summary>
    /// Escapes a file path so it can be safely embedded in an FFmpeg filter expression.
    /// </summary>
    public static string EscapeFilterPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        // Replace backslashes with forward slashes
        var normalized = path.Replace('\\', '/');
        // Escape colons (e.g. C: -> C\:)
        normalized = normalized.Replace(":", @"\:");
        // Escape single quotes
        normalized = normalized.Replace("'", @"\'");

        return normalized;
    }

    /// <summary>
    /// Builds the lavfi-complex filter graph string for mpv.
    /// Supports high-framerate (60 fps) frequency bars, centered oscilloscope waveform,
    /// CQT pitch spectrum, and stereo Lissajous vectorscope with electric cyan styling.
    /// </summary>
    public static string BuildFilter(string? mode, string? backgroundPath = null, int width = 1280, int height = 720)
    {
        var normalizedMode = mode?.Trim().ToLowerInvariant() switch
        {
            "waveform" or "showwaves" => "waveform",
            "spectrum" or "showcqt" => "spectrum",
            "vector" or "vectorscope" or "avectorscope" => "vector",
            _ => "bars"
        };

        var baseVizFilter = normalizedMode switch
        {
            "waveform" => $"showwaves=s={width}x{height}:mode=cline:scale=cbrt:draw=scale:rate=60:colors=0x00E5FF|0x5CF0FF",
            "spectrum" => $"showcqt=s={width}x{height}:fps=60:bar_g=2:sono_g=3:bar_t=0.5:count=6",
            "vector" => $"aformat=channel_layouts=stereo,avectorscope=s={width}x{height}:mode=lissajous:rate=60:rc=0:gc=229:bc=255:rf=10:gf=25:bf=30:zoom=1.2:draw=aaline:scale=cbrt:mirror=xy",
            _ => $"showfreqs=s={width}x{height}:mode=bar:ascale=cbrt:fscale=log:win_size=2048:win_func=hann:averaging=3:rate=60:colors=0x00E5FF|0x5CF0FF|0x3DDC97|0x00E5FF"
        };

        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
        {
            var escaped = EscapeFilterPath(backgroundPath);
            return $"movie='{escaped}':loop=0[bg];" +
                   $"[bg]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1[bg_s];" +
                   $"[aid1]asplit[ao][a];" +
                   $"[a]{baseVizFilter},colorkey=0x000000:0.18:0.08[fg];" +
                   $"[bg_s][fg]overlay=0:0:shortest=0[vo]";
        }

        // Clean dark visualizer stream
        return $"[aid1]asplit[ao][v];[v]{baseVizFilter}[vo]";
    }
}
