using NRMerge;
using Xunit;

public class AssembleTests
{
    static string Tmp(string content)
    {
        if (content == null) return Path.Combine(Path.GetTempPath(), "nrmerge-missing-" + Guid.NewGuid().ToString("N"));
        var p = Path.Combine(Path.GetTempPath(), "nrmerge-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(p, content);
        return p;
    }

    [Theory]
    [InlineData("x", null, null, "ev-only")]
    [InlineData(null, "x", null, "mmv-only")]
    [InlineData("x", "x", "base", "identical")]
    [InlineData("base", "m", "base", "take-mmv")]
    [InlineData("e", "base", "base", "take-ev")]
    [InlineData("e", "m", "base", "merge")]
    [InlineData("e", "m", null, "merge")]
    [InlineData("same", "same", null, "identical")]
    public void ClassifiesByComparingBothModsWithTheBase(string ev, string mmv, string bas, string expected)
    {
        var cls = Assemble.Classify(Tmp(ev), Tmp(mmv), bas == null ? null : Tmp(bas));
        Assert.Equal(expected, cls);
    }

    [Theory]
    [InlineData("dll/nighter.dll", true)]
    [InlineData("ServerRedirector/cl_server_redirector.dll", true)]
    [InlineData(".smithbox/MSB/Map List Filters.json", true)]
    [InlineData("project.json", true)]
    [InlineData("regulation.bin", true)]
    [InlineData("regulation.bin.prev", true)]
    [InlineData("chr/c0000.behbnd.dcx", false)]
    [InlineData("map/mapstudio/m30_00_00_00.msb.dcx", false)]
    public void ExcludesProfileDllAndRegulationPaths(string rel, bool excluded)
    {
        Assert.Equal(excluded, Assemble.IsExcluded(rel));
    }
}
