using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NJPlayer.Core.Playlist;

/// <summary>
/// Manages the application's playlist: file/folder ingestion, drag-and-drop reordering,
/// search filtering, shuffle, repeat, M3U import/export, and JSON persistence.
/// </summary>
public sealed class PlaylistManager
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".ts", ".m4v",
        ".mp3", ".flac", ".wav", ".m4a", ".opus", ".ogg", ".aac", ".wma"
    };

    private readonly ObservableCollection<PlaylistItem> _items = new();
    private readonly Random _random = new();

    private int _currentIndex = -1;
    private bool _isShuffled = false;
    private RepeatMode _repeatMode = RepeatMode.None;
    private List<int> _shuffleOrder = new();
    private int _shufflePointer = -1;

    public ObservableCollection<PlaylistItem> Items => _items;
    public int CurrentIndex => _currentIndex;
    public PlaylistItem? CurrentItem => _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;

    public bool IsShuffled
    {
        get => _isShuffled;
        set
        {
            if (_isShuffled != value)
            {
                _isShuffled = value;
                if (_isShuffled) GenerateShuffleOrder();
                PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler<PlaylistItem?>? CurrentItemChanged;
    public event EventHandler? PlaylistStateChanged;

    public PlaylistItem AddFile(string filePath, string? title = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var item = new PlaylistItem(filePath, title, duration);
        _items.Add(item);

        if (_isShuffled) GenerateShuffleOrder();
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public int AddFolder(string folderPath, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return 0;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            _items.Add(new PlaylistItem(file));
        }

        if (_isShuffled) GenerateShuffleOrder();
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
        return files.Count;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        var removedCurrent = (index == _currentIndex);
        _items.RemoveAt(index);

        if (_currentIndex > index)
        {
            _currentIndex--;
        }
        else if (removedCurrent)
        {
            _currentIndex = Math.Min(_currentIndex, _items.Count - 1);
            CurrentItemChanged?.Invoke(this, CurrentItem);
        }

        if (_isShuffled) GenerateShuffleOrder();
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(PlaylistItem item)
    {
        var idx = _items.IndexOf(item);
        if (idx >= 0) RemoveAt(idx);
    }

    public void Clear()
    {
        _items.Clear();
        _currentIndex = -1;
        _shuffleOrder.Clear();
        _shufflePointer = -1;
        CurrentItemChanged?.Invoke(this, null);
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _items.Count || toIndex < 0 || toIndex >= _items.Count || fromIndex == toIndex)
        {
            return;
        }

        var item = _items[fromIndex];
        _items.Move(fromIndex, toIndex);

        if (_currentIndex == fromIndex)
        {
            _currentIndex = toIndex;
        }
        else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
        {
            _currentIndex--;
        }
        else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
        {
            _currentIndex++;
        }

        if (_isShuffled) GenerateShuffleOrder();
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SetCurrentIndex(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        _currentIndex = index;
        CurrentItemChanged?.Invoke(this, CurrentItem);
        return true;
    }

    public bool Next()
    {
        if (_items.Count == 0) return false;

        if (_repeatMode == RepeatMode.RepeatOne && _currentIndex >= 0)
        {
            CurrentItemChanged?.Invoke(this, CurrentItem);
            return true;
        }

        if (_isShuffled)
        {
            if (_shuffleOrder.Count == 0) GenerateShuffleOrder();
            _shufflePointer++;
            if (_shufflePointer >= _shuffleOrder.Count)
            {
                if (_repeatMode == RepeatMode.RepeatAll)
                {
                    GenerateShuffleOrder();
                    _shufflePointer = 0;
                }
                else
                {
                    return false;
                }
            }

            _currentIndex = _shuffleOrder[_shufflePointer];
            CurrentItemChanged?.Invoke(this, CurrentItem);
            return true;
        }

        var nextIndex = _currentIndex + 1;
        if (nextIndex >= _items.Count)
        {
            if (_repeatMode == RepeatMode.RepeatAll)
            {
                nextIndex = 0;
            }
            else
            {
                return false;
            }
        }

        _currentIndex = nextIndex;
        CurrentItemChanged?.Invoke(this, CurrentItem);
        return true;
    }

    public bool Previous()
    {
        if (_items.Count == 0) return false;

        if (_isShuffled && _shuffleOrder.Count > 0)
        {
            _shufflePointer = Math.Max(0, _shufflePointer - 1);
            _currentIndex = _shuffleOrder[_shufflePointer];
            CurrentItemChanged?.Invoke(this, CurrentItem);
            return true;
        }

        var prevIndex = _currentIndex - 1;
        if (prevIndex < 0)
        {
            if (_repeatMode == RepeatMode.RepeatAll)
            {
                prevIndex = _items.Count - 1;
            }
            else
            {
                prevIndex = 0;
            }
        }

        _currentIndex = prevIndex;
        CurrentItemChanged?.Invoke(this, CurrentItem);
        return true;
    }

    private void GenerateShuffleOrder()
    {
        _shuffleOrder = Enumerable.Range(0, _items.Count).ToList();
        // Fisher-Yates shuffle
        for (var i = _shuffleOrder.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_shuffleOrder[i], _shuffleOrder[j]) = (_shuffleOrder[j], _shuffleOrder[i]);
        }
        _shufflePointer = _currentIndex >= 0 ? _shuffleOrder.IndexOf(_currentIndex) : -1;
    }

    // --- M3U / M3U8 Playlist Support ---

    public void LoadM3u(string m3uPath)
    {
        ArgumentNullException.ThrowIfNull(m3uPath);
        if (!File.Exists(m3uPath)) return;

        var baseDir = Path.GetDirectoryName(m3uPath) ?? string.Empty;
        var lines = File.ReadAllLines(m3uPath);
        string? nextTitle = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = line.IndexOf(',');
                if (commaIndex >= 0 && commaIndex < line.Length - 1)
                {
                    nextTitle = line[(commaIndex + 1)..].Trim();
                }
            }
            else if (!line.StartsWith('#'))
            {
                var fullPath = Path.IsPathRooted(line) ? line : Path.Combine(baseDir, line);
                if (File.Exists(fullPath) || Uri.TryCreate(fullPath, UriKind.Absolute, out _))
                {
                    _items.Add(new PlaylistItem(fullPath, nextTitle));
                }
                nextTitle = null;
            }
        }

        if (_isShuffled) GenerateShuffleOrder();
        PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveM3u(string m3uPath)
    {
        ArgumentNullException.ThrowIfNull(m3uPath);
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var item in _items)
        {
            var durSeconds = (int)item.Duration.TotalSeconds;
            sb.AppendLine($"#EXTINF:{durSeconds},{item.Title}");
            sb.AppendLine(item.FilePath);
        }

        File.WriteAllText(m3uPath, sb.ToString(), Encoding.UTF8);
    }

    // --- JSON Persistence ---

    public static string GetDefaultPlaylistFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "NJPlayer");
        return Path.Combine(folder, "playlist.json");
    }

    public void SaveToJson(string? filePath = null)
    {
        filePath ??= GetDefaultPlaylistFilePath();
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public void LoadFromJson(string? filePath = null)
    {
        filePath ??= GetDefaultPlaylistFilePath();
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var items = JsonSerializer.Deserialize<List<PlaylistItem>>(json);
                if (items != null)
                {
                    _items.Clear();
                    foreach (var itm in items.Where(i => File.Exists(i.FilePath) || Uri.TryCreate(i.FilePath, UriKind.Absolute, out _)))
                    {
                        _items.Add(itm);
                    }
                    PlaylistStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch { }
        }
    }
}
