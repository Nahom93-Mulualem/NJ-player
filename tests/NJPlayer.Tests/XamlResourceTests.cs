using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NJPlayer.Tests;

public class XamlResourceTests
{
    [Fact]
    public void AllStaticResources_InAppXaml_MustResolveToDefinedKeys()
    {
        var baseDir = AppContext.BaseDirectory;
        // Search upward to find src/NJPlayer.App
        var dir = new DirectoryInfo(baseDir);
        string? appDir = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NJPlayer.App");
            if (Directory.Exists(candidate))
            {
                appDir = candidate;
                break;
            }
            dir = dir.Parent;
        }

        Assert.NotNull(appDir);

        // 1. Collect all keys defined in Themes/*.xaml
        var themeDir = Path.Combine(appDir, "Themes");
        var definedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var themeFile in Directory.GetFiles(themeDir, "*.xaml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(themeFile);
            var keyMatches = Regex.Matches(content, @"x:Key=""([^""]+)""");
            foreach (Match m in keyMatches)
            {
                definedKeys.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(definedKeys);

        // 2. Scan every XAML file in src/NJPlayer.App
        var missingResources = new List<string>();
        foreach (var xamlFile in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(xamlFile);

            var localKeys = new HashSet<string>(definedKeys, StringComparer.Ordinal);
            var localKeyMatches = Regex.Matches(content, @"x:Key=""([^""]+)""");
            foreach (Match m in localKeyMatches)
            {
                localKeys.Add(m.Groups[1].Value);
            }

            var refMatches = Regex.Matches(content, @"\{StaticResource\s+([^}]+)\}");
            foreach (Match rm in refMatches)
            {
                var resName = rm.Groups[1].Value.Trim();
                if (resName.StartsWith("{") || resName.Contains("x:Static")) continue;

                if (!localKeys.Contains(resName))
                {
                    missingResources.Add($"{Path.GetFileName(xamlFile)}: Missing resource '{resName}'");
                }
            }
        }

        Assert.Empty(missingResources);
    }
}
