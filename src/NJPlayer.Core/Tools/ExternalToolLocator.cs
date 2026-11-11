using System;
using System.Collections.Generic;
using System.IO;
using NJPlayer.Core.Diagnostics;

namespace NJPlayer.Core.Tools;

/// <summary>
/// Result of an external tool lookup containing paths searched and status details.
/// </summary>
public sealed record ToolLookupResult(
    string ToolName,
    bool Found,
    string? ExecutablePath,
    IReadOnlyList<string> SearchedPaths,
    string StatusMessage);

/// <summary>
/// Unified external tool locator for yt-dlp, ffmpeg, and ffprobe.
/// Searches application root, libs folder, and system PATH, logging diagnostics per Issue 6.
/// </summary>
public static class ExternalToolLocator
{
    /// <summary>
    /// Locates an executable binary by searching:
    /// 1. Directory next to the executable
    /// 2. 'libs' folder next to the executable
    /// 3. Development 'libs' folder relative to base directory
    /// 4. Directories listed in system PATH
    /// </summary>
    public static ToolLookupResult Locate(string toolFileName)
    {
        ArgumentNullException.ThrowIfNull(toolFileName);

        var searched = new List<string>();
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Next to executable
        var pathNextToExe = Path.Combine(baseDir, toolFileName);
        searched.Add(pathNextToExe);
        if (File.Exists(pathNextToExe))
        {
            var fullPath = Path.GetFullPath(pathNextToExe);
            LogService.LogInfo($"[ToolLocator] Located '{toolFileName}' next to executable at: {fullPath}");
            return new ToolLookupResult(toolFileName, true, fullPath, searched, $"Found next to application at {fullPath}");
        }

        // 2. 'libs' subfolder next to executable
        var pathLibsSubfolder = Path.Combine(baseDir, "libs", toolFileName);
        searched.Add(pathLibsSubfolder);
        if (File.Exists(pathLibsSubfolder))
        {
            var fullPath = Path.GetFullPath(pathLibsSubfolder);
            LogService.LogInfo($"[ToolLocator] Located '{toolFileName}' in libs subfolder at: {fullPath}");
            return new ToolLookupResult(toolFileName, true, fullPath, searched, $"Found in libs folder at {fullPath}");
        }

        // 3. Solution root 'libs' folder (development / test runner environment)
        var devLibsDir = Path.Combine(baseDir, "..", "..", "..", "libs", toolFileName);
        var devLibsFull = Path.GetFullPath(devLibsDir);
        searched.Add(devLibsFull);
        if (File.Exists(devLibsDir))
        {
            LogService.LogInfo($"[ToolLocator] Located '{toolFileName}' in solution libs at: {devLibsFull}");
            return new ToolLookupResult(toolFileName, true, devLibsFull, searched, $"Found in solution libs folder at {devLibsFull}");
        }

        // 4. System PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var parts = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                try
                {
                    var candidate = Path.Combine(part, toolFileName);
                    if (!searched.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        searched.Add(candidate);
                    }

                    if (File.Exists(candidate))
                    {
                        var fullPath = Path.GetFullPath(candidate);
                        LogService.LogInfo($"[ToolLocator] Located '{toolFileName}' in system PATH at: {fullPath}");
                        return new ToolLookupResult(toolFileName, true, fullPath, searched, $"Found in system PATH at {fullPath}");
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries
                }
            }
        }

        // Tool was not found in any standard location
        var failureReason = $"'{toolFileName}' was not found after checking {searched.Count} locations (application root, libs directory, and system PATH).";
        LogService.LogWarning($"[ToolLocator] {failureReason} Searched: {string.Join("; ", searched)}");

        return new ToolLookupResult(
            toolFileName,
            false,
            null,
            searched,
            failureReason);
    }
}
