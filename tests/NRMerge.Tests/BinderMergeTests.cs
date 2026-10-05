using NRMerge;
using SoulsFormats;
using Xunit;

public class BinderMergeTests
{
    static string Write(string dir, string name, params (string path, byte[] data)[] files)
    {
        var b = new BND4();
        int id = 0;
        foreach (var (p, d) in files) b.Files.Add(new BinderFile(Binder.FileFlags.Flag1, id++, p, d));
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, b.Write());
        return path;
    }

    static byte[] B(params byte[] x) => x;

    [Fact]
    public void BinderDecisionsGoToTheStagesOwnJournalArea()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bas = Write(dir, "b.bnd", ("x.tae", B(1)));
        var ev = Write(dir, "e.bnd", ("x.tae", B(2)));
        var mmv = Write(dir, "m.bnd", ("x.tae", B(3)));
        Journal.Clear();
        BinderMerge.Merge(bas, ev, mmv, Path.Combine(dir, "o.bnd"), _ => Side.EV, "t", area: "binders-test");
        Assert.Equal(1, Journal.Count("binders-test"));
        Assert.Equal(0, Journal.Count("binders"));
    }

    [Fact]
    public void EntryBothChangedIsMergedByTheEntryMergerAndFullPathKeysKeepSameNamedFilesApart()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bas = Write(dir, "b.bnd", (@"w:\x\interroot_win64\action\c1\export\behaviors\c1.hkx", B(1)), (@"w:\x\interroot_win64\action\c1\export\c1.hkx", B(9)));
        var ev = Write(dir, "e.bnd", (@"n:\y\interroot_win64\action\c1\export\behaviors\c1.hkx", B(2)), (@"n:\y\interroot_win64\action\c1\export\c1.hkx", B(9)));
        var mmv = Write(dir, "m.bnd", (@"w:\x\interroot_win64\action\c1\export\behaviors\c1.hkx", B(3)), (@"w:\x\interroot_win64\action\c1\export\c1.hkx", B(8)));
        var outp = Path.Combine(dir, "o.bnd");
        string seenKey = null;
        var r = BinderMerge.Merge(bas, ev, mmv, outp, _ => Side.EV, "t",
            (key, b, e, m) => { seenKey = key; return new byte[] { (byte)(b[0] + e[0] + m[0]) }; }, fullPathKeys: true);
        var o = BND4.Read(File.ReadAllBytes(outp));
        Assert.Equal(@"action\c1\export\behaviors\c1.hkx", seenKey);
        Assert.Equal(6, o.Files.Single(f => f.Name.Contains("behaviors")).Bytes.ToArray()[0]);   // merged 1+2+3
        Assert.Equal(8, o.Files.Single(f => !f.Name.Contains("behaviors")).Bytes.ToArray()[0]);  // MMV-only change taken
        Assert.Equal(2, o.Files.Count);
    }

    [Fact]
    public void AnimationSetEvEmptiedButMmvFilledKeepsMmvsAnimations()
    {
        // EV emptied vanilla a281 (no EV weapon uses hero moveset 281); MMV's Frozen Cold Needle Invader still does
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bas = Write(dir, "b.bnd", ("a281.tae", B(1, 1)), ("a282.tae", B(5)), ("x.fxr", B(7)));
        var ev = Write(dir, "e.bnd", ("a281.tae", B()), ("x.fxr", B()));
        var mmv = Write(dir, "m.bnd", ("a281.tae", B(1, 2, 3)), ("a282.tae", B(6)), ("x.fxr", B(8)));
        var outp = Path.Combine(dir, "o.bnd");
        Journal.Clear();
        BinderMerge.Merge(bas, ev, mmv, outp, _ => Side.EV, "t", (key, b, e, m) => null);
        var o = BND4.Read(File.ReadAllBytes(outp)).Files.ToDictionary(f => f.Name, f => f.Bytes.ToArray());
        Assert.Equal(B(1, 2, 3), o["a281.tae"]);   // emptied by EV, filled by MMV -> MMV
        Assert.Equal(B(6), o["a282.tae"]);         // removed by EV, changed by MMV -> MMV
        Assert.Equal(B(), o["x.fxr"]);             // other entry types keep the owner rule
    }
}

public class EntryKeyTests
{
    [Theory]
    [InlineData(@"N:\GR\data\INTERROOT_win64\chr\c2120\tae\c2120.tae", @"chr\c2120\tae\c2120.tae")]
    [InlineData(@"W:\CL\data\Target\INTERROOT_win64\chr\c2120\INTERROOT_win64\chr\c2120\tae\c2120.tae", @"chr\c2120\tae\c2120.tae")]
    [InlineData(@"N:\GR\data\Model\chr\c2120\blf\c2120_div00.blf", @"chr\c2120\blf\c2120_div00.blf")]
    [InlineData(@"W:\CL\data\Target\INTERROOT_win64\chr\c2120\Model\chr\c2120\blf\c2120_div00.blf", @"chr\c2120\blf\c2120_div00.blf")]
    [InlineData(@"w:\cl\data\target\interroot_win64\action\c5160\export\behaviors\c5160.hkx", @"action\c5160\export\behaviors\c5160.hkx")]
    public void BuildRootsAndDuplicatedPrefixesAreIgnored(string name, string key) => Assert.Equal(key, BehProbe.EntryKey(name));
}
