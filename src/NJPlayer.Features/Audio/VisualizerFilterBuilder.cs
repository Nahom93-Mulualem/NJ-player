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
    /// </summary>
    public static string BuildFilter(string? mode, string? backgroundPath = null, int width = 1280, int height = 720)
    {
        var normalizedMode = mode?.Trim().ToLowerInvariant() switch
        {
            "waveform" or "showwaves" => "waveform",
            "spectrum" or "showcqt" => "spectrum",
            _ => "bars"
        };

        var baseVizFilter = normalizedMode switch
        {
            "waveform" => $"showwaves=s={width}x{height}:mode=line:colors=0x00E5FF",
            "spectrum" => $"showcqt=s={width}x{height}:fps=30:bar_g=2:count=1",
            _ => $"showfreqs=s={width}x{height}:mode=bar:ascale=log:fscale=log:colors=0x00E5FF|0x5CF0FF"
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
