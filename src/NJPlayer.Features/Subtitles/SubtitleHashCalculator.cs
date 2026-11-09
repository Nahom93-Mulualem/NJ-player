using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Features.Subtitles;

/// <summary>
/// Computes the 64-bit chunk hash used by subtitle services (OpenSubtitles / Podnapisi).
/// Runs completely off the UI thread with cancellation support.
/// </summary>
public static class SubtitleHashCalculator
{
    private const int ChunkSize = 65536; // 64 KB

    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() => ComputeHash(filePath, ct), ct).ConfigureAwait(false);
    }

    public static string ComputeHash(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var fileLength = stream.Length;
            if (fileLength < ChunkSize)
            {
                return string.Empty;
            }

            ulong hash = (ulong)fileLength;

            // 1. First 64 KB
            var buffer = new byte[sizeof(ulong)];
            for (int i = 0; i < ChunkSize / sizeof(ulong); i++)
            {
                ct.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, sizeof(ulong));
                if (read < sizeof(ulong)) break;
                hash += BitConverter.ToUInt64(buffer, 0);
            }

            // 2. Last 64 KB
            stream.Seek(Math.Max(0, fileLength - ChunkSize), SeekOrigin.Begin);
            for (int i = 0; i < ChunkSize / sizeof(ulong); i++)
            {
                ct.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, sizeof(ulong));
                if (read < sizeof(ulong)) break;
                hash += BitConverter.ToUInt64(buffer, 0);
            }

            return hash.ToString("x16");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }
}

