using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NJPlayer.Core.Settings;

/// <summary>
/// Manages JSON settings persistence with portable mode detection (portable.flag),
/// per-file resume position tracking, and recent files history.
/// </summary>
public sealed class SettingsManager
{
    private const int MaxRecentFiles = 15;
    private const int MaxResumeEntries = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _storageDirectory;
    private readonly bool _isPortable;
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;
    public bool IsPortable => _isPortable;
    public string StorageDirectory => _storageDirectory;

    public SettingsManager(string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;

        // Portable mode check: portable.flag next to executable
        var flagPath = Path.Combine(baseDirectory, "portable.flag");
        if (File.Exists(flagPath))
        {
            _isPortable = true;
            _storageDirectory = Path.Combine(baseDirectory, "data");
        }
        else
        {
            _isPortable = false;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _storageDirectory = Path.Combine(appData, "NJPlayer");
        }

        if (!Directory.Exists(_storageDirectory))
        {
            try { Directory.CreateDirectory(_storageDirectory); } catch { }
        }

        Load();
    }

    public string GetSettingsFilePath() => Path.Combine(_storageDirectory, "settings.json");

    public void Load()
    {
        var path = GetSettingsFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                _settings = new AppSettings();
            }
        }
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }

            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(GetSettingsFilePath(), json);
        }
        catch { }
    }

    // --- Resume Playback Per File ---

    public void SetResumePosition(string filePath, double positionSeconds, double totalDurationSeconds)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        // Only store resume position if past 5 seconds and not in final 5 seconds
        if (positionSeconds > 5.0 && (totalDurationSeconds <= 0 || positionSeconds < totalDurationSeconds - 5.0))
        {
            _settings.ResumePositions[filePath] = positionSeconds;

            // Trim dictionary if it exceeds max entries
            if (_settings.ResumePositions.Count > MaxResumeEntries)
            {
                var oldest = _settings.ResumePositions.Keys.First();
                _settings.ResumePositions.Remove(oldest);
            }
        }
        else
        {
            // If near beginning or ended, clear saved position so next playback starts clean
            _settings.ResumePositions.Remove(filePath);
        }
    }

    public double? GetResumePosition(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !_settings.ResumePlayback) return null;
        return _settings.ResumePositions.TryGetValue(filePath, out var pos) ? pos : null;
    }

    public void ClearResumePositions()
    {
        _settings.ResumePositions.Clear();
    }

    // --- Recent Files History ---

    public void AddRecentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        _settings.RecentFiles.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, filePath);

        if (_settings.RecentFiles.Count > MaxRecentFiles)
        {
            _settings.RecentFiles.RemoveRange(MaxRecentFiles, _settings.RecentFiles.Count - MaxRecentFiles);
        }
    }

    public void ClearRecentFiles()
    {
        _settings.RecentFiles.Clear();
    }

    // --- Window Geometry ---

    public void SaveWindowGeometry(double left, double top, double width, double height, bool isMaximized)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.IsWindowMaximized = isMaximized;
    }
}
