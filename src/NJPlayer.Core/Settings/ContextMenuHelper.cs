using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NJPlayer.Core.Settings;

/// <summary>
/// Helper to manage Windows Explorer right-click shell context menu registration under HKCU.
/// Does not require administrative elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ContextMenuHelper
{
    private const string ShellKeyPath = @"Software\Classes\*\shell\NJPlayer.Trim";
    private const string MenuText = "Trim with NJ Player";

    /// <summary>
    /// Checks whether the "Trim with NJ Player" context menu is currently registered in HKCU.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellKeyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers "Trim with NJ Player" under HKCU\Software\Classes\*\shell\NJPlayer.Trim.
    /// </summary>
    public static bool Register(string? exePath = null)
    {
        try
        {
            exePath ??= Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NJPlayer.App.exe");
            }

            using var key = Registry.CurrentUser.CreateSubKey(ShellKeyPath);
            if (key == null) return false;

            key.SetValue(string.Empty, MenuText);
            key.SetValue("Icon", $"\"{exePath}\",0");

            using var cmdKey = key.CreateSubKey("command");
            if (cmdKey == null) return false;

            cmdKey.SetValue(string.Empty, $"\"{exePath}\" --trim \"%1\"");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.LogService.LogError("ContextMenuHelper.Register", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes the context menu entry from HKCU.
    /// </summary>
    public static bool Unregister()
    {
        try
        {
            using var shellKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell", true);
            if (shellKey != null)
            {
                shellKey.DeleteSubKeyTree("NJPlayer.Trim", false);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Diagnostics.LogService.LogError("ContextMenuHelper.Unregister", ex);
            return false;
        }
    }
}
