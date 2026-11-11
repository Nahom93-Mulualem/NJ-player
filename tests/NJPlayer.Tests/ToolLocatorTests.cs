using System;
using System.IO;
using NJPlayer.Core.Tools;
using Xunit;

namespace NJPlayer.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void Locate_NonExistentTool_ReturnsNotFoundWithSearchedPaths()
    {
        var result = ExternalToolLocator.Locate("definitely_non_existent_tool_12345.exe");

        Assert.False(result.Found);
        Assert.Null(result.ExecutablePath);
        Assert.NotEmpty(result.SearchedPaths);
        Assert.Contains("definitely_non_existent_tool_12345.exe", result.StatusMessage);
    }

    [Fact]
    public void Locate_SearchesNextToExeAndLibs()
    {
        var result = ExternalToolLocator.Locate("yt-dlp.exe");

        Assert.NotNull(result);
        Assert.NotEmpty(result.SearchedPaths);
        // Verify path 1 is next to base directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        Assert.Equal(Path.Combine(baseDir, "yt-dlp.exe"), result.SearchedPaths[0]);
        // Verify path 2 is libs subfolder
        Assert.Equal(Path.Combine(baseDir, "libs", "yt-dlp.exe"), result.SearchedPaths[1]);
    }
}
