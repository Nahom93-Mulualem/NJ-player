using System;
using NJPlayer.Core.Playback;
using NJPlayer.Core.Settings;
using Xunit;

namespace NJPlayer.Tests;

public class CodecTests
{
    [Fact]
    public void AppSettings_OpenCodecDefaults_AreSetCorrectly()
    {
        var settings = new AppSettings();

        Assert.Equal("auto", settings.VideoDecoderPriority);
        Assert.Equal("auto", settings.AudioDecoderPriority);
        Assert.Equal(string.Empty, settings.CustomLavcOptions);
        Assert.Equal("auto-safe", settings.HardwareDecoder);
    }

    [Theory]
    [InlineData("d3d11va", true)]
    [InlineData("nvdec", true)]
    [InlineData("qsv", true)]
    [InlineData("vulkan", true)]
    [InlineData("no", false)]
    [InlineData("software", false)]
    [InlineData("", false)]
    public void CodecInfo_IsHardwareAccelerated_EvaluatesCorrectly(string hwdec, bool expectedAcc)
    {
        var info = new CodecInfo
        {
            HardwareDecoder = hwdec
        };

        Assert.Equal(expectedAcc, info.IsHardwareAccelerated);
    }
}

