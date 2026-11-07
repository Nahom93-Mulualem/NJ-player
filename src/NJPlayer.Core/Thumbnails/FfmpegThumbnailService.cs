using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Core.Thumbnails;

/// <summary>
/// Generates video thumbnail preview frames using an external ffmpeg process.
/// Employs ProcessStartInfo.ArgumentList, low priority, cancellation, and a 5-second bucket in-memory cache.
/// </summary>
public sealed class FfmpegThumbnailService : IThumbnailService
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private string? _cachedFfmpegPath;
    private bool _ffmpegLookedUp;

    public static string GetBucketKey(string filePath, double timeInSeconds)
    {
        var bucket = (long)Math.Max(0, Math.Floor(timeInSeconds / 5.0));
        return $"{filePath}|bucket_{bucket}";
    }

    public async Task<byte[]?> GetThumbnailAsync(string mediaFilePath, double timeInSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return null;
        }

        // 1. Check in-memory 5-second bucket cache
        var cacheKey = GetBucketKey(mediaFilePath, timeInSeconds);
        if (_cache.TryGetValue(cacheKey, out var cachedBytes))
        {
            return cachedBytes;
        }

        // 2. Locate ffmpeg executable
        var ffmpegPath = FindFfmpegExecutable();
        if (ffmpegPath == null)
        {
            return null;
        }

        // 3. Launch ffmpeg process with low priority and argument list
        try
        {
            var bucketTime = Math.Max(0, Math.Floor(timeInSeconds / 5.0) * 5.0);
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Use ProcessStartInfo.ArgumentList for safety - never string concatenation
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(bucketTime.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-noaccurate_seek");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(mediaFilePath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add("scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2:black");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("image2pipe");
            psi.ArgumentList.Add("-vcodec");
            psi.ArgumentList.Add("mjpeg");
            psi.ArgumentList.Add("-");

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return null;
            }

            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch
            {
                // Ignore if process permissions prevent setting priority
            }

            using var outputMs = new MemoryStream();
            using var reg = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            });

            // Read output stream asynchronously
            var readTask = process.StandardOutput.BaseStream.CopyToAsync(outputMs, cancellationToken);
            var processExitTask = process.WaitForExitAsync(cancellationToken);

            var timeoutTask = Task.Delay(2500, cancellationToken);
            var completedTask = await Task.WhenAny(Task.WhenAll(readTask, processExitTask), timeoutTask);

            if (completedTask == timeoutTask || cancellationToken.IsCancellationRequested)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                return null;
            }

            var bytes = outputMs.ToArray();
            if (bytes.Length > 0)
            {
                _cache.TryAdd(cacheKey, bytes);
                return bytes;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[NJPlayer Thumbnail Warning] ffmpeg thumbnail extraction error: {ex.Message}");
        }

        return null;
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    private string? FindFfmpegExecutable()
    {
        if (_ffmpegLookedUp)
        {
            return _cachedFfmpegPath;
        }

        _ffmpegLookedUp = true;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Check next to exe
        var candidate1 = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(candidate1))
        {
            return _cachedFfmpegPath = candidate1;
        }

        // Check in libs/ folder
        var candidate2 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "libs", "ffmpeg.exe"));
        if (File.Exists(candidate2))
        {
            return _cachedFfmpegPath = candidate2;
        }

        // Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(part.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return _cachedFfmpegPath = candidate;
                }
            }
        }

        Trace.WriteLine("[NJPlayer Info] ffmpeg.exe not found. Video hover preview thumbnails will be skipped.");
        return _cachedFfmpegPath = null;
    }
}
