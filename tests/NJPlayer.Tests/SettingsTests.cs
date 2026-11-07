using System;
using System.IO;
using NJPlayer.Core.Settings;
using Xunit;

namespace NJPlayer.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _testDir;

    public SettingsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NJPlayer_SettingsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void SettingsManager_Detects_PortableMode_WhenFlagExists()
    {
        var flagPath = Path.Combine(_testDir, "portable.flag");
        File.WriteAllText(flagPath, "");

        var manager = new SettingsManager(_testDir);

        Assert.True(manager.IsPortable);
        Assert.Equal(Path.Combine(_testDir, "data"), manager.StorageDirectory);
    }

    [Fact]
    public void SettingsManager_UsesAppData_WhenFlagAbsent()
    {
        var manager = new SettingsManager(_testDir);

        Assert.False(manager.IsPortable);
        var expectedAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NJPlayer");
        Assert.Equal(expectedAppData, manager.StorageDirectory);
    }

    [Fact]
    public void SettingsManager_SavesAndLoads_SettingsCorrectly()
    {
        File.WriteAllText(Path.Combine(_testDir, "portable.flag"), "");

        var manager1 = new SettingsManager(_testDir);
        manager1.Settings.ResumePlayback = true;
        manager1.Settings.HardwareDecoder = "nvdec";
        manager1.Settings.WindowWidth = 1280;
        manager1.Settings.WindowHeight = 720;
        manager1.Save();

        var manager2 = new SettingsManager(_testDir);
        Assert.True(manager2.Settings.ResumePlayback);
        Assert.Equal("nvdec", manager2.Settings.HardwareDecoder);
        Assert.Equal(1280, manager2.Settings.WindowWidth);
        Assert.Equal(720, manager2.Settings.WindowHeight);
    }

    [Fact]
    public void SettingsManager_SetResumePosition_Enforces5SecondRules()
    {
        File.WriteAllText(Path.Combine(_testDir, "portable.flag"), "");
        var manager = new SettingsManager(_testDir);
        var testFile = Path.Combine(_testDir, "movie.mp4");

        // Positions <= 5 seconds should not be saved
        manager.SetResumePosition(testFile, 4.5, 120.0);
        Assert.Null(manager.GetResumePosition(testFile));

        // Positions in final 5 seconds should not be saved
        manager.SetResumePosition(testFile, 116.0, 120.0);
        Assert.Null(manager.GetResumePosition(testFile));

        // Valid middle position should be saved
        manager.SetResumePosition(testFile, 42.0, 120.0);
        Assert.Equal(42.0, manager.GetResumePosition(testFile));

        // If ResumePlayback is disabled, GetResumePosition returns null
        manager.Settings.ResumePlayback = false;
        Assert.Null(manager.GetResumePosition(testFile));
    }

    [Fact]
    public void SettingsManager_AddRecentFile_DedupesAndMaintainsOrder()
    {
        File.WriteAllText(Path.Combine(_testDir, "portable.flag"), "");
        var manager = new SettingsManager(_testDir);

        var fileA = Path.Combine(_testDir, "a.mp4");
        var fileB = Path.Combine(_testDir, "b.mp4");
        var fileC = Path.Combine(_testDir, "c.mp4");

        File.WriteAllText(fileA, "");
        File.WriteAllText(fileB, "");
        File.WriteAllText(fileC, "");

        manager.AddRecentFile(fileA);
        manager.AddRecentFile(fileB);
        manager.AddRecentFile(fileC);

        Assert.Equal(fileC, manager.Settings.RecentFiles[0]);
        Assert.Equal(fileB, manager.Settings.RecentFiles[1]);
        Assert.Equal(fileA, manager.Settings.RecentFiles[2]);

        // Re-adding A moves it to the top
        manager.AddRecentFile(fileA);
        Assert.Equal(fileA, manager.Settings.RecentFiles[0]);
        Assert.Equal(fileC, manager.Settings.RecentFiles[1]);
        Assert.Equal(fileB, manager.Settings.RecentFiles[2]);
        Assert.Equal(3, manager.Settings.RecentFiles.Count);
    }
}

