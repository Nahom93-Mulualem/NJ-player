using System;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Reasons why playback of a file or stream ended in mpv.
/// </summary>
public enum EndFileReason
{
    Unknown = -1,
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5
}

/// <summary>
/// Event arguments delivered when a media file finishes or stops.
/// </summary>
public sealed class EndFileEventArgs : EventArgs
{
    public EndFileReason Reason { get; }
    public int ErrorCode { get; }
    public string? ErrorMessage { get; }

    public EndFileEventArgs(EndFileReason reason, int errorCode, string? errorMessage = null)
    {
        Reason = reason;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event arguments delivered when an mpv internal error occurs.
/// </summary>
public sealed class MpvErrorEventArgs : EventArgs
{
    public string Message { get; }
    public int ErrorCode { get; }

    public MpvErrorEventArgs(string message, int errorCode = 0)
    {
        Message = message;
        ErrorCode = errorCode;
    }
}

