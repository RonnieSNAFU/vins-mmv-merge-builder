using NRMerge;
using Xunit;

public class Seq3Tests
{
    static List<string> L(string s) => s.Select(c => c.ToString()).ToList();
    static string S(IEnumerable<string> l) => string.Concat(l);

    [Fact]
    public void IdenticalInputsReturnSameList()
    {
        var r = Seq3.Merge(L("abcdef"), L("abcdef"), L("abcdef"), x => x, out var conflicts);
        Assert.Equal("abcdef", S(r));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void InsertionsAtDifferentPlacesAreBothKeptInOrder()
    {
        var r = Seq3.Merge(L("abcdef"), L("abXcdef"), L("abcdeYf"), x => x, out var conflicts);
        Assert.Equal("abXcdeYf", S(r));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DeletionByOneSideIsApplied()
    {
        var r = Seq3.Merge(L("abcdef"), L("abdef"), L("abcdef"), x => x, out var conflicts);
        Assert.Equal("abdef", S(r));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DifferentInsertionsAtSameSpotConflictAndKeepAThenB()
    {
        var r = Seq3.Merge(L("abcd"), L("abXcd"), L("abYcd"), x => x, out var conflicts);
        Assert.Single(conflicts);
        Assert.Equal("abXYcd", S(r));
    }

    [Fact]
    public void SameInsertionOnBothSidesIsNotAConflict()
    {
        var r = Seq3.Merge(L("abcd"), L("abXcd"), L("abXcd"), x => x, out var conflicts);
        Assert.Equal("abXcd", S(r));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void ChangeVersusDeleteOfSameItemIsAConflict()
    {
        // A replaces c with C, B deletes c
        var r = Seq3.Merge(L("abcd"), L("abCd"), L("abd"), x => x, out var conflicts);
        Assert.Single(conflicts);
        Assert.Equal("abCd", S(r));
    }
}

public class Seq3ResolverTests
{
    static List<string> L(string s) => s.Select(c => c.ToString()).ToList();

    [Fact]
    public void ResolverCanSettleAConflictHunk()
    {
        // base a S b c ; A: a S B' c ; B: a T B' x c  -> resolver takes B's hunk
        var r = Seq3.Merge(L("aSbc"), L("aSBc"), L("aTBxc"), x => x, out var conflicts,
            c => c.A.Where(x => !c.Base.Contains(x)).All(c.B.Contains) ? c.B.ToList() : null);
        Assert.Empty(conflicts);
        Assert.Equal("aTBxc", string.Concat(r));
    }
}
