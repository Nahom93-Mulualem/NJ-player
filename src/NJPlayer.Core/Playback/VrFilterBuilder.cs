using System;
using System.Globalization;

namespace NJPlayer.Core.Playback;

/// <summary>
/// Constructs FFmpeg v360 panoramic video filter parameters for 180° and 360° videos.
/// </summary>
public static class VrFilterBuilder
{
    public const double DefaultFov = 105.0;
    public const double MinFov = 45.0;
    public const double MaxFov = 140.0;

    /// <summary>
    /// Generates the FFmpeg video filter graph command for the specified VR mode, angles, and field of view.
    /// </summary>
    public static string BuildFilterString(
        VrProjectionMode mode,
        double yaw = 0.0,
        double pitch = 0.0,
        double roll = 0.0,
        double fov = DefaultFov)
    {
        if (mode == VrProjectionMode.Off)
        {
            return string.Empty;
        }

        // Clamp pitch to avoid gimbal flipping (-90 to +90 degrees)
        pitch = Math.Clamp(pitch, -90.0, 90.0);
        fov = Math.Clamp(fov, MinFov, MaxFov);

        // Normalize yaw to [-180, 180]
        while (yaw > 180.0) yaw -= 360.0;
        while (yaw < -180.0) yaw += 360.0;

        var yawStr = yaw.ToString("0.##", CultureInfo.InvariantCulture);
        var pitchStr = pitch.ToString("0.##", CultureInfo.InvariantCulture);
        var rollStr = roll.ToString("0.##", CultureInfo.InvariantCulture);
        var fovStr = fov.ToString("0.##", CultureInfo.InvariantCulture);

        return mode switch
        {
            VrProjectionMode.Vr180Sbs =>
                $"crop=iw/2:ih:0:0,v360=input=hequirect:output=flat:d_fov={fovStr}:yaw={yawStr}:pitch={pitchStr}:roll={rollStr}",
            VrProjectionMode.Vr180Mono =>
                $"v360=input=hequirect:output=flat:d_fov={fovStr}:yaw={yawStr}:pitch={pitchStr}:roll={rollStr}",
            VrProjectionMode.Vr360Mono =>
                $"v360=input=equirect:output=flat:d_fov={fovStr}:yaw={yawStr}:pitch={pitchStr}:roll={rollStr}",
            VrProjectionMode.Vr180TopBottom =>
                $"crop=iw:ih/2:0:0,v360=input=hequirect:output=flat:d_fov={fovStr}:yaw={yawStr}:pitch={pitchStr}:roll={rollStr}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Friendly user-facing label for the active VR projection mode.
    /// </summary>
    public static string GetModeLabel(VrProjectionMode mode)
    {
        return mode switch
        {
            VrProjectionMode.Vr180Sbs => "VR 180° Side-by-Side (SBS)",
            VrProjectionMode.Vr180Mono => "VR 180° Monoscopic",
            VrProjectionMode.Vr360Mono => "VR 360° Spherical",
            VrProjectionMode.Vr180TopBottom => "VR 180° Top-Bottom",
            _ => "VR Disabled"
        };
    }
}
