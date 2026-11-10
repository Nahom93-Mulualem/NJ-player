using System;
using System.IO;

namespace NJPlayer.Core.Playlist;

/// <summary>
/// Model representing a playable track or file in the playlist.
/// </summary>
public sealed class PlaylistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    public string FormattedDuration => Duration.TotalHours >= 1
        ? Duration.ToString(@"hh\:mm\:ss")
        : Duration.ToString(@"mm\:ss");

    public PlaylistItem() { }

    public PlaylistItem(string filePath, string? title = null, TimeSpan? duration = null)
    {
        FilePath = filePath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
        else
        {
            try
            {
                Title = Path.GetFileNameWithoutExtension(FilePath);
            }
            catch
            {
                Title = FilePath;
            }

            if (string.IsNullOrWhiteSpace(Title))
            {
                Title = FilePath;
            }
        }
        Duration = duration ?? TimeSpan.Zero;
    }

    public override string ToString() => $"{Title} ({FormattedDuration})";
}

public enum RepeatMode
{
    None = 0,
    RepeatAll = 1,
    RepeatOne = 2
}
