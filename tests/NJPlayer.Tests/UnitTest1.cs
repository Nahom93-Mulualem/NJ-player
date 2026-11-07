namespace NJPlayer.Tests;

public class BasicArchitectureTests
{
    [Fact]
    public void Core_HasExpectedName()
    {
        Assert.Equal("NJPlayer.Core", Core.CoreInfo.Name);
    }

    [Fact]
    public void Features_HasExpectedName()
    {
        Assert.Equal("NJPlayer.Features", Features.FeaturesInfo.Name);
    }
}
