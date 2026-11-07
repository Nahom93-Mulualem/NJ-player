using System;
using System.Collections.Generic;
using NJPlayer.Core.Interop;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Builder for initial options passed to libmpv before calling mpv_initialize.
/// </summary>
public sealed class MpvOptionBuilder
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public MpvOptionBuilder()
    {
        // Safe defaults for NJ Player
        WithKeepOpen(true);
        WithIdle(true);
        WithTerminal(false);
        WithHardwareDecoding("auto-safe");
    }

    public MpvOptionBuilder WithOption(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _options[name] = value;
        return this;
    }

    public MpvOptionBuilder WithHardwareDecoding(string hwdec = "auto-safe")
    {
        return WithOption("hwdec", hwdec);
    }

    public MpvOptionBuilder WithKeepOpen(bool keepOpen = true)
    {
        return WithOption("keep-open", keepOpen ? "yes" : "no");
    }

    public MpvOptionBuilder WithIdle(bool idle = true)
    {
        return WithOption("idle", idle ? "yes" : "no");
    }

    public MpvOptionBuilder WithTerminal(bool terminal = false)
    {
        return WithOption("terminal", terminal ? "yes" : "no");
    }

    public MpvOptionBuilder WithWindowHandle(IntPtr wid)
    {
        return WithOption("wid", wid.ToInt64().ToString());
    }

    public IReadOnlyDictionary<string, string> Build()
    {
        return new Dictionary<string, string>(_options, StringComparer.OrdinalIgnoreCase);
    }

    public void Apply(MpvContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var (key, value) in _options)
        {
            context.SetOptionString(key, value);
        }
    }
}

