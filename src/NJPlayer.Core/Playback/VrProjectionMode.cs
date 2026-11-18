namespace NJPlayer.Core.Playback;

/// <summary>
/// Supported 180° and 360° VR video projection modes for converting panoramic video into a flat interactive viewport.
/// </summary>
public enum VrProjectionMode
{
    Off = 0,
    Vr180Sbs = 1,
    Vr180Mono = 2,
    Vr360Mono = 3,
    Vr180TopBottom = 4
}
