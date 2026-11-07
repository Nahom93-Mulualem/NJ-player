using System;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Core.Thumbnails;

/// <summary>
/// Service interface for extracting video preview thumbnails.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Asynchronously extracts a thumbnail frame for the specified media file at the given time offset.
    /// Returns raw image bytes (JPEG) or null if extraction was cancelled, failed, or ffmpeg is unavailable.
    /// </summary>
    Task<byte[]?> GetThumbnailAsync(string mediaFilePath, double timeInSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the in-memory thumbnail cache.
    /// </summary>
    void ClearCache();
}
