using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NJPlayer.Core.Playlist;

/// <summary>
/// Manages persistent "Watch Later" video queue, auto-advancing, and JSON persistence.
/// </summary>
public sealed class WatchLaterManager
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".ts", ".m4v",
        ".mp3", ".flac", ".wav", ".m4a", ".opus", ".ogg", ".aac", ".wma"
    };

    private readonly ObservableCollection<WatchLaterItem> _items = new();

    public ObservableCollection<WatchLaterItem> Items => _items;

    public event EventHandler? QueueChanged;

    public static string GetDefaultWatchLaterFilePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var flagPath = Path.Combine(baseDir, "portable.flag");
        var folder = File.Exists(flagPath)
            ? Path.Combine(baseDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NJPlayer");

        return Path.Combine(folder, "watch_later.json");
    }

    public bool Add(string filePath, string? title = null, double duration = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        // Prevent duplicate queue entries for the same path
        if (_items.Any(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var item = WatchLaterItem.Create(filePath, title, duration);
        _items.Add(item);
        SaveToJson();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public int AddFolder(string folderPath, bool recursive = true)
    {
        if (!Directory.Exists(folderPath)) return 0;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(folderPath, "*.*", option)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        int count = 0;
        foreach (var file in files)
        {
            if (Add(file)) count++;
        }

        return count;
    }

    public bool Remove(WatchLaterItem item)
    {
        if (_items.Remove(item))
        {
            SaveToJson();
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public bool RemoveAt(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            _items.RemoveAt(index);
            SaveToJson();
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _items.Count || toIndex < 0 || toIndex >= _items.Count || fromIndex == toIndex)
        {
            return;
        }

        _items.Move(fromIndex, toIndex);
        SaveToJson();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count > 0)
        {
            _items.Clear();
            SaveToJson();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkWatched(string filePath, bool isWatched = true)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.IsWatched = isWatched;
            SaveToJson();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SaveToJson(string? filePath = null)
    {
        try
        {
            filePath ??= GetDefaultWatchLaterFilePath();
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore I/O lock issues on background write
        }
    }

    public void LoadFromJson(string? filePath = null)
    {
        filePath ??= GetDefaultWatchLaterFilePath();
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var items = JsonSerializer.Deserialize<List<WatchLaterItem>>(json);
                if (items != null)
                {
                    _items.Clear();
                    foreach (var itm in items.Where(i => File.Exists(i.FilePath) || Uri.TryCreate(i.FilePath, UriKind.Absolute, out _)))
                    {
                        _items.Add(itm);
                    }
                    QueueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch { }
        }
    }
}

