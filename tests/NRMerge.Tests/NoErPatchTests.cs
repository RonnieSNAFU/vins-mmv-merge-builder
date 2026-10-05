using Xunit;

namespace NRMerge.Tests;

public class DeltaTests
{
    static byte[] Rnd(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }

    [Fact]
    public void RoundTripsAndOnlyChangedBytesAreLiteral()
    {
        var pre = Rnd(200_000, 1);
        var other = Rnd(50_000, 2);
        var target = (byte[])pre.Clone();
        target[1000] ^= 0xFF; target[1001] ^= 0xFF;                   // a changed field
        Array.Copy(other, 10_000, target, 120_000, 3000);             // a block taken from another source
        var dict = pre.Concat(other).ToArray();
        var patch = Delta.Encode(dict, target, out var literal);
        Assert.Equal(target, Delta.Decode(dict, patch));
        Assert.True(literal <= 8, $"literal {literal}");
        Assert.True(patch.Length < 200, $"patch {patch.Length}");
    }

    [Fact]
    public void BytesNoSourceHasAreCarriedAsLiterals()
    {
        var dict = Rnd(10_000, 3);
        var target = Rnd(500, 4);
        var patch = Delta.Encode(dict, target, out var literal);
        Assert.Equal(target, Delta.Decode(dict, patch));
        Assert.Equal(500, literal);
    }

    [Fact]
    public void EmptyAndTinyTargets()
    {
        var dict = Rnd(1000, 5);
        foreach (var t in new[] { Array.Empty<byte>(), new byte[] { 1, 2, 3 } })
            Assert.Equal(t, Delta.Decode(dict, Delta.Encode(dict, t, out _)));
    }
}

/// <summary>make-noer-patches + the noerpatch stage: the no-Elden-Ring result becomes the verified result, copying from EV/MMV.</summary>
[Collection("Paths")]
public class NoErPatchTests : IDisposable
{
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-noerpatch-" + Guid.NewGuid().ToString("N")[..8]);

    static byte[] Rnd(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }

    void Put(string root, string rel, byte[] b)
    {
        var f = Paths.In(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f));
        File.WriteAllBytes(f, b);
    }

    (string full, string noer, string patches) Setup(bool er)
    {
        var cfg = new BuildConfig
        {
            WorkDir = Path.Combine(tmp, "w"), DataDir = Path.Combine(tmp, "data"), NrGame = Path.Combine(tmp, "nr"),
            ErGame = er ? Path.Combine(tmp, "er") : null, EvMod = Path.Combine(tmp, "ev"), MmvMod = Path.Combine(tmp, "mmv"),
        };
        Paths.Use(cfg);
        var ev = Rnd(40_000, 10);
        var mmv = Rnd(40_000, 11);
        Put(Paths.EV, "chr/c1.bin", ev);
        Put(Paths.MMV, "chr/c1.bin", mmv);
        string full = Path.Combine(tmp, "full"), noer = Paths.OutMod;
        // verified: half EV, half MMV; without Elden Ring: all EV
        Put(full, "chr/c1.bin", ev.Take(20_000).Concat(mmv.Skip(20_000)).ToArray());
        Put(noer, "chr/c1.bin", ev);
        Put(full, "same.bin", Rnd(100, 12)); Put(noer, "same.bin", Rnd(100, 12));
        Put(full, "only-verified.bin", mmv.Take(5000).ToArray());
        Put(Paths.MMV, "only-verified.bin", mmv.Take(6000).ToArray());
        Put(noer, "only-noer.bin", Rnd(10, 13));
        return (full, noer, NoErPatch.Dir);
    }

    public void Dispose()
    {
        Paths.Use(BuildConfig.Dev());
        ErFallback.Reset();
        try { Directory.Delete(tmp, true); } catch { }
    }

    [Fact]
    public void ReplayMakesTheNoEldenRingResultEqualToTheVerifiedOne()
    {
        var (full, noer, dir) = Setup(er: false);
        Assert.Equal(0, NoErPatch.Make(full, noer, dir));
        var index = System.Text.Json.JsonSerializer.Deserialize<NoErPatch.Index>(File.ReadAllText(Path.Combine(dir, NoErPatch.IndexName)),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Assert.Equal(new[] { "chr/c1.bin:patch", "only-noer.bin:delete", "only-verified.bin:add" }, index.Entries.Select(e => e.Rel + ":" + e.Op));
        Assert.True(index.Entries.Sum(e => e.Literal) < 64, "deltas copy from EV/MMV instead of carrying bytes");

        Assert.Equal(0, NoErPatch.Run());
        foreach (var rel in new[] { "chr/c1.bin", "same.bin", "only-verified.bin" })
            Assert.Equal(File.ReadAllBytes(Paths.In(full, rel)), File.ReadAllBytes(Paths.In(noer, rel)));
        Assert.False(File.Exists(Paths.In(noer, "only-noer.bin")));
    }

    [Fact]
    public void RefusesAResultThatIsNotWhatTheDeltaWasMadeFrom()
    {
        var (full, noer, dir) = Setup(er: false);
        NoErPatch.Make(full, noer, dir);
        File.WriteAllBytes(Paths.In(noer, "chr/c1.bin"), Rnd(40_000, 99));
        var ex = Assert.Throws<BuildException>(() => NoErPatch.Run());
        Assert.Contains("chr/c1.bin", ex.Message);
    }

    [Fact]
    public void NothingToDoWithEldenRing()
    {
        var (full, noer, dir) = Setup(er: true);
        NoErPatch.Make(full, noer, dir);
        var before = File.ReadAllBytes(Paths.In(noer, "chr/c1.bin"));
        Assert.Equal(0, NoErPatch.Run());
        Assert.Equal(before, File.ReadAllBytes(Paths.In(noer, "chr/c1.bin")));
    }
}
