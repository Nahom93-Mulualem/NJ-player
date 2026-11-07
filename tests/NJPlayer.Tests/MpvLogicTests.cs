using System;
using NJPlayer.Core.Playback;
using Xunit;

namespace NJPlayer.Tests;

public class MpvLogicTests
{
    [Fact]
    public void OptionBuilder_DefaultOptions_ContainExpectedValues()
    {
        var builder = new MpvOptionBuilder();
        var options = builder.Build();

        Assert.Equal("auto-safe", options["hwdec"]);
        Assert.Equal("yes", options["keep-open"]);
        Assert.Equal("yes", options["idle"]);
        Assert.Equal("no", options["terminal"]);
    }

    [Fact]
    public void OptionBuilder_CustomOptionsAndWid_SetProperly()
    {
        var wid = new IntPtr(123456);
        var builder = new MpvOptionBuilder()
            .WithHardwareDecoding("d3d11va")
            .WithKeepOpen(false)
            .WithWindowHandle(wid)
            .WithOption("vo", "gpu-next");

        var options = builder.Build();

        Assert.Equal("d3d11va", options["hwdec"]);
        Assert.Equal("no", options["keep-open"]);
        Assert.Equal(wid.ToInt64().ToString(), options["wid"]);
        Assert.Equal("gpu-next", options["vo"]);
    }

    [Theory]
    [InlineData(0, EndFileReason.Eof)]
    [InlineData(2, EndFileReason.Stop)]
    [InlineData(3, EndFileReason.Quit)]
    [InlineData(4, EndFileReason.Error)]
    [InlineData(5, EndFileReason.Redirect)]
    [InlineData(99, EndFileReason.Unknown)]
    [InlineData(-1, EndFileReason.Unknown)]
    public void EventMapper_MapsEndFileReason_Correctly(int rawReason, EndFileReason expected)
    {
        var result = MpvEventMapper.MapEndFileReason(rawReason);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(65.5, 65500)]
    [InlineData(3600.0, 3600000)]
    public void EventMapper_SecondsToTimeSpan_ValidSeconds(double seconds, double expectedMs)
    {
        var ts = MpvEventMapper.SecondsToTimeSpan(seconds);
        Assert.Equal(expectedMs, ts.TotalMilliseconds);
    }

    [Fact]
    public void EventMapper_SecondsToTimeSpan_HandlesSpecialDoubles()
    {
        Assert.Equal(TimeSpan.Zero, MpvEventMapper.SecondsToTimeSpan(-5.0));
        Assert.Equal(TimeSpan.Zero, MpvEventMapper.SecondsToTimeSpan(double.NaN));
        Assert.Equal(TimeSpan.Zero, MpvEventMapper.SecondsToTimeSpan(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(42, true)]
    public void EventMapper_FlagToBool_MapsCorrectly(int flag, bool expected)
    {
        Assert.Equal(expected, MpvEventMapper.FlagToBool(flag));
    }

    [Fact]
    public void EventMapper_BuildLoadFileCommand_FormatsArgumentsWithoutStringConcatenation()
    {
        var playCmd = MpvEventMapper.BuildLoadFileCommand("C:\\videos\\sample.mp4", startPaused: false);
        Assert.Equal(3, playCmd.Length);
        Assert.Equal("loadfile", playCmd[0]);
        Assert.Equal("C:\\videos\\sample.mp4", playCmd[1]);
        Assert.Equal("replace", playCmd[2]);

        var pauseCmd = MpvEventMapper.BuildLoadFileCommand("C:\\videos\\sample.mp4", startPaused: true);
        Assert.Equal("pause", pauseCmd[2]);
    }

    [Fact]
    public void EventMapper_BuildSeekCommand_FormatsCorrectFlags()
    {
        var fastSeek = MpvEventMapper.BuildSeekCommand(45.2, exact: false);
        Assert.Equal(new[] { "seek", "45.2", "absolute" }, fastSeek);

        var exactSeek = MpvEventMapper.BuildSeekCommand(120.0, exact: true);
        Assert.Equal(new[] { "seek", "120", "absolute+exact" }, exactSeek);

        var relFast = MpvEventMapper.BuildSeekRelativeCommand(5.0, exact: false);
        Assert.Equal(new[] { "seek", "5", "relative" }, relFast);

        var relExact = MpvEventMapper.BuildSeekRelativeCommand(-10.0, exact: true);
        Assert.Equal(new[] { "seek", "-10", "relative+exact" }, relExact);
    }
}

