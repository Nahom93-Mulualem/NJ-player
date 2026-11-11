using System;
using System.IO;

namespace NJPlayer.Core.Playlist;

/// <summary>
/// An item in the user's "Watch Later" queue.
/// </summary>
public sealed class WatchLaterItem
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; } = 0.0;
    public double ProgressSeconds { get; set; } = 0.0;
    public bool IsWatched { get; set; } = false;

    public string FormattedDuration
    {
        get
        {
            if (DurationSeconds <= 0) return "--:--";
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    public string FormattedAddedDate => AddedAt.ToString("MMM d, yyyy");

    public static WatchLaterItem Create(string filePath, string? title = null, double duration = 0)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return new WatchLaterItem
        {
            FilePath = filePath,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(filePath) : title,
            AddedAt = DateTime.Now,
            DurationSeconds = duration
        };
    }
}
