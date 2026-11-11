using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NJPlayer.Core.Tools;

namespace NJPlayer.Features.Downloader;

/// <summary>
/// Progress reporting model for yt-dlp download operation.
/// </summary>
public sealed class DownloadProgress
{
    public double Percentage { get; init; }
    public string TotalSize { get; init; } = string.Empty;
    public string DownloadSpeed { get; init; } = string.Empty;
    public string Eta { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Service executing online downloads using external yt-dlp process.
/// Handles progress updates, cancellation, and downloaded file detection.
/// </summary>
public sealed class MediaDownloaderService
{
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([0-9.]+)%\s+of\s+~?([0-9.]+[A-Za-z]+)\s+at\s+([0-9.]+[A-Za-z]+/s)\s+ETA\s+([0-9:]+)",
        RegexOptions.Compiled);

    private static readonly Regex DestinationRegex = new(
        @"\[(?:download|Merger)\]\s+Destination:\s+(.+)",
        RegexOptions.Compiled);

    private readonly string? _ytdlpPath;
    private readonly ToolLookupResult _lookupResult;

    public MediaDownloaderService(string? customYtDlpPath = null)
    {
        if (!string.IsNullOrEmpty(customYtDlpPath))
        {
            _ytdlpPath = customYtDlpPath;
            _lookupResult = new ToolLookupResult(
                "yt-dlp.exe",
                File.Exists(customYtDlpPath),
                customYtDlpPath,
                [customYtDlpPath],
                File.Exists(customYtDlpPath) ? "Custom path valid." : "Custom path not found.");
        }
        else
        {
            _lookupResult = ExternalToolLocator.Locate("yt-dlp.exe");
            _ytdlpPath = _lookupResult.ExecutablePath;
        }
    }

    public bool IsYtDlpAvailable => !string.IsNullOrEmpty(_ytdlpPath) && File.Exists(_ytdlpPath);

    public string? YtDlpPath => _ytdlpPath;

    public ToolLookupResult LookupResult => _lookupResult;

    public static string? FindYtDlpBinary()
    {
        return ExternalToolLocator.Locate("yt-dlp.exe").ExecutablePath;
    }

    public async Task<string?> DownloadAsync(DownloadOptions options, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        options.Validate();

        if (!IsYtDlpAvailable)
        {
            throw new FileNotFoundException(
                "yt-dlp.exe was not found. Please place yt-dlp.exe into the 'libs' folder or add it to system PATH.");
        }

        if (!Directory.Exists(options.OutputDirectory))
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }

        var outputTemplate = Path.Combine(options.OutputDirectory, "%(title)s.%(ext)s");

        var startInfo = new ProcessStartInfo
        {
            FileName = _ytdlpPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);

        // Format selection
        switch (options.Format)
        {
            case DownloadFormatPreset.Max1080p:
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestvideo[height<=1080]+bestaudio/best[height<=1080]/best");
                break;

            case DownloadFormatPreset.Max720p:
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestvideo[height<=720]+bestaudio/best[height<=720]/best");
                break;

            case DownloadFormatPreset.AudioOnlyMp3:
                startInfo.ArgumentList.Add("-x");
                startInfo.ArgumentList.Add("--audio-format");
                startInfo.ArgumentList.Add("mp3");
                break;

            case DownloadFormatPreset.AudioOnlyBest:
                startInfo.ArgumentList.Add("-x");
                break;

            case DownloadFormatPreset.BestAvailable:
            default:
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestvideo+bestaudio/best");
                break;
        }

        startInfo.ArgumentList.Add(options.Url);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<bool>();
        string? downloadedFilePath = null;

        DataReceivedEventHandler handler = (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            // Check destination path
            var destMatch = DestinationRegex.Match(e.Data);
            if (destMatch.Success)
            {
                downloadedFilePath = destMatch.Groups[1].Value.Trim();
            }

            // Check progress
            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                var total = match.Groups[2].Value;
                var speed = match.Groups[3].Value;
                var eta = match.Groups[4].Value;

                progress?.Report(new DownloadProgress
                {
                    Percentage = pct,
                    TotalSize = total,
                    DownloadSpeed = speed,
                    Eta = eta,
                    StatusMessage = $"Downloading {pct:F1}% of {total} ({speed}, ETA {eta})"
                });
            }
        };

        process.OutputDataReceived += handler;
        process.ErrorDataReceived += handler;

        process.Exited += (_, _) =>
        {
            if (process.ExitCode == 0)
            {
                progress?.Report(new DownloadProgress
                {
                    Percentage = 100.0,
                    StatusMessage = "Download complete!"
                });
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException($"yt-dlp exited with error code {process.ExitCode}."));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start yt-dlp process.");
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

        return downloadedFilePath;
    }
}

