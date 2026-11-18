using System;
using System.Text.RegularExpressions;

namespace NJPlayer.Core.Playlist;

public enum PlaylistSearchScope
{
    TitleOnly = 0,
    TitleAndPath = 1,
    DirectoryOnly = 2
}

public enum PlaylistSearchMode
{
    Contains = 0,
    ExactMatch = 1,
    StartsWith = 2,
    Regex = 3
}

/// <summary>
/// Configurable options and evaluation logic for playlist searching.
/// </summary>
public sealed class PlaylistSearchOptions
{
    public PlaylistSearchScope Scope { get; set; } = PlaylistSearchScope.TitleOnly;
    public PlaylistSearchMode Mode { get; set; } = PlaylistSearchMode.Contains;
    public bool CaseSensitive { get; set; } = false;

    public bool Matches(PlaylistItem item, string query)
    {
        if (item == null) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;

        var target = Scope switch
        {
            PlaylistSearchScope.TitleOnly => item.Title ?? string.Empty,
            PlaylistSearchScope.TitleAndPath => $"{item.Title} {item.FilePath}",
            PlaylistSearchScope.DirectoryOnly => System.IO.Path.GetDirectoryName(item.FilePath) ?? string.Empty,
            _ => item.Title ?? string.Empty
        };

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return Mode switch
        {
            PlaylistSearchMode.Contains => target.Contains(query, comparison),
            PlaylistSearchMode.StartsWith => target.StartsWith(query, comparison),
            PlaylistSearchMode.ExactMatch => string.Equals(target, query, comparison) ||
                                            string.Equals(item.Title, query, comparison),
            PlaylistSearchMode.Regex => MatchesRegex(target, query, CaseSensitive),
            _ => target.Contains(query, comparison)
        };
    }

    private static bool MatchesRegex(string target, string pattern, bool caseSensitive)
    {
        try
        {
            var options = RegexOptions.None;
            if (!caseSensitive) options |= RegexOptions.IgnoreCase;
            return Regex.IsMatch(target, pattern, options, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            // If regex is partially typed or invalid, gracefully return false rather than crashing
            return false;
        }
    }
}
