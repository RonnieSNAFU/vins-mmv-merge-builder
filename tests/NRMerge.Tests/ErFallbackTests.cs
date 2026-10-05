using Xunit;

namespace NRMerge.Tests;

/// <summary>Building without Elden Ring: every Elden-Ring-based lookup falls back and is journaled; with Elden Ring nothing changes.</summary>
[Collection("Paths")]
public class ErFallbackTests : IDisposable
{
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-noer-" + Guid.NewGuid().ToString("N")[..8]);
    const string ErRel = "chr/c4290.anibnd.dcx";      // an Elden Ring base file of the verified build
    const string NrRel = "chr/c0000.anibnd.dcx";

    BuildConfig Cfg(bool er)
    {
        var data = Path.Combine(tmp, "Data");
        Directory.CreateDirectory(data);
        File.WriteAllLines(Path.Combine(data, "vanilla-manifest.tsv"), new[]
        {
            "game\trel\tsha256\tsize", $"NR\t{NrRel}\t{new string('1', 64)}\t3", $"ER\t{ErRel}\t{new string('2', 64)}\t3", $"ER\tchr/c4291.anibnd.dcx\t{new string('3', 64)}\t3",
        });
        var cfg = new BuildConfig { WorkDir = Path.Combine(tmp, "w"), DataDir = data, NrGame = Path.Combine(tmp, "nr"), ErGame = er ? Path.Combine(tmp, "er") : null };
        Paths.Use(cfg);
        foreach (var (dir, rel) in new[] { (Paths.Vanilla, NrRel), (Paths.VanillaER, ErRel) })
        {
            var f = Paths.In(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, "abc");
        }
        return cfg;
    }

    public ErFallbackTests() => ErFallback.Reset();

    public void Dispose()
    {
        Paths.Use(BuildConfig.Dev());
        ErFallback.Reset();
        try { Directory.Delete(tmp, true); } catch { }
    }

    [Fact]
    public void WithEldenRing_BaseOfUsesTheElderRingOriginalAndJournalsNothing()
    {
        Cfg(er: true);
        Assert.True(Paths.HasEldenRing);
        Assert.Equal(Paths.In(Paths.VanillaER, ErRel), Paths.BaseOf(ErRel));
        Assert.Equal(Paths.In(Paths.Vanilla, NrRel), Paths.BaseOf(NrRel));
        Assert.Null(EnemyMerge.ErTae(NrRel, "x.tae"));   // NR base exists: no ER fallback either way
        Assert.Empty(ErFallback.Entries);
        Journal.Dir = Path.Combine(tmp, "journal");
        Journal.Clear();
        Journal.Save();
        Assert.False(File.Exists(Path.Combine(tmp, "journal", "no-eldenring.tsv")));
    }

    [Fact]
    public void WithoutEldenRing_BaseOfFallsBackToNoBaseAndJournalsIt_EvenIfAStaleErFileExists()
    {
        Cfg(er: false);
        Assert.False(Paths.HasEldenRing);
        Assert.Null(Paths.BaseOf(ErRel));
        Assert.Null(Paths.BaseOf("/" + ErRel));             // same file, other spelling: one journal line
        Assert.Equal(Paths.In(Paths.Vanilla, NrRel), Paths.BaseOf(NrRel));
        Assert.Null(Paths.BaseOf("chr/c9999.anibnd.dcx"));  // no base in the verified build either: nothing to journal
        var e = Assert.Single(ErFallback.Entries);
        Assert.Equal(ErRel, e.Item);
        Assert.StartsWith("no Elden Ring base:", e.Decision);
    }

    [Fact]
    public void RowsBothModsAdded_WithEldenRingUseTheErRegulation_WithoutItAreComparedBySimilarityAndJournaled()
    {
        Cfg(er: true);
        Assert.True(RegMerge.HasErOriginal("NpcParam", 42900000, true));
        Assert.False(RegMerge.HasErOriginal("NpcParam", 42900001, false));
        Assert.Empty(ErFallback.Entries);
        Cfg(er: false);
        Assert.True(RegMerge.HasErOriginal("NpcParam", 42900001, false));
        Assert.Contains(ErFallback.Entries, x => x.Item == "NpcParam 42900001" && x.Decision.Contains("similarity"));
        // without a common original the rows would always be an unrelated collision; assumed one -> similar rows merge
        var e = new Dictionary<string, object> { ["hp"] = 100, ["a"] = 1, ["b"] = 2 };
        var m = new Dictionary<string, object> { ["hp"] = 150, ["a"] = 1, ["b"] = 2 };
        Assert.True(RegMerge.IsCollisionCore("NpcParam", e, m, false, out _));
        Assert.False(RegMerge.IsCollisionCore("NpcParam", e, m, RegMerge.HasErOriginal("NpcParam", 1, false), out _));
    }

    [Fact]
    public void WithoutEldenRing_AssembleClassifiesByTheErOriginalsManifestHash_LikeTheVerifiedBuild()
    {
        Cfg(er: false);
        var orig = System.Text.Encoding.ASCII.GetBytes("er original");
        var manifest = Path.Combine(tmp, "Data", "vanilla-manifest.tsv");
        File.AppendAllText(manifest, $"ER\tsfx/x.ffxbnd.dcx\t{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(orig))}\t{orig.Length}\n");
        ErFallback.Reset();
        string F(string name, byte[] data) { var p = Path.Combine(tmp, name); File.WriteAllBytes(p, data); return p; }
        var o = F("o", orig); var a = F("a", new byte[] { 1 }); var b = F("b", new byte[] { 2 });
        Assert.Equal("take-mmv", Assemble.ClassifyWithoutEldenRing("sfx/x.ffxbnd.dcx", o, a));
        Assert.Equal("take-ev", Assemble.ClassifyWithoutEldenRing("sfx/x.ffxbnd.dcx", a, o));
        Assert.Equal("merge", Assemble.ClassifyWithoutEldenRing("sfx/x.ffxbnd.dcx", a, b));
        Assert.Equal("identical", Assemble.ClassifyWithoutEldenRing("sfx/x.ffxbnd.dcx", a, a));
        Assert.Contains(ErFallback.Entries, x => x.Item == "sfx/x.ffxbnd.dcx" && x.Decision.Contains("take-mmv decided by"));
        Assert.Equal("merge", Assemble.ClassifyWithoutEldenRing("sfx/other.ffxbnd.dcx", a, b));   // no base at all: unchanged behaviour
    }

    [Fact]
    public void AnAiScriptWithoutBase_KeepsEvsScriptByOwnerRuleAndIsJournaled()
    {
        Cfg(er: false);
        Journal.Clear();
        Assert.Null(EnemyMerge.MergeLua("script/c4290.luabnd.dcx", "c4290_battle.lua", null, new byte[] { 1 }, new byte[] { 2 }));
        Assert.Equal(1, Journal.Count("ai"));
        Assert.Contains(ErFallback.Entries, x => x.Item.Contains("c4290_battle.lua") && x.Decision.Contains("EV's script kept"));
    }

    [Fact]
    public void WithoutEldenRing_TheTaeFallbackBaseIsSkippedAndJournaled()
    {
        Cfg(er: false);
        Assert.Null(EnemyMerge.ErTae("chr/c4291.anibnd.dcx", "a00.tae"));
        Assert.Contains(ErFallback.Entries, x => x.Item == "chr/c4291.anibnd.dcx" && x.Decision.Contains("TAE"));
    }

    [Fact]
    public void WithoutEldenRing_TheFallbacksAreWrittenWithTheStageJournal()
    {
        Cfg(er: false);
        Paths.BaseOf(ErRel);
        Journal.Clear();
        Journal.Add("assembly", "x", "y");
        Journal.Save();
        Journal.Clear();          // the next stage starts a new journal; the fallbacks stay
        Journal.Add("player", "x", "y");
        Journal.Save();
        var lines = File.ReadAllLines(Path.Combine(Journal.Dir, "no-eldenring.tsv"));
        Assert.Equal("item\tdecision", lines[0]);
        Assert.Contains(lines, l => l.StartsWith(ErRel + "\tno Elden Ring base:"));
    }

    [Fact]
    public void WithoutEldenRing_VanillaExtractSkipsTheElderRingRows()
    {
        Cfg(er: false);
        var m = VanillaManifest.Load(VanillaManifest.DefaultPath);
        var read = new List<string>();
        var problems = m.ExtractTo((game, rel) => { read.Add(game + " " + rel); return null; }, Path.Combine(tmp, "v"), null, null);
        Assert.Equal(new[] { "NR " + NrRel }, read);
        Assert.Single(problems);   // the fake reader returned nothing for the NR row
    }

    [Fact]
    public void WithoutEldenRing_OodleStagesOnlyNightreignsDllAndDropsAStaleOne()
    {
        var nr = Path.Combine(tmp, "nrgame");
        Directory.CreateDirectory(nr);
        File.WriteAllText(Path.Combine(nr, Oodle.NrDll), "nine");
        var dir = Path.Combine(tmp, "oodle");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Oodle.ErDll), "stale six");
        Oodle.Stage(nr, null, dir);
        Assert.True(File.Exists(Path.Combine(dir, Oodle.NrDll)));
        Assert.False(File.Exists(Path.Combine(dir, Oodle.ErDll)));
        Assert.Contains("oo2core_9", Assert.Throws<BuildException>(() => Oodle.Stage(Path.Combine(tmp, "none"), null, dir)).Message);
    }
}
