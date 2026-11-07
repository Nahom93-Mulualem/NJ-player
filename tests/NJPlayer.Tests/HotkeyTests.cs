using System.Linq;
using NJPlayer.Core.Hotkeys;
using Xunit;

namespace NJPlayer.Tests;

public class HotkeyTests
{
    [Fact]
    public void DefaultMap_MatchesDesignSpecHotkeys()
    {
        var manager = new HotkeyManager();

        Assert.True(manager.TryGetCommand("Space", out var cmd1));
        Assert.Equal(CommandId.PlayPause, cmd1);

        Assert.True(manager.TryGetCommand("M", out var cmd2));
        Assert.Equal(CommandId.Mute, cmd2);

        Assert.True(manager.TryGetCommand("Right", out var cmd3));
        Assert.Equal(CommandId.SeekForward5s, cmd3);

        Assert.True(manager.TryGetCommand("Shift+Right", out var cmd4));
        Assert.Equal(CommandId.SeekForward1s, cmd4);

        Assert.True(manager.TryGetCommand("Ctrl+Right", out var cmd5));
        Assert.Equal(CommandId.SeekForward30s, cmd5);

        Assert.True(manager.TryGetCommand("Up", out var cmd6));
        Assert.Equal(CommandId.VolumeUp5, cmd6);

        Assert.True(manager.TryGetCommand("Down", out var cmd7));
        Assert.Equal(CommandId.VolumeDown5, cmd7);

        Assert.True(manager.TryGetCommand("F", out var cmd8));
        Assert.Equal(CommandId.ToggleFullscreen, cmd8);

        Assert.True(manager.TryGetCommand("Ctrl+O", out var cmd9));
        Assert.Equal(CommandId.OpenFile, cmd9);
    }

    [Theory]
    [InlineData("ctrl+shift+s", "Ctrl+Shift+S")]
    [InlineData("shift+ctrl+o", "Ctrl+Shift+O")]
    [InlineData("ctrl+alt+p", "Ctrl+Alt+P")]
    [InlineData("space", "Space")]
    [InlineData("  Ctrl + O  ", "Ctrl+O")]
    public void NormalizeGesture_NormalizesModifiersAndCasing(string input, string expected)
    {
        var result = HotkeyManager.NormalizeGesture(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConflictDetection_DefaultMap_HasZeroConflicts()
    {
        var manager = new HotkeyManager();
        var conflicts = manager.DetectConflicts();
        Assert.Empty(conflicts);
    }

    [Fact]
    public void ConflictDetection_WhenDuplicateGestureAssigned_DetectsConflict()
    {
        var manager = new HotkeyManager();
        // Force conflict: rebind Mute to Space as well
        manager.Rebind(CommandId.Mute, "Space");

        var conflicts = manager.DetectConflicts();
        Assert.Single(conflicts);
        Assert.Equal("Space", conflicts[0].Gesture);
        Assert.Contains(CommandId.PlayPause, conflicts[0].Commands);
        Assert.Contains(CommandId.Mute, conflicts[0].Commands);
    }

    [Fact]
    public void JsonSerialization_SavesAndRestoresRebindsCorrectly()
    {
        var manager1 = new HotkeyManager();
        manager1.Rebind(CommandId.PlayPause, "Ctrl+P");
        var json = manager1.ToJson();

        var manager2 = new HotkeyManager();
        manager2.LoadFromJson(json);

        Assert.True(manager2.TryGetCommand("Ctrl+P", out var restoredCmd));
        Assert.Equal(CommandId.PlayPause, restoredCmd);
    }
}
