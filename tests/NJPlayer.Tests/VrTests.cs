using System;
using NJPlayer.Core.Playback;
using Xunit;

namespace NJPlayer.Tests;

public class VrTests
{
    [Fact]
    public void VrFilterBuilder_Off_ReturnsEmpty()
    {
        var filter = VrFilterBuilder.BuildFilterString(VrProjectionMode.Off);
        Assert.Equal(string.Empty, filter);
    }

    [Fact]
    public void VrFilterBuilder_Vr180Sbs_BuildsCorrectCropAndV360Parameters()
    {
        var filter = VrFilterBuilder.BuildFilterString(
            VrProjectionMode.Vr180Sbs,
            yaw: 15.0,
            pitch: -10.0,
            roll: 0.0,
            fov: 110.0);

        Assert.Contains("crop=iw/2:ih:0:0", filter);
        Assert.Contains("v360=input=hequirect:output=flat", filter);
        Assert.Contains("d_fov=110", filter);
        Assert.Contains("yaw=15", filter);
        Assert.Contains("pitch=-10", filter);
    }

    [Fact]
    public void VrFilterBuilder_Vr180Mono_BuildsHequirectWithoutCrop()
    {
        var filter = VrFilterBuilder.BuildFilterString(
            VrProjectionMode.Vr180Mono,
            yaw: 0.0,
            pitch: 0.0,
            roll: 0.0,
            fov: 105.0);

        Assert.DoesNotContain("crop=", filter);
        Assert.Contains("v360=input=hequirect:output=flat", filter);
        Assert.Contains("d_fov=105", filter);
    }

    [Fact]
    public void VrFilterBuilder_Vr360Mono_BuildsEquirectangularFilter()
    {
        var filter = VrFilterBuilder.BuildFilterString(
            VrProjectionMode.Vr360Mono,
            yaw: 90.0,
            pitch: 30.0,
            roll: 0.0,
            fov: 120.0);

        Assert.Contains("v360=input=equirect:output=flat", filter);
        Assert.Contains("yaw=90", filter);
        Assert.Contains("pitch=30", filter);
        Assert.Contains("d_fov=120", filter);
    }

    [Fact]
    public void VrFilterBuilder_ClampsPitchAndFovToSafeRanges()
    {
        // Pitch beyond 90 degrees should clamp
        var filterPitchOver = VrFilterBuilder.BuildFilterString(
            VrProjectionMode.Vr180Sbs,
            yaw: 0.0,
            pitch: 150.0,
            roll: 0.0,
            fov: 250.0);

        Assert.Contains("pitch=90", filterPitchOver);
        Assert.Contains($"d_fov={VrFilterBuilder.MaxFov}", filterPitchOver);

        var filterFovUnder = VrFilterBuilder.BuildFilterString(
            VrProjectionMode.Vr180Sbs,
            yaw: 0.0,
            pitch: -120.0,
            roll: 0.0,
            fov: 10.0);

        Assert.Contains("pitch=-90", filterFovUnder);
        Assert.Contains($"d_fov={VrFilterBuilder.MinFov}", filterFovUnder);
    }

    [Fact]
    public void VrFilterBuilder_GetModeLabel_ReturnsAccurateDescriptions()
    {
        Assert.Contains("Side-by-Side", VrFilterBuilder.GetModeLabel(VrProjectionMode.Vr180Sbs));
        Assert.Contains("Monoscopic", VrFilterBuilder.GetModeLabel(VrProjectionMode.Vr180Mono));
        Assert.Contains("Spherical", VrFilterBuilder.GetModeLabel(VrProjectionMode.Vr360Mono));
        Assert.Contains("Disabled", VrFilterBuilder.GetModeLabel(VrProjectionMode.Off));
    }
}
