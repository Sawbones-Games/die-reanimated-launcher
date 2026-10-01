using DieReanimated.Launcher.Ui;
using Xunit;

namespace Launcher.Tests;

public sealed class StartOptionsTests
{
    [Fact]
    public void RecognisesItsOwnOptions_ForwardsTheRest()
    {
        var o = StartOptions.Parse(new[] { "--game", @"C:\g", "-nolog", "--server", "http://s", "--updated", "--tab", "news", "-force-d3d9" });
        Assert.Equal(@"C:\g", o.GameDir);
        Assert.Equal("http://s", o.ServerUrl);
        Assert.True(o.JustUpdated);
        Assert.Equal("news", o.Tab);
        Assert.Equal(new[] { "-nolog", "-force-d3d9" }, o.GameArgs);
    }

    [Fact]
    public void RestartArgsReproduceTheStart()
    {
        var o = StartOptions.Parse(new[] { "--game", @"C:\g", "-nolog" });
        Assert.Equal(new[] { "--game", @"C:\g", "-nolog" }, o.RestartArgs());
    }

    [Fact]
    public void NothingIsTheNormalCase()
    {
        var o = StartOptions.Parse(Array.Empty<string>());
        Assert.Null(o.GameDir); Assert.Null(o.ServerUrl); Assert.False(o.JustUpdated); Assert.Empty(o.GameArgs);
    }
}
