using System.Text;
using Xunit;

namespace NRMerge.Tests;

/// <summary>Synthetic EV + MMV downloads in a temp folder, plus the manifest made from them.</summary>
public sealed class ModFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "modmanifest-" + Guid.NewGuid().ToString("N")[..8]);
    public string Ev => Path.Combine(Dir, "ELDEN VINS NIGHTREIGN");
    public string Mmv => Path.Combine(Dir, "More Map Variations 2.1.8-hotfix3 & Weapons Mod");
    public const int GameFilesPerMod = 20;

    public ModFixture()
    {
        MakeEv(Ev);
        MakeMmv(Mmv);
    }

    public static void Write(string root, string rel, string text)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, text);
    }

    public static void MakeEv(string root)
    {
        Write(root, "ELDEN VINS NIGHTREIGN.me3", "profileVersion = \"v1\"\n[[packages]]\nid = \"mod\"\n");
        Write(root, "mod/regulation.bin", "EV-REGULATION-" + new string('e', 300));
        for (int i = 0; i < GameFilesPerMod; i++) Write(root, $"mod/chr/c{i:D4}.chrbnd.dcx", $"ev chr {i} " + new string('x', i * 10));
        Write(root, "mod/dll/nighter.dll", "ev dll");
        Write(root, "mod/dll/nighter.json", "{\"a\":1}");
        Write(root, "mod/dll/NightreignFPSFOV/config.ini", "fov=90");
        Write(root, "mod/dll/cl_server_redirector.ini", "[x]");
        Write(root, "mod/dll/cl_server_redirector.log", "runtime log");
        Write(root, "mod/action/eventnameid.txt", "names");
    }

    public static void MakeMmv(string root)
    {
        Write(root, "MMV 2.1.8-hf3 & Weapons Mod.me3", "profileVersion = \"v1\"\n[[packages]]\npath = 'mod'\n");
        Write(root, "mod/regulation.bin", "MMV-REGULATION-" + new string('m', 500));
        for (int i = 0; i < GameFilesPerMod; i++) Write(root, $"mod/map/m{i:D2}.msb.dcx", $"mmv map {i} " + new string('y', i * 7));
        Write(root, "mod/dll/custom_drop_fxrs.dll", "mmv dll");
        Write(root, "mod/dll/custom_drop_fxrs.yaml", "a: 1");
        Write(root, "mod/dll/logs/custom_drop_fxrs_2026-08-13.log", "log");
        Write(root, "mod/ServerRedirector/cl_server_redirector.log", "log");
        Write(root, "mod/ServerRedirector/cl_server_redirector.ini", "[x]");
    }

    public ModManifest Manifest() => ModManifest.Parse(ModManifest.Create(Ev, Mmv));

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class ModManifestTests : IDisposable
{
    readonly ModFixture fx = new();
    public void Dispose() => fx.Dispose();

    static string P(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Create_RecordsHashedFilesPresenceOnlyConfigsAndMetadata()
    {
        var ev = fx.Manifest().Get("ev");
        Assert.Equal("Elden Vins Nightreign", ev.Name);
        Assert.Equal("https://www.nexusmods.com/eldenringnightreign/mods/287", ev.NexusUrl);
        Assert.Equal("ELDEN VINS NIGHTREIGN.me3", ev.Me3);
        var reg = ev.Files.Single(f => f.Path == "mod/regulation.bin");
        Assert.Equal(new FileInfo(P(fx.Ev, "mod/regulation.bin")).Length, reg.Size);
        Assert.Matches("^[0-9a-f]{64}$", reg.Sha256);
        var me3 = ev.Files.Single(f => f.Path == "ELDEN VINS NIGHTREIGN.me3");
        Assert.True(me3.PresenceOnly);
        Assert.Null(me3.Sha256);
        var json = ev.Files.Single(f => f.Path == "mod/dll/nighter.json");
        Assert.True(json.PresenceOnly);
        Assert.Null(json.Sha256);
        Assert.True(ev.Files.Single(f => f.Path == "mod/dll/NightreignFPSFOV/config.ini").PresenceOnly);
        Assert.False(ev.Files.Single(f => f.Path == "mod/action/eventnameid.txt").PresenceOnly);
        Assert.DoesNotContain(ev.Files, f => f.Path.EndsWith(".log"));

        var mmv = fx.Manifest().Get("mmv");
        Assert.Equal("More Map Variations 2.1.8-hotfix3 & Weapons", mmv.DisplayName);
        Assert.Equal("https://www.nexusmods.com/eldenringnightreign/mods/578", mmv.NexusUrl);
        Assert.Equal("MMV 2.1.8-hf3 & Weapons Mod.me3", mmv.Me3);
        Assert.True(mmv.Files.Single(f => f.Path == "mod/dll/custom_drop_fxrs.yaml").PresenceOnly);
        Assert.DoesNotContain(mmv.Files, f => f.Path.Contains("/logs/"));
    }

    [Fact]
    public void Create_IgnoresFilesOutsideModExceptMe3()
    {
        ModFixture.Write(fx.Ev, "readme.txt", "hello");
        Assert.DoesNotContain(fx.Manifest().Get("ev").Files, f => f.Path == "readme.txt");
    }

    [Fact]
    public void Manifest_RoundTripsThroughJson()
    {
        var m = fx.Manifest();
        var again = ModManifest.Parse(m.ToJson());
        Assert.Equal(m.Get("ev").Files.Count, again.Get("ev").Files.Count);
        Assert.Equal(m.Get("mmv").Files.Select(f => f.Sha256), again.Get("mmv").Files.Select(f => f.Sha256));
    }

    [Fact]
    public void Check_UnchangedDownloads_AreOk()
    {
        var m = fx.Manifest();
        var ev = m.Check("ev", fx.Ev);
        Assert.True(ev.Ok, ev.FormatReport());
        Assert.Equal(ModDiagnosis.Ok, ev.Kind);
        Assert.Empty(ev.Missing); Assert.Empty(ev.Extra); Assert.Empty(ev.Changed);
        Assert.True(m.Check("mmv", fx.Mmv).Ok);
    }

    [Fact]
    public void Check_MissingFile_IsReported()
    {
        var m = fx.Manifest();
        File.Delete(P(fx.Ev, "mod/chr/c0003.chrbnd.dcx"));
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/chr/c0003.chrbnd.dcx" }, r.Missing);
        Assert.Contains("re-extract", r.Diagnosis);
    }

    [Fact]
    public void Check_ExtraGameFileUnderMod_IsFatal()
    {
        var m = fx.Manifest();
        ModFixture.Write(fx.Ev, "mod/chr/c9999.chrbnd.dcx", "new");
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/chr/c9999.chrbnd.dcx" }, r.Extra);
        Assert.Equal(ModDiagnosis.Mismatch, r.Kind);
    }

    [Fact]
    public void Check_IgnorableExtras_AreWarningsOnly()
    {
        var m = fx.Manifest();
        ModFixture.Write(fx.Ev, "readme.txt", "hello");
        ModFixture.Write(fx.Ev, "mod/notes.txt", "notes");
        ModFixture.Write(fx.Ev, "mod/script/talk/x.talkesdbnd.dcx.bak", "bak");
        var r = m.Check("ev", fx.Ev);
        Assert.True(r.Ok, r.FormatReport());
        Assert.Equal(new[] { "mod/notes.txt", "mod/script/talk/x.talkesdbnd.dcx.bak", "readme.txt" }, r.IgnorableExtra.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Check_SkinsInstallerCopies_AreAccepted()
    {
        ModFixture.Write(fx.Ev, "mod/parts/bd_m_1000.partsbnd.dcx", "body part " + new string('b', 40));
        var m = fx.Manifest();
        // EV's OPEN-THIS-TO-INSTALL-SKINS.bat copies every part to its low-detail (_l) name
        File.Copy(P(fx.Ev, "mod/parts/bd_m_1000.partsbnd.dcx"), P(fx.Ev, "mod/parts/bd_m_1000_l.partsbnd.dcx"));
        var r = m.Check("ev", fx.Ev);
        Assert.True(r.Ok, r.FormatReport());
        Assert.Equal(new[] { "mod/parts/bd_m_1000_l.partsbnd.dcx" }, r.SkinsCopies);
        Assert.Empty(r.Extra);
    }

    [Fact]
    public void Check_LowDetailPartThatIsNotACopy_IsFatal()
    {
        ModFixture.Write(fx.Ev, "mod/parts/bd_m_1000.partsbnd.dcx", "body part " + new string('b', 40));
        var m = fx.Manifest();
        ModFixture.Write(fx.Ev, "mod/parts/bd_m_1000_l.partsbnd.dcx", "body part " + new string('c', 40)); // same size, other bytes
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/parts/bd_m_1000_l.partsbnd.dcx" }, r.Extra);
    }

    [Fact]
    public void Check_SameSizeDifferentBytes_IsChanged()
    {
        var m = fx.Manifest();
        var p = P(fx.Ev, "mod/chr/c0005.chrbnd.dcx");
        var b = File.ReadAllBytes(p); b[0] ^= 0x20; File.WriteAllBytes(p, b);
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/chr/c0005.chrbnd.dcx" }, r.Changed);
    }

    [Fact]
    public void Check_DifferentSize_IsChanged()
    {
        var m = fx.Manifest();
        File.AppendAllText(P(fx.Mmv, "mod/map/m04.msb.dcx"), "more");
        var r = m.Check("mmv", fx.Mmv);
        Assert.Equal(new[] { "mod/map/m04.msb.dcx" }, r.Changed);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Check_EditedConfig_IsOk()
    {
        var m = fx.Manifest();
        File.WriteAllText(P(fx.Ev, "mod/dll/nighter.json"), "{\"a\":2, \"b\": true}");
        File.WriteAllText(P(fx.Ev, "mod/dll/NightreignFPSFOV/config.ini"), "fov=110");
        File.WriteAllText(P(fx.Mmv, "mod/dll/custom_drop_fxrs.yaml"), "a: 2\nb: 3");
        Assert.True(m.Check("ev", fx.Ev).Ok);
        Assert.True(m.Check("mmv", fx.Mmv).Ok);
    }

    [Fact]
    public void Check_EditedMe3_IsOk()
    {
        // The download's profile is user-editable (ME3 managers rewrite it): presence-only
        var m = fx.Manifest();
        File.WriteAllText(P(fx.Ev, "ELDEN VINS NIGHTREIGN.me3"), "profileVersion = \"v1\"\n# edited by a manager\n");
        var r = m.Check("ev", fx.Ev);
        Assert.True(r.Ok, r.FormatReport());
        Assert.Empty(r.Changed);
    }

    [Fact]
    public void Check_MissingMe3_IsStillMissing()
    {
        var m = fx.Manifest();
        File.Delete(P(fx.Mmv, "MMV 2.1.8-hf3 & Weapons Mod.me3"));
        var r = m.Check("mmv", fx.Mmv);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "MMV 2.1.8-hf3 & Weapons Mod.me3" }, r.Missing);
        Assert.Empty(r.IgnorableMissing);
    }

    [Fact]
    public void Check_MissingIgnorableFiles_AreWarningsOnly()
    {
        ModFixture.Write(fx.Ev, "mod/readme_ev.dat", "readme");
        ModFixture.Write(fx.Ev, "mod/.smithbox/project.dat", "smithbox");
        ModFixture.Write(fx.Ev, "mod/docs/guide.pdf", "pdf");
        var m = fx.Manifest();
        File.Delete(P(fx.Ev, "mod/action/eventnameid.txt"));
        File.Delete(P(fx.Ev, "mod/readme_ev.dat"));
        File.Delete(P(fx.Ev, "mod/.smithbox/project.dat"));
        File.Delete(P(fx.Ev, "mod/docs/guide.pdf"));
        var r = m.Check("ev", fx.Ev);
        Assert.True(r.Ok, r.FormatReport());
        Assert.Empty(r.Missing);
        Assert.Equal(new[] { "mod/.smithbox/project.dat", "mod/action/eventnameid.txt", "mod/docs/guide.pdf", "mod/readme_ev.dat" }, r.IgnorableMissing);
        Assert.Contains("4 non-game file(s) missing", r.Diagnosis);
        var text = r.FormatReport();
        Assert.Contains("Missing non-game files (warning) (4):", text);
        Assert.Contains("mod/action/eventnameid.txt", text);
    }

    [Fact]
    public void Check_MissingIgnorableAndGameFile_ReportsBoth()
    {
        var m = fx.Manifest();
        File.Delete(P(fx.Ev, "mod/action/eventnameid.txt"));
        File.Delete(P(fx.Ev, "mod/chr/c0002.chrbnd.dcx"));
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/chr/c0002.chrbnd.dcx" }, r.Missing);
        Assert.Equal(new[] { "mod/action/eventnameid.txt" }, r.IgnorableMissing);
    }

    [Theory]
    [InlineData("mod/readme.dat", true)]
    [InlineData("mod/sub/ReadMe First.bin", true)]
    [InlineData("mod/a/notes.TXT", true)]
    [InlineData("mod/x.png", true)]
    [InlineData("mod/x.dcx.bak", true)]
    [InlineData("mod/.smithbox/anything.bin", true)]
    [InlineData("mod/regulation.bin", false)]
    [InlineData("mod/dll/nighter.json", false)]
    [InlineData("mod/dll/config.ini", false)]
    [InlineData("readme.txt", false)]
    [InlineData("ELDEN VINS NIGHTREIGN.me3", false)]
    public void IsIgnorableMissing_ClassifiesPaths(string rel, bool expected) =>
        Assert.Equal(expected, ModManifest.IsIgnorableMissing(rel));

    [Fact]
    public void Check_MissingConfig_IsMissing()
    {
        var m = fx.Manifest();
        File.Delete(P(fx.Ev, "mod/dll/nighter.json"));
        var r = m.Check("ev", fx.Ev);
        Assert.False(r.Ok);
        Assert.Equal(new[] { "mod/dll/nighter.json" }, r.Missing);
    }

    [Fact]
    public void Check_RuntimeLogs_AreIgnored()
    {
        var m = fx.Manifest();
        ModFixture.Write(fx.Mmv, "mod/dll/logs/custom_drop_fxrs_2026-10-01.log", "new log");
        ModFixture.Write(fx.Mmv, "mod/dll/logs/sub/trace.txt", "nested runtime file");
        ModFixture.Write(fx.Ev, "mod/dll/logs/x.log", "log");
        File.AppendAllText(P(fx.Ev, "mod/dll/cl_server_redirector.log"), "grew");
        File.Delete(P(fx.Mmv, "mod/ServerRedirector/cl_server_redirector.log"));
        var mmv = m.Check("mmv", fx.Mmv);
        Assert.True(mmv.Ok, mmv.FormatReport());
        Assert.Contains("mod/dll/logs/sub/trace.txt", mmv.IgnoredRuntime);
        var ev = m.Check("ev", fx.Ev);
        Assert.True(ev.Ok, ev.FormatReport());
        Assert.Contains("mod/dll/cl_server_redirector.log", ev.IgnoredRuntime);
    }

    [Fact]
    public void Check_SwappedFolders_AreDiagnosed()
    {
        var m = fx.Manifest();
        var r = m.Check("ev", fx.Mmv);
        Assert.False(r.Ok);
        Assert.Equal(ModDiagnosis.Swapped, r.Kind);
        Assert.Contains("This folder is More Map Variations, not Elden Vins", r.Diagnosis);
        Assert.Contains("swap", r.Diagnosis);
        Assert.Equal(ModDiagnosis.Swapped, m.Check("mmv", fx.Ev).Kind);
    }

    [Fact]
    public void Check_SwappedModFolderWithoutMe3_IsDiagnosedByRegulation()
    {
        var m = fx.Manifest();
        File.Delete(P(fx.Mmv, "MMV 2.1.8-hf3 & Weapons Mod.me3"));
        Assert.Equal(ModDiagnosis.Swapped, m.Check("ev", Path.Combine(fx.Mmv, "mod")).Kind);
    }

    [Fact]
    public void Check_MergedOutputFolder_IsDiagnosed()
    {
        var m = fx.Manifest();
        var merged = Path.Combine(fx.Dir, "Elden Vins with more map variations");
        ModFixture.Write(merged, "Elden Vins with more map variations.me3", "x");
        ModFixture.Write(merged, "mod/regulation.bin", "merged");
        var r = m.Check("ev", merged);
        Assert.Equal(ModDiagnosis.MergedOutput, r.Kind);
        Assert.Contains("merged mod, not the original download", r.Diagnosis);
        Assert.Equal(ModDiagnosis.MergedOutput, m.Check("mmv", Path.Combine(merged, "mod")).Kind);
    }

    [Fact]
    public void Check_FolderWithMergeReport_IsMergedOutput()
    {
        var m = fx.Manifest();
        ModFixture.Write(fx.Ev, "MERGE_REPORT.md", "# report");
        Assert.Equal(ModDiagnosis.MergedOutput, m.Check("ev", fx.Ev).Kind);
    }

    [Fact]
    public void Check_FolderWithMergeJournal_IsMergedOutput()
    {
        var m = fx.Manifest();
        Directory.CreateDirectory(Path.Combine(fx.Mmv, "merge-journal"));
        Assert.Equal(ModDiagnosis.MergedOutput, m.Check("mmv", fx.Mmv).Kind);
    }

    [Fact]
    public void Check_PartialDownload_SaysReExtract()
    {
        var m = fx.Manifest();
        for (int i = 0; i < 15; i++) File.Delete(P(fx.Ev, $"mod/chr/c{i:D4}.chrbnd.dcx"));
        File.Delete(P(fx.Ev, "mod/regulation.bin"));
        var r = m.Check("ev", fx.Ev);
        Assert.Equal(ModDiagnosis.Partial, r.Kind);
        Assert.Equal(16, r.Missing.Count);
        Assert.Contains("re-extract", r.Diagnosis);
    }

    [Fact]
    public void Check_DifferentVersion_IsDiagnosedWithLink()
    {
        var m = fx.Manifest();
        File.WriteAllText(P(fx.Ev, "mod/regulation.bin"), "EV-REGULATION-NEWER-" + new string('e', 310));
        for (int i = 0; i < 3; i++) File.AppendAllText(P(fx.Ev, $"mod/chr/c{i:D4}.chrbnd.dcx"), "v2");
        ModFixture.Write(fx.Ev, "mod/chr/c5000.chrbnd.dcx", "new in v2");
        var r = m.Check("ev", fx.Ev);
        Assert.Equal(ModDiagnosis.WrongVersion, r.Kind);
        Assert.Contains("different version of Elden Vins", r.Diagnosis);
        Assert.Contains("https://www.nexusmods.com/eldenringnightreign/mods/287", r.Diagnosis);
        Assert.Equal(4, r.Changed.Count);
    }

    [Fact]
    public void Check_ManyChangedFilesWithoutRegulation_IsWrongVersion()
    {
        var m = fx.Manifest();
        for (int i = 0; i < 6; i++) File.AppendAllText(P(fx.Mmv, $"mod/map/m{i:D2}.msb.dcx"), "v2");
        var r = m.Check("mmv", fx.Mmv);
        Assert.Equal(ModDiagnosis.WrongVersion, r.Kind);
        Assert.Contains("More Map Variations 2.1.8-hotfix3 & Weapons", r.Diagnosis);
    }

    [Fact]
    public void Check_ModFolderInput_ResolvesToRoot()
    {
        var m = fx.Manifest();
        var r = m.Check("ev", Path.Combine(fx.Ev, "mod"));
        Assert.True(r.Ok, r.FormatReport());
        Assert.Equal(Path.GetFullPath(fx.Ev), r.Root);
    }

    [Fact]
    public void Check_NestedExtraFolder_IsAccepted()
    {
        var m = fx.Manifest();
        var wrapper = Path.Combine(fx.Dir, "EV-download-287-1-0");
        Directory.CreateDirectory(wrapper);
        Directory.Move(fx.Ev, Path.Combine(wrapper, "ELDEN VINS NIGHTREIGN"));
        var r = m.Check("ev", wrapper);
        Assert.True(r.Ok, r.FormatReport());
        Assert.Equal(Path.Combine(wrapper, "ELDEN VINS NIGHTREIGN"), r.Root);
    }

    [Fact]
    public void Check_NonexistentFolder_IsNotFound()
    {
        var r = fx.Manifest().Check("ev", Path.Combine(fx.Dir, "nope"));
        Assert.Equal(ModDiagnosis.NotFound, r.Kind);
        Assert.Contains("does not exist", r.Diagnosis);
    }

    [Fact]
    public void Check_EmptyFolder_IsEmpty()
    {
        var empty = Path.Combine(fx.Dir, "empty");
        Directory.CreateDirectory(empty);
        var r = fx.Manifest().Check("ev", empty);
        Assert.Equal(ModDiagnosis.Empty, r.Kind);
        Assert.Contains("empty", r.Diagnosis);
        Assert.Contains("https://www.nexusmods.com/eldenringnightreign/mods/287", r.Diagnosis);
    }

    [Fact]
    public void Check_ReportsProgressUpToTotals()
    {
        var m = fx.Manifest();
        var seen = new List<HashProgress>();
        var p = new SyncProgress(seen);
        m.Check("ev", fx.Ev, p);
        Assert.NotEmpty(seen);
        var last = seen.MaxBy(x => x.FilesDone);
        Assert.Equal(last.FilesTotal, last.FilesDone);
        Assert.Equal(last.BytesTotal, seen.Max(x => x.BytesDone));
        Assert.True(last.BytesTotal > 0);
    }

    [Fact]
    public void Check_SizeMismatch_IsNotHashed()
    {
        var m = fx.Manifest();
        int hashable = m.Get("ev").Files.Count(f => !f.PresenceOnly);
        File.AppendAllText(P(fx.Ev, "mod/chr/c0001.chrbnd.dcx"), "grown");
        var seen = new List<HashProgress>();
        var r = m.Check("ev", fx.Ev, new SyncProgress(seen));
        Assert.Equal(new[] { "mod/chr/c0001.chrbnd.dcx" }, r.Changed);
        Assert.Equal(hashable - 1, seen[^1].FilesTotal);
    }

    [Fact]
    public void FormatReport_ListsCountsAndTruncates()
    {
        var m = fx.Manifest();
        for (int i = 0; i < 12; i++) File.Delete(P(fx.Ev, $"mod/chr/c{i:D4}.chrbnd.dcx"));
        File.AppendAllText(P(fx.Ev, "mod/chr/c0015.chrbnd.dcx"), "x");
        var text = m.Check("ev", fx.Ev).FormatReport(5);
        Assert.Contains("Missing (12)", text);
        Assert.Contains("Changed (1)", text);
        Assert.Contains("... and 7 more", text);
        Assert.Contains("mod/chr/c0015.chrbnd.dcx", text);
        Assert.Contains("Elden Vins Nightreign", text);
    }

    sealed class SyncProgress(List<HashProgress> sink) : IProgress<HashProgress>
    {
        public void Report(HashProgress value) { lock (sink) sink.Add(value); }
    }
}
