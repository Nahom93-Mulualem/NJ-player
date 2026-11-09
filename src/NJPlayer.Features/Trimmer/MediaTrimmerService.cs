using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Features.Trimmer;

/// <summary>
/// Progress reporting model for trimming operation.
/// </summary>
public sealed class TrimProgress
{
    public double Percentage { get; init; }
    public TimeSpan ProcessedTime { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Service executing lossless stream copy and re-encoded video trimming using FFmpeg.
/// </summary>
public sealed class MediaTrimmerService
{
    private static readonly Regex TimeRegex = new(@"time=([0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{2})", RegexOptions.Compiled);

    private readonly string? _ffmpegPath;

    public MediaTrimmerService(string? customFfmpegPath = null)
    {
        _ffmpegPath = customFfmpegPath ?? FindFfmpegBinary();
    }

    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);

    public string? FfmpegPath => _ffmpegPath;

    public static string? FindFfmpegBinary()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Check next to executable
        var directPath = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(directPath)) return directPath;

        // 2. Check libs folder
        var libsDir = Path.Combine(baseDir, "..", "..", "..", "libs", "ffmpeg.exe");
        if (File.Exists(libsDir)) return Path.GetFullPath(libsDir);

        var libsFolder = Path.Combine(baseDir, "libs", "ffmpeg.exe");
        if (File.Exists(libsFolder)) return libsFolder;

        // 3. Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(part.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public async Task TrimAsync(TrimOptions options, IProgress<TrimProgress>? progress = null, CancellationToken ct = default)
    {
        options.Validate();

        if (!IsFfmpegAvailable)
        {
            throw new FileNotFoundException(
                "ffmpeg.exe was not found. Please place ffmpeg.exe into the 'libs' folder or add it to system PATH.");
        }

        var totalDuration = options.Duration;
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Populate ArgumentList securely
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");

        // In-point seek
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(options.InPoint.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));

        // Out-point or duration
        startInfo.ArgumentList.Add("-to");
        startInfo.ArgumentList.Add(options.OutPoint.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(options.InputFilePath);

        if (!options.ReEncode)
        {
            // Lossless stream copy
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add("-avoid_negative_ts");
            startInfo.ArgumentList.Add("make_zero");
        }
        else
        {
            // Accurate re-encoding
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libx264");
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("fast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("22");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("aac");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add("192k");
        }

        startInfo.ArgumentList.Add(options.OutputFilePath);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var tcs = new TaskCompletionSource<bool>();

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            var match = TimeRegex.Match(e.Data);
            if (match.Success && TimeSpan.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var processed))
            {
                var pct = totalDuration.TotalSeconds > 0
                    ? Math.Clamp((processed.TotalSeconds / totalDuration.TotalSeconds) * 100.0, 0.0, 99.0)
                    : 0.0;

                progress?.Report(new TrimProgress
                {
                    Percentage = pct,
                    ProcessedTime = processed,
                    TotalDuration = totalDuration,
                    StatusMessage = $"Trimming... {pct:F0}%"
                });
            }
        };

        process.Exited += (_, _) =>
        {
            if (process.ExitCode == 0)
            {
                progress?.Report(new TrimProgress
                {
                    Percentage = 100.0,
                    ProcessedTime = totalDuration,
                    TotalDuration = totalDuration,
                    StatusMessage = "Trim complete!"
                });
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException($"FFmpeg exited with error code {process.ExitCode}."));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ffmpeg process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using (ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            tcs.TrySetCanceled(ct);
        }))
        {
            await tcs.Task.ConfigureAwait(false);
        }
    }
}

