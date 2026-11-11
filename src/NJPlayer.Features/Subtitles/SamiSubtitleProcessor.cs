using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NJPlayer.Features.Subtitles;

public enum RubyDisplayMode
{
    /// <summary>
    /// Parenthesized ruby annotation: base(ruby)
    /// </summary>
    Parentheses,

    /// <summary>
    /// Keep only base text, discarding ruby pronunciation
    /// </summary>
    BaseOnly,

    /// <summary>
    /// Keep both base and ruby as ASS superscript annotation
    /// </summary>
    AssAnnotation
}

/// <summary>
/// Processor for SMI/SAMI subtitles supporting sync timestamps, multi-language classes,
/// and HTML5/SMI Ruby pronunciation tags (&lt;ruby&gt;...&lt;rt&gt;...&lt;/rt&gt;&lt;/ruby&gt;).
/// </summary>
public static class SamiSubtitleProcessor
{
    private static readonly Regex RubyRegex = new(
        @"<ruby[^>]*>(.*?)<rt[^>]*>(.*?)</rt[^>]*>.*?</ruby>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SyncRegex = new(
        @"<SYNC\s+Start\s*=\s*([0-9]+)[^>]*>(.*?)(?=<SYNC|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    /// <summary>
    /// Checks whether the text contains SAMI / HTML Ruby pronunciation tags.
    /// </summary>
    public static bool HasRubyTags(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        return RubyRegex.IsMatch(content);
    }

    /// <summary>
    /// Processes Ruby pronunciation tags according to chosen display mode.
    /// </summary>
    public static string ProcessRubyTags(string content, RubyDisplayMode mode = RubyDisplayMode.Parentheses)
    {
        if (string.IsNullOrEmpty(content) || !HasRubyTags(content)) return content;

        return RubyRegex.Replace(content, match =>
        {
            var baseText = CleanHtml(match.Groups[1].Value).Trim();
            var rubyText = CleanHtml(match.Groups[2].Value).Trim();

            return mode switch
            {
                RubyDisplayMode.BaseOnly => baseText,
                RubyDisplayMode.AssAnnotation => $"{baseText}{{\\fs18\\an8}}{rubyText}{{\\r}}",
                _ => string.IsNullOrEmpty(rubyText) ? baseText : $"{baseText}({rubyText})"
            };
        });
    }

    /// <summary>
    /// Cleans HTML entities and tags while preserving newlines.
    /// </summary>
    public static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var text = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

        text = HtmlTagRegex.Replace(text, string.Empty);
        return text.Trim();
    }

    /// <summary>
    /// Normalizes an SMI/SAMI subtitle file by processing Ruby tags and saving a UTF-8 copy.
    /// </summary>
    public static string NormalizeSamiToUtf8(string smiFilePath, RubyDisplayMode mode = RubyDisplayMode.Parentheses)
    {
        ArgumentNullException.ThrowIfNull(smiFilePath);
        if (!File.Exists(smiFilePath)) return smiFilePath;

        var encoding = SubtitleService.DetectEncoding(smiFilePath);
        var rawContent = File.ReadAllText(smiFilePath, encoding);

        if (!HasRubyTags(rawContent))
        {
            return smiFilePath;
        }

        var processed = ProcessRubyTags(rawContent, mode);

        var tempDir = Path.Combine(Path.GetTempPath(), "NJPlayer", "Subtitles");
        Directory.CreateDirectory(tempDir);
        var normalizedPath = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(smiFilePath)}_normalized.smi");

        File.WriteAllText(normalizedPath, processed, Encoding.UTF8);
        return normalizedPath;
    }
}

