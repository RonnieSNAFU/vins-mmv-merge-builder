using Microsoft.Extensions.Logging;
using Xunit;

namespace NRMerge.Tests;

public class SelfTestTests
{
    [Fact]
    public void Quiet_DisablesLibraryDebugLogging()
    {
        LibraryLogging.Quiet();
        Assert.False(Andre.Core.AndreLogging.For<SelfTestTests>().IsEnabled(LogLevel.Debug));
        Assert.False(Andre.Core.AndreLogging.For<SelfTestTests>().IsEnabled(LogLevel.Information));
        Assert.True(Andre.Core.AndreLogging.For<SelfTestTests>().IsEnabled(LogLevel.Warning));
        Assert.False(SoulsFormats.Util.Logging.LoggerFactory.CreateLogger("x").IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void SelfTest_PassesInTheTestHost()
    {
        var w = new StringWriter();
        Assert.True(SelfTest.Run(w) == 0, w.ToString());
        Assert.Contains("selftest: OK", w.ToString());
    }
}
