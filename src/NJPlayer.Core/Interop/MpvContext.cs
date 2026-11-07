using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NJPlayer.Core.Interop;

/// <summary>
/// SafeHandle wrapper for unmanaged mpv_handle context.
/// Provides synchronized, thread-safe access to low-level mpv functions.
/// </summary>
public sealed class MpvContext : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly object _syncLock = new();

    public MpvContext() : base(true)
    {
    }

    /// <summary>
    /// Thread synchronization lock for callers who need to coordinate with the mpv event loop or execute batch operations.
    /// </summary>
    public object SyncLock => _syncLock;

    public static MpvContext Create()
    {
        IntPtr ptr;
        try
        {
            ptr = MpvNative.Create();
        }
        catch (DllNotFoundException ex)
        {
            throw new DllNotFoundException(
                "Could not find 'libmpv-2.dll'. Please download libmpv (x64) and place it in the application directory or 'libs/' folder.",
                ex);
        }

        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create mpv context.");
        }

        var context = new MpvContext();
        context.SetHandle(ptr);
        return context;
    }

    public void Initialize()
    {
        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.Initialize(handle);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_initialize failed with error {res}: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    public void SetOptionString(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.SetOptionString(handle, name, value);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_set_option_string('{name}', '{value}') failed: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    /// <summary>
    /// Executes an mpv command using a null-terminated array of strings.
    /// Never uses string concatenation with paths or parameters.
    /// </summary>
    public void Command(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0) return;

        lock (_syncLock)
        {
            ThrowIfInvalid();

            var pointers = new IntPtr[args.Length + 1];
            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i] ?? string.Empty);
                }
                pointers[^1] = IntPtr.Zero; // null-terminated array

                var res = MpvNative.Command(handle, pointers);
                if (res < 0)
                {
                    throw new InvalidOperationException($"mpv_command failed with error {res}: {MpvNative.GetErrorString(res)}");
                }
            }
            finally
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if (pointers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pointers[i]);
                    }
                }
            }
        }
    }

    public void SetPropertyString(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.SetPropertyString(handle, name, value);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_set_property_string('{name}', '{value}') failed: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    public void SetPropertyDouble(string name, double value)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.SetPropertyDouble(handle, name, MpvFormat.Double, ref value);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_set_property double ('{name}', {value}) failed: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    public void SetPropertyFlag(string name, bool value)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            byte flag = (byte)(value ? 1 : 0);
            var res = MpvNative.SetProperty(handle, name, MpvFormat.Flag, ref flag);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_set_property flag ('{name}', {value}) failed: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    public string? GetPropertyString(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var ptr = MpvNative.GetPropertyString(handle, name);
            if (ptr == IntPtr.Zero) return null;

            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                MpvNative.Free(ptr);
            }
        }
    }

    public double? GetPropertyDouble(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.GetPropertyDouble(handle, name, MpvFormat.Double, out var val);
            return res >= 0 ? val : null;
        }
    }

    public bool? GetPropertyFlag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.GetPropertyFlag(handle, name, MpvFormat.Flag, out var val);
            return res >= 0 ? (val != 0) : null;
        }
    }

    public void ObserveProperty(ulong replyUserdata, string name, MpvFormat format)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_syncLock)
        {
            ThrowIfInvalid();
            var res = MpvNative.ObserveProperty(handle, replyUserdata, name, format);
            if (res < 0)
            {
                throw new InvalidOperationException($"mpv_observe_property('{name}') failed: {MpvNative.GetErrorString(res)}");
            }
        }
    }

    public void UnobserveProperty(ulong replyUserdata)
    {
        lock (_syncLock)
        {
            if (IsInvalid || IsClosed) return;
            MpvNative.UnobserveProperty(handle, replyUserdata);
        }
    }

    public IntPtr WaitEvent(double timeout)
    {
        lock (_syncLock)
        {
            if (IsInvalid || IsClosed) return IntPtr.Zero;
            return MpvNative.WaitEvent(handle, timeout);
        }
    }

    public void Wakeup()
    {
        lock (_syncLock)
        {
            if (IsInvalid || IsClosed) return;
            MpvNative.Wakeup(handle);
        }
    }

    public void SetWakeupCallback(MpvWakeupCallback callback, IntPtr data)
    {
        lock (_syncLock)
        {
            ThrowIfInvalid();
            MpvNative.SetWakeupCallback(handle, callback, data);
        }
    }

    private void ThrowIfInvalid()
    {
        if (IsInvalid || IsClosed)
        {
            throw new ObjectDisposedException(nameof(MpvContext), "mpv context is not initialized or has been disposed.");
        }
    }

    protected override bool ReleaseHandle()
    {
        lock (_syncLock)
        {
            if (handle != IntPtr.Zero)
            {
                MpvNative.TerminateDestroy(handle);
                handle = IntPtr.Zero;
            }
        }
        return true;
    }
}

