using System;
using System.Collections.Generic;
using System.Text;

namespace NJPlayer.Core.Settings;

/// <summary>
/// Helper class providing Windows Registry file association code and Inno Setup configuration.
/// Per AGENTS.md and Prompt 8 rules, this class does NOT register associations automatically without explicit user confirmation.
/// </summary>
public static class FileAssociationHelper
{
    public static readonly IReadOnlyList<string> SupportedVideoExtensions = new[]
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".ts", ".m4v"
    };

    public static readonly IReadOnlyList<string> SupportedAudioExtensions = new[]
    {
        ".mp3", ".flac", ".wav", ".m4a", ".opus", ".ogg", ".aac"
    };

    /// <summary>
    /// Generates the Inno Setup [Registry] and [Root] script section to register file associations safely at installer time.
    /// </summary>
    public static string GenerateInnoSetupScriptSnippet(string appExecutableName = "NJPlayer.App.exe")
    {
        var sb = new StringBuilder();
        sb.AppendLine("; ===========================================================================");
        sb.AppendLine("; NJ Player - Inno Setup File Association Definitions (Run at install time)");
        sb.AppendLine("; ===========================================================================");
        sb.AppendLine("[Registry]");

        // ProgID definition
        sb.AppendLine($"Root: HKA; Subkey: \"Software\\Classes\\NJPlayer.Media\"; ValueType: string; ValueData: \"NJ Player Media File\"; Flags: uninsdeletekey");
        sb.AppendLine($"Root: HKA; Subkey: \"Software\\Classes\\NJPlayer.Media\\DefaultIcon\"; ValueType: string; ValueData: \"{{app}}\\{appExecutableName},0\"");
        sb.AppendLine($"Root: HKA; Subkey: \"Software\\Classes\\NJPlayer.Media\\shell\\open\\command\"; ValueType: string; ValueData: \"\"\"{{app}}\\{appExecutableName}\"\" \"\"%1\"\"\"");

        // Extension mappings
        foreach (var ext in SupportedVideoExtensions)
        {
            sb.AppendLine($"Root: HKA; Subkey: \"Software\\Classes\\{ext}\\OpenWithProgids\"; ValueType: none; ValueName: \"NJPlayer.Media\"; Flags: uninsdeletevalue");
        }
        foreach (var ext in SupportedAudioExtensions)
        {
            sb.AppendLine($"Root: HKA; Subkey: \"Software\\Classes\\{ext}\\OpenWithProgids\"; ValueType: none; ValueName: \"NJPlayer.Media\"; Flags: uninsdeletevalue");
        }

        return sb.ToString();
    }
}

