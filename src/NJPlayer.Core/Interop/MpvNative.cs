using System;
using System.Runtime.InteropServices;

namespace NJPlayer.Core.Interop;

internal static class MpvNative
{
    private const string LibName = "libmpv-2.dll";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_client_api_version")]
    public static extern ulong ClientApiVersion();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_create")]
    public static extern IntPtr Create();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_initialize")]
    public static extern int Initialize(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_destroy")]
    public static extern void Destroy(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_terminate_destroy")]
    public static extern void TerminateDestroy(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_command")]
    public static extern int Command(IntPtr ctx, [In] IntPtr[] args);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_option_string")]
    public static extern int SetOptionString(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_property")]
    public static extern int SetProperty(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, ref byte data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_property")]
    public static extern int SetPropertyDouble(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, ref double data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_property")]
    public static extern int SetPropertyLong(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, ref long data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_property_string")]
    public static extern int SetPropertyString(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int GetPropertyDouble(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, out double data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int GetPropertyLong(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, out long data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int GetPropertyFlag(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format, out int data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property_string")]
    public static extern IntPtr GetPropertyString(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_free")]
    public static extern void Free(IntPtr data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_observe_property")]
    public static extern int ObserveProperty(IntPtr ctx, ulong replyUserdata, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, MpvFormat format);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_unobserve_property")]
    public static extern int UnobserveProperty(IntPtr ctx, ulong replyUserdata);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_wait_event")]
    public static extern IntPtr WaitEvent(IntPtr ctx, double timeout);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_wakeup")]
    public static extern void Wakeup(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_error_string")]
    public static extern IntPtr ErrorString(int error);

    public static string GetErrorString(int error)
    {
        var ptr = ErrorString(error);
        return ptr == IntPtr.Zero ? "Unknown error" : Marshal.PtrToStringUTF8(ptr) ?? "Unknown error";
    }
}

public enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8,
    ByteArray = 9
}

public enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    TracksChanged = 9,
    TrackSwitched = 10,
    Idle = 11,
    Pause = 12,
    Unpause = 13,
    Tick = 14,
    ScriptInputDispatch = 15,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    MetadataUpdate = 19,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    ChapterChange = 23,
    QueueOverflow = 24,
    Hook = 25
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserData;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventProperty
{
    public IntPtr Name;
    public MpvFormat Format;
    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventEndFile
{
    public int Reason;
    public int Error;
    public long PlaylistEntryId;
}

