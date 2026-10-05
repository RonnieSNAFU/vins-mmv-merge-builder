using NRMerge;
using Xunit;

public class NameIdMergeTests
{
    [Fact]
    public void NamesOnlyMmvHasAreAppendedAfterEvsAndCountUpdated()
    {
        var ev = "Num  = 3\r\n1    = \"A\"\r\n2    = \"B\"\r\n3    = \"C\"\r\n";
        var mmv = "#comment\r\n\r\nNum  = 3\r\n1    = \"A\"\r\n2    = \"B\"\r\n3    = \"D\"\r\n\0\0";
        var r = NameIdMerge.Merge(ev, mmv, out var added);
        Assert.Equal("Num  = 4\r\n1    = \"A\"\r\n2    = \"B\"\r\n3    = \"C\"\r\n4    = \"D\"\r\n", r);
        Assert.Equal(new[] { "D" }, added);
    }

    [Fact]
    public void NothingNewKeepsEvsTextExactly()
    {
        var ev = "Num  = 1\r\n1    = \"A\"\r\n";
        Assert.Equal(ev, NameIdMerge.Merge(ev, "Num  = 1\n1    = \"A\"\n", out var added));
        Assert.Empty(added);
    }
}
