using System;

namespace NJPlayer.Core.Hotkeys;

/// <summary>
/// Represents a key gesture bound to a player CommandId.
/// </summary>
public sealed class HotkeyBinding
{
    public CommandId Command { get; set; }
    public string Gesture { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";

    public HotkeyBinding() { }

    public HotkeyBinding(CommandId command, string gesture, string name, string category = "General")
    {
        Command = command;
        Gesture = gesture ?? string.Empty;
        Name = name ?? string.Empty;
        Category = category ?? "General";
    }

    public override string ToString() => $"{Name} ({Gesture}) -> {Command}";
}
