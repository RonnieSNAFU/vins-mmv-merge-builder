using SoulsFormats;
using Xunit;

namespace NRMerge.Tests;

public class CoopCodeTests : IDisposable
{
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-coop-" + Guid.NewGuid().ToString("N")[..8]);

    string Mod(string name, Action<string> fill)
    {
        var d = Path.Combine(tmp, name);
        Directory.CreateDirectory(Path.Combine(d, "chr"));
        Directory.CreateDirectory(Path.Combine(d, "dll", "logs"));
        File.WriteAllBytes(Path.Combine(d, "chr", "c1.hks"), new byte[] { 1, 2, 3 });
        fill(d);
        return d;
    }

    public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

    [Fact]
    public void IgnoresLogsAndConfigsButNotGameData()
    {
        var a = Mod("a", d => File.WriteAllText(Path.Combine(d, "dll", "logs", "x.log"), "1"));
        var b = Mod("b", d => { File.WriteAllText(Path.Combine(d, "dll", "logs", "x.log"), "2"); File.WriteAllText(Path.Combine(d, "dll", "nighter.json"), "{}"); });
        var c = Mod("c", d => File.WriteAllBytes(Path.Combine(d, "chr", "c1.hks"), new byte[] { 1, 2, 4 }));
        Assert.Equal(CoopCode.Compute(a), CoopCode.Compute(b));
        Assert.NotEqual(CoopCode.Compute(a), CoopCode.Compute(c));
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", CoopCode.Compute(a));
    }

    [Fact]
    public void SameContentWithDifferentCompressionGivesTheSameCode()
    {
        var content = new byte[5000];
        new Random(1).NextBytes(content);
        var a = Mod("a", d => File.WriteAllBytes(Path.Combine(d, "chr", "c2.anibnd.dcx"), DCX.Compress(content, DCX.Type.DCX_DFLT_10000_44_9)));
        var b = Mod("b", d => File.WriteAllBytes(Path.Combine(d, "chr", "c2.anibnd.dcx"), DCX.Compress(content, DCX.Type.DCX_DFLT_10000_24_9)));
        Assert.NotEqual(File.ReadAllBytes(Path.Combine(a, "chr", "c2.anibnd.dcx")), File.ReadAllBytes(Path.Combine(b, "chr", "c2.anibnd.dcx")));
        Assert.Equal(CoopCode.Compute(a), CoopCode.Compute(b));
    }
}
