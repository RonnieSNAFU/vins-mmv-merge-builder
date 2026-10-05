using System.Text.Json;
using Xunit;

namespace NRMerge.Tests;

public class PyDifflibTests
{
    [Fact]
    public void NdiffChangeCount_SimpleReplace() => Assert.Equal(2, PyDifflib.NdiffChangeCount(new[] { "a", "b", "c" }, new[] { "a", "x", "c" }));

    [Fact]
    public void NdiffChangeCount_IdenticalLinesInsideReplaceBlockAreNotCounted()
        => Assert.Equal(2, PyDifflib.NdiffChangeCount(new[] { "end", "end", "end", "x = 1" }, new[] { "y = 2", "end", "end", "end" }));

    /// <summary>Expected counts produced by CPython 3.13 difflib.ndiff (fixtures/ndiff_counts.json; includes autojunk-sized inputs).</summary>
    [Fact]
    public void NdiffChangeCount_MatchesCPythonFixtures()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ndiff_counts.json")));
        int n = 0;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var a = c[0].EnumerateArray().Select(x => x.GetString()).ToArray();
            var b = c[1].EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.True(c[2].GetInt32() == PyDifflib.NdiffChangeCount(a, b), $"case {n}");
            n++;
        }
        Assert.Equal(43, n);
    }
}
