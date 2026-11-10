using System;
using System.Runtime.InteropServices;
using System.Threading;
using NJPlayer.Core.Interop;

namespace NJPlayer.Core.Playback;

/// <summary>
/// High-level player engine wrapping libmpv with thread synchronization, property observation,
/// and a background event loop raising standard .NET events.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private const ulong PropReplyTimePos = 1;
    private const ulong PropReplyDuration = 2;
    private const ulong PropReplyPause = 3;
    private const ulong PropReplyVolume = 4;
    private const ulong PropReplySpeed = 5;
    private const ulong PropReplyTrackList = 6;
    private const ulong PropReplyMediaTitle = 7;

    private readonly object _stateLock = new();
    private readonly AutoResetEvent _wakeEvent = new(false);

    private MpvContext? _context;
    private Thread? _eventThread;
    private MpvWakeupCallback? _wakeupCallback; // Prevent GC collection of unmanaged delegate
    private volatile bool _disposed;
    private volatile bool _isInitialized;

    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isPaused = true;
    private double _volume = 100.0;
    private double _speed = 1.0;

    public event EventHandler<TimeSpan>? TimePositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<bool>? PauseChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<double>? SpeedChanged;
    public event EventHandler? TrackListChanged;
    public event EventHandler<string>? MediaTitleChanged;
    public event EventHandler<EndFileEventArgs>? EndFile;
    public event EventHandler<MpvErrorEventArgs>? Error;

    public bool IsInitialized => _isInitialized;
    public TimeSpan Position => _position;
    public TimeSpan Duration => _duration;
    public bool IsPaused => _isPaused;
    public double Volume => _volume;
    public double Speed => _speed;
    public string? HardwareDecoder => _context?.GetPropertyString("hwdec-current");
    public MpvContext? Context => _context;

    public void Initialize(Action<MpvOptionBuilder>? configureOptions = null)
    {
        if (_isInitialized) return;

        var builder = new MpvOptionBuilder();
        configureOptions?.Invoke(builder);

        _context = MpvContext.Create();
        builder.Apply(_context);

        // Register wakeup callback before initialize so early events are captured
        _wakeupCallback = _ => _wakeEvent.Set();
        _context.SetWakeupCallback(_wakeupCallback, IntPtr.Zero);

        _context.Initialize();

        // Register observed properties
        _context.ObserveProperty(PropReplyTimePos, "time-pos", MpvFormat.Double);
        _context.ObserveProperty(PropReplyDuration, "duration", MpvFormat.Double);
        _context.ObserveProperty(PropReplyPause, "pause", MpvFormat.Flag);
        _context.ObserveProperty(PropReplyVolume, "volume", MpvFormat.Double);
        _context.ObserveProperty(PropReplySpeed, "speed", MpvFormat.Double);
        _context.ObserveProperty(PropReplyTrackList, "track-list", MpvFormat.None);
        _context.ObserveProperty(PropReplyMediaTitle, "media-title", MpvFormat.String);

        _isInitialized = true;

        // Launch dedicated background event-loop thread
        _eventThread = new Thread(EventLoop)
        {
            Name = "NJPlayer.MpvEventLoop",
            IsBackground = true
        };
        _eventThread.Start();
    }

    public void Load(string filePath, bool startPaused = false)
    {
        EnsureInitialized();
        var commandArgs = MpvEventMapper.BuildLoadFileCommand(filePath, startPaused);
        _context!.Command(commandArgs);
    }

    public void Play()
    {
        EnsureInitialized();
        try
        {
            _context!.SetPropertyFlag("pause", false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] Play failed: {ex.Message}");
        }
    }

    public void Pause()
    {
        EnsureInitialized();
        try
        {
            _context!.SetPropertyFlag("pause", true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] Pause failed: {ex.Message}");
        }
    }

    public void TogglePause()
    {
        EnsureInitialized();
        if (Duration <= TimeSpan.Zero) return;
        try
        {
            _context!.Command("cycle", "pause");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] TogglePause failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        EnsureInitialized();
        if (Duration <= TimeSpan.Zero) return;
        try
        {
            _context!.Command("stop");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] Stop failed: {ex.Message}");
        }
    }

    public void Seek(double seconds, bool exact = false)
    {
        EnsureInitialized();
        if (Duration <= TimeSpan.Zero) return;
        try
        {
            var commandArgs = MpvEventMapper.BuildSeekCommand(seconds, exact);
            _context!.Command(commandArgs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] Seek failed: {ex.Message}");
        }
    }

    public void SeekRelative(double offsetSeconds, bool exact = false)
    {
        EnsureInitialized();
        if (Duration <= TimeSpan.Zero) return;
        try
        {
            var commandArgs = MpvEventMapper.BuildSeekRelativeCommand(offsetSeconds, exact);
            _context!.Command(commandArgs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SeekRelative failed: {ex.Message}");
        }
    }

    public void SetVolume(double volume)
    {
        EnsureInitialized();
        volume = Math.Clamp(volume, 0.0, 150.0);
        try
        {
            _context!.SetPropertyDouble("volume", volume);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SetVolume failed: {ex.Message}");
        }
    }

    public void SetSpeed(double speed)
    {
        EnsureInitialized();
        speed = Math.Clamp(speed, 0.01, 100.0);
        try
        {
            _context!.SetPropertyDouble("speed", speed);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SetSpeed failed: {ex.Message}");
        }
    }

    public void SetPropertyString(string name, string value)
    {
        EnsureInitialized();
        try
        {
            _context!.SetPropertyString(name, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SetPropertyString('{name}') failed: {ex.Message}");
        }
    }

    public void SetPropertyDouble(string name, double value)
    {
        EnsureInitialized();
        try
        {
            _context!.SetPropertyDouble(name, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SetPropertyDouble('{name}') failed: {ex.Message}");
        }
    }

    public void SetPropertyFlag(string name, bool value)
    {
        EnsureInitialized();
        try
        {
            _context!.SetPropertyFlag(name, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] SetPropertyFlag('{name}') failed: {ex.Message}");
        }
    }

    public string? GetPropertyString(string name)
    {
        if (!_isInitialized || _context == null) return null;
        try
        {
            return _context.GetPropertyString(name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] GetPropertyString('{name}') failed: {ex.Message}");
            return null;
        }
    }

    public double? GetPropertyDouble(string name)
    {
        if (!_isInitialized || _context == null) return null;
        try
        {
            return _context.GetPropertyDouble(name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] GetPropertyDouble('{name}') failed: {ex.Message}");
            return null;
        }
    }

    public bool? GetPropertyFlag(string name)
    {
        if (!_isInitialized || _context == null) return null;
        try
        {
            return _context.GetPropertyFlag(name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] GetPropertyFlag('{name}') failed: {ex.Message}");
            return null;
        }
    }

    public void AddSubtitle(string filePath, bool select = true)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (Duration <= TimeSpan.Zero) return;
        Command("sub-add", filePath, select ? "select" : "auto");
    }

    public void SetSubtitleDelay(double delaySeconds)
    {
        SetPropertyDouble("sub-delay", delaySeconds);
    }

    public void SetSubtitleFontSize(double size)
    {
        SetPropertyDouble("sub-font-size", size);
    }

    public void SetSubtitlePosition(double position)
    {
        SetPropertyDouble("sub-pos", position);
    }

    public void ToggleSubtitles()
    {
        if (Duration <= TimeSpan.Zero) return;
        Command("cycle", "sub-visibility");
    }

    public void CycleSubtitles()
    {
        if (Duration <= TimeSpan.Zero) return;
        Command("cycle", "sub");
    }

    public void Command(params string[] args)
    {
        EnsureInitialized();
        try
        {
            _context!.Command(args);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[NJPlayer] Command '{string.Join(" ", args)}' failed: {ex.Message}");
        }
    }

    private void EnsureInitialized()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MpvPlayer), "Player has been disposed.");
        }
        if (!_isInitialized || _context == null)
        {
            throw new InvalidOperationException("MpvPlayer is not initialized. Call Initialize() first.");
        }
    }

    private void EventLoop()
    {
        while (!_disposed)
        {
            // Block until mpv signals the wakeup callback or Dispose signals wake
            _wakeEvent.WaitOne();
            if (_disposed || _context == null) break;

            // Drain all available events from the queue with timeout 0
            while (!_disposed)
            {
                MpvEvent evt;
                lock (_context.SyncLock)
                {
                    if (_disposed || _context.IsInvalid || _context.IsClosed) break;
                    var ptr = _context.WaitEvent(0);
                    if (ptr == IntPtr.Zero) break;
                    evt = Marshal.PtrToStructure<MpvEvent>(ptr);
                }

                if (evt.EventId == MpvEventId.None) break;
                ProcessEvent(evt);
            }
        }
    }

    private void ProcessEvent(MpvEvent evt)
    {
        if (evt.EventId == MpvEventId.Shutdown)
        {
            _disposed = true;
            return;
        }

        if (evt.Error < 0)
        {
            var msg = MpvNative.GetErrorString(evt.Error);
            Error?.Invoke(this, new MpvErrorEventArgs(msg, evt.Error));
        }

        switch (evt.EventId)
        {
            case MpvEventId.PropertyChange:
                HandlePropertyChange(evt);
                break;

            case MpvEventId.EndFile:
                HandleEndFile(evt);
                break;
        }
    }

    private void HandlePropertyChange(MpvEvent evt)
    {
        if (evt.Data == IntPtr.Zero) return;

        var prop = Marshal.PtrToStructure<MpvEventProperty>(evt.Data);
        var propName = prop.Name == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(prop.Name);

        switch (evt.ReplyUserData)
        {
            case PropReplyTimePos when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var posSeconds = Marshal.PtrToStructure<double>(prop.Data);
                var pos = MpvEventMapper.SecondsToTimeSpan(posSeconds);
                lock (_stateLock) _position = pos;
                TimePositionChanged?.Invoke(this, pos);
                break;

            case PropReplyDuration when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var durSeconds = Marshal.PtrToStructure<double>(prop.Data);
                var dur = MpvEventMapper.SecondsToTimeSpan(durSeconds);
                lock (_stateLock) _duration = dur;
                DurationChanged?.Invoke(this, dur);
                break;

            case PropReplyPause when prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero:
                var flag = Marshal.ReadByte(prop.Data);
                var isPaused = MpvEventMapper.FlagToBool(flag);
                lock (_stateLock) _isPaused = isPaused;
                PauseChanged?.Invoke(this, isPaused);
                break;

            case PropReplyVolume when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var vol = Marshal.PtrToStructure<double>(prop.Data);
                lock (_stateLock) _volume = vol;
                VolumeChanged?.Invoke(this, vol);
                break;

            case PropReplySpeed when prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero:
                var spd = Marshal.PtrToStructure<double>(prop.Data);
                lock (_stateLock) _speed = spd;
                SpeedChanged?.Invoke(this, spd);
                break;

            case PropReplyTrackList:
                TrackListChanged?.Invoke(this, EventArgs.Empty);
                break;

            case PropReplyMediaTitle when prop.Format == MpvFormat.String && prop.Data != IntPtr.Zero:
                var titlePtr = Marshal.ReadIntPtr(prop.Data);
                var title = titlePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(titlePtr) : string.Empty;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    MediaTitleChanged?.Invoke(this, title);
                }
                break;

            default:
                if (string.Equals(propName, "track-list", StringComparison.OrdinalIgnoreCase))
                {
                    TrackListChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    private void HandleEndFile(MpvEvent evt)
    {
        if (evt.Data == IntPtr.Zero) return;

        var endFile = Marshal.PtrToStructure<MpvEventEndFile>(evt.Data);
        var reason = MpvEventMapper.MapEndFileReason(endFile.Reason);
        var errorMsg = endFile.Error != 0 ? MpvNative.GetErrorString(endFile.Error) : null;

        EndFile?.Invoke(this, new EndFileEventArgs(reason, endFile.Error, errorMsg));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wake up background thread and mpv context
        _wakeEvent.Set();
        try
        {
            _context?.Wakeup();
        }
        catch (ObjectDisposedException) { }

        // Wait for event loop to exit gracefully
        if (_eventThread != null && _eventThread.IsAlive)
        {
            _eventThread.Join(500);
        }

        _context?.Dispose();
        _context = null;

        _wakeEvent.Dispose();
        _isInitialized = false;
    }
}
