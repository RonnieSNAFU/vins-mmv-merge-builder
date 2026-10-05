using NRMerge;
using Xunit;

public class VerifyTests
{
    [Fact]
    public void OnlyDanglingReferencesAbsentFromBothSourcesCountAsIntroduced()
    {
        var a = new Verify.Dangling("NpcParam", 1, "itemLotId", 5);
        var b = new Verify.Dangling("NpcParam", 2, "itemLotId", 6);
        var c = new Verify.Dangling("Bullet", 3, "atkId", 7);
        var r = Verify.IntroducedDangling(new() { a, b, c }, new() { a }, new() { b });
        Assert.Equal(new[] { c }, r);
    }
}

public class VerifyCopyTests
{
    [Fact]
    public void ACopiedRowWhoseValueDanglesInASourceIsNotIntroduced()
    {
        var copy = new Verify.Dangling("BehaviorParam_PC", 104000148, "refId", 4000420);
        var original = new Verify.Dangling("BehaviorParam_PC", 104000420, "refId", 4000420);
        Assert.Empty(Verify.IntroducedDangling(new() { copy }, new() { original }, new()));
    }
}
