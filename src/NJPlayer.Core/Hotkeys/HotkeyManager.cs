using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NJPlayer.Core.Hotkeys;

public sealed class HotkeyConflict
{
    public string Gesture { get; }
    public IReadOnlyList<CommandId> Commands { get; }

    public HotkeyConflict(string gesture, IEnumerable<CommandId> commands)
    {
        Gesture = gesture;
        Commands = commands.ToList();
    }
}

/// <summary>
/// Data-driven hotkey manager handling key-to-command mapping, hotkeys.json persistence,
/// conflict detection, and normalized gesture lookups.
/// </summary>
public sealed class HotkeyManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<CommandId, HotkeyBinding> _bindings = new();

    public IReadOnlyDictionary<CommandId, HotkeyBinding> Bindings => _bindings;

    public HotkeyManager()
    {
        ResetToDefaults();
    }

    /// <summary>
    /// Resets all hotkey bindings to the exact defaults specified in DESIGN.md section 10.
    /// </summary>
    public void ResetToDefaults()
    {
        _bindings.Clear();

        // Playback
        AddBinding(CommandId.PlayPause, "Space", "Play / Pause", "Playback");
        AddBinding(CommandId.Mute, "M", "Mute", "Audio");
        AddBinding(CommandId.VolumeUp5, "Up", "Volume +5", "Audio");
        AddBinding(CommandId.VolumeDown5, "Down", "Volume -5", "Audio");

        // Seeking
        AddBinding(CommandId.SeekForward5s, "Right", "Seek +5 s", "Seeking");
        AddBinding(CommandId.SeekBackward5s, "Left", "Seek -5 s", "Seeking");
        AddBinding(CommandId.SeekForward1s, "Shift+Right", "Seek +1 s", "Seeking");
        AddBinding(CommandId.SeekBackward1s, "Shift+Left", "Seek -1 s", "Seeking");
        AddBinding(CommandId.SeekForward30s, "Ctrl+Right", "Seek +30 s", "Seeking");
        AddBinding(CommandId.SeekBackward30s, "Ctrl+Left", "Seek -30 s", "Seeking");

        // Navigation & Chapters
        AddBinding(CommandId.NextChapter, "Alt+Right", "Next Chapter", "Navigation");
        AddBinding(CommandId.PrevChapter, "Alt+Left", "Previous Chapter", "Navigation");
        AddBinding(CommandId.NextFile, "N", "Next File", "Navigation");
        AddBinding(CommandId.PrevFile, "P", "Previous File", "Navigation");
        AddBinding(CommandId.JumpToStart, "Home", "Jump to Start", "Navigation");
        AddBinding(CommandId.JumpToEnd, "End", "Jump to End", "Navigation");
        AddBinding(CommandId.FrameStepForward, "OemPeriod", "Frame Step Forward", "Playback");
        AddBinding(CommandId.FrameStepBackward, "OemComma", "Frame Step Backward", "Playback");

        // Speed
        AddBinding(CommandId.SpeedUp, "OemCloseBrackets", "Speed +0.1x", "Playback");
        AddBinding(CommandId.SpeedDown, "OemOpenBrackets", "Speed -0.1x", "Playback");
        AddBinding(CommandId.ResetSpeed, "OemBackslash", "Reset Speed", "Playback");

        // Subtitles & Audio Tracks
        AddBinding(CommandId.SubtitleDelayPlus, "Z", "Subtitle Delay +100 ms", "Subtitles");
        AddBinding(CommandId.SubtitleDelayMinus, "X", "Subtitle Delay -100 ms", "Subtitles");
        AddBinding(CommandId.ToggleSubtitles, "V", "Toggle Subtitles", "Subtitles");
        AddBinding(CommandId.CycleAudioTrack, "A", "Cycle Audio Track", "Audio");
        AddBinding(CommandId.SubtitleDownload, "Ctrl+Shift+S", "Download Subtitles", "Subtitles");

        // Window & UI
        AddBinding(CommandId.ToggleFullscreen, "F", "Fullscreen", "Window");
        AddBinding(CommandId.ExitFullscreen, "Escape", "Exit Fullscreen", "Window");
        AddBinding(CommandId.ToggleAlwaysOnTop, "T", "Always on Top", "Window");
        AddBinding(CommandId.OpenFile, "Ctrl+O", "Open File", "File");
        AddBinding(CommandId.OpenUrl, "Ctrl+U", "Open URL", "File");
        AddBinding(CommandId.TogglePlaylist, "Ctrl+L", "Playlist", "Window");
        AddBinding(CommandId.ToggleEqualizer, "E", "Equalizer", "Audio");
        AddBinding(CommandId.MediaInfo, "I", "Media Info", "Window");
        AddBinding(CommandId.Screenshot, "S", "Screenshot", "File");

        // Jump to 0-90%
        AddBinding(CommandId.JumpTo0Percent, "D0", "Jump to 0%", "Seeking");
        AddBinding(CommandId.JumpTo10Percent, "D1", "Jump to 10%", "Seeking");
        AddBinding(CommandId.JumpTo20Percent, "D2", "Jump to 20%", "Seeking");
        AddBinding(CommandId.JumpTo30Percent, "D3", "Jump to 30%", "Seeking");
        AddBinding(CommandId.JumpTo40Percent, "D4", "Jump to 40%", "Seeking");
        AddBinding(CommandId.JumpTo50Percent, "D5", "Jump to 50%", "Seeking");
        AddBinding(CommandId.JumpTo60Percent, "D6", "Jump to 60%", "Seeking");
        AddBinding(CommandId.JumpTo70Percent, "D7", "Jump to 70%", "Seeking");
        AddBinding(CommandId.JumpTo80Percent, "D8", "Jump to 80%", "Seeking");
        AddBinding(CommandId.JumpTo90Percent, "D9", "Jump to 90%", "Seeking");
    }

    private void AddBinding(CommandId command, string gesture, string name, string category)
    {
        _bindings[command] = new HotkeyBinding(command, NormalizeGesture(gesture), name, category);
    }

    public void Rebind(CommandId command, string newGesture)
    {
        if (_bindings.TryGetValue(command, out var binding))
        {
            binding.Gesture = NormalizeGesture(newGesture);
        }
        else
        {
            _bindings[command] = new HotkeyBinding(command, NormalizeGesture(newGesture), command.ToString());
        }
    }

    public bool TryGetCommand(string gesture, out CommandId command)
    {
        var normalized = NormalizeGesture(gesture);
        foreach (var (_, binding) in _bindings)
        {
            if (string.Equals(binding.Gesture, normalized, StringComparison.OrdinalIgnoreCase))
            {
                command = binding.Command;
                return true;
            }
        }

        command = CommandId.None;
        return false;
    }

    public IReadOnlyList<HotkeyConflict> DetectConflicts()
    {
        return _bindings.Values
            .Where(b => !string.IsNullOrWhiteSpace(b.Gesture))
            .GroupBy(b => b.Gesture, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new HotkeyConflict(g.Key, g.Select(x => x.Command)))
            .ToList();
    }

    public static string NormalizeGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasCtrl = false;
        var hasShift = false;
        var hasAlt = false;
        string key = string.Empty;

        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL") hasCtrl = true;
            else if (upper is "SHIFT") hasShift = true;
            else if (upper is "ALT") hasAlt = true;
            else key = part;
        }

        var modifiers = new List<string>();
        if (hasCtrl) modifiers.Add("Ctrl");
        if (hasAlt) modifiers.Add("Alt");
        if (hasShift) modifiers.Add("Shift");

        if (!string.IsNullOrEmpty(key))
        {
            if (key.Length == 1)
            {
                modifiers.Add(key.ToUpperInvariant());
            }
            else
            {
                modifiers.Add(char.ToUpperInvariant(key[0]) + key[1..]);
            }
        }

        return string.Join("+", modifiers);
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(_bindings.Values, JsonOptions);
    }

    public void LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var list = JsonSerializer.Deserialize<List<HotkeyBinding>>(json, JsonOptions);
        if (list == null) return;

        foreach (var item in list)
        {
            if (item.Command != CommandId.None)
            {
                Rebind(item.Command, item.Gesture);
            }
        }
    }

    public static string GetDefaultHotkeysFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "NJPlayer");
        return Path.Combine(folder, "hotkeys.json");
    }

    public void SaveToFile(string? filePath = null)
    {
        filePath ??= GetDefaultHotkeysFilePath();
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(filePath, ToJson());
    }

    public void LoadFromFile(string? filePath = null)
    {
        filePath ??= GetDefaultHotkeysFilePath();
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            LoadFromJson(json);
        }
    }
}
