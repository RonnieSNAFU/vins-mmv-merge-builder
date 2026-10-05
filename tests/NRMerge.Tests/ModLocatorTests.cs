using System.IO.Compression;
using Xunit;

namespace NRMerge.Tests;

public class ModLocatorTests : IDisposable
{
    readonly ModFixture fx = new();
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-loc-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { fx.Dispose(); try { Directory.Delete(tmp, true); } catch { } }

    ModSpec Ev => fx.Manifest().Get("ev");
    const string EvMe3 = "ELDEN VINS NIGHTREIGN.me3";

    static void CopyDir(string src, string dst)
    {
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var d = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d));
            File.Copy(f, d);
        }
    }

    // ---------------------------------------------------------------- .me3 / mod folder normalisation

    [Fact]
    public void TheMe3FileTheModFolderAndTheDownloadRootAllNormaliseToTheRoot()
    {
        Assert.Equal(fx.Ev, ModLocator.NormalizeFolder(Path.Combine(fx.Ev, EvMe3), EvMe3));
        Assert.Equal(fx.Ev, ModLocator.NormalizeFolder(Path.Combine(fx.Ev, "mod"), EvMe3));
        Assert.Equal(fx.Ev, ModLocator.NormalizeFolder(fx.Ev, EvMe3));
        Assert.Equal(fx.Ev, ModLocator.NormalizeFolder(fx.Ev + "\\", EvMe3));
        // archive extracted into an extra folder
        var outer = Path.Combine(tmp, "outer");
        CopyDir(fx.Ev, Path.Combine(outer, "ELDEN VINS NIGHTREIGN"));
        Assert.Equal(Path.Combine(outer, "ELDEN VINS NIGHTREIGN"), ModLocator.NormalizeFolder(outer, EvMe3));
    }

    [Fact]
    public void GivenTheMe3FolderOrTheModFolder_TheBuilderUsesTheSameModFolder()
    {
        var env = new BuilderEnv { Interactive = false, Out = new StringWriter() };
        var a = Builder.LocateMod(Ev, Path.Combine(fx.Ev, EvMe3), env, Array.Empty<string>(), null, tmp);
        var b = Builder.LocateMod(Ev, Path.Combine(fx.Ev, "mod"), env, Array.Empty<string>(), null, tmp);
        Assert.Equal(fx.Ev, a);
        Assert.Equal(fx.Ev, b);
    }

    // ---------------------------------------------------------------- archives

    [Fact]
    public void ArchivesAreRecognisedByExtension()
    {
        Assert.True(ModLocator.IsArchive(@"C:\d\EV-287-1.zip"));
        Assert.True(ModLocator.IsArchive(@"C:\d\MMV.7Z"));
        Assert.True(ModLocator.IsArchive(@"C:\d\x.rar"));
        Assert.False(ModLocator.IsArchive(@"C:\d\x.me3"));
        Assert.False(ModLocator.IsArchive(@"C:\d\folder"));
    }

    [Fact]
    public void ZipIsDetectedAndExtractedToACheckableDownload()
    {
        Directory.CreateDirectory(tmp);
        var zip = Path.Combine(tmp, "ELDEN VINS NIGHTREIGN-287-1-0.zip");
        ZipFile.CreateFromDirectory(fx.Ev, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        Assert.True(ModLocator.ArchiveContains(zip, EvMe3));
        Assert.False(ModLocator.ArchiveContains(zip, "MMV 2.1.8-hf3 & Weapons Mod.me3"));

        var root = ModLocator.Extract(zip, Path.Combine(tmp, "x", "ev"), EvMe3);
        Assert.True(File.Exists(Path.Combine(root, EvMe3)));
        Assert.True(fx.Manifest().Check("ev", root).Ok);
    }

    [Fact]
    public void SevenZipIsDetectedAndExtracted()
    {
        Directory.CreateDirectory(tmp);
        var sz = Path.Combine(tmp, "More Map Variations-578-2-1-8.7z");
        SevenZipFixture.Write(sz, SevenZipFixture.FilesOf(fx.Mmv, Path.GetDirectoryName(fx.Mmv)));
        const string me3 = "MMV 2.1.8-hf3 & Weapons Mod.me3";
        Assert.True(ModLocator.ArchiveContains(sz, me3));
        var root = ModLocator.Extract(sz, Path.Combine(tmp, "x", "mmv"), me3);
        Assert.Equal(File.ReadAllBytes(Path.Combine(fx.Mmv, "mod", "regulation.bin")), File.ReadAllBytes(Path.Combine(root, "mod", "regulation.bin")));
        Assert.True(fx.Manifest().Check("mmv", root).Ok);
    }

    [Fact]
    public void ABrokenArchiveIsNotACandidateAndExtractingItExplainsWhatToDo()
    {
        Directory.CreateDirectory(tmp);
        var bad = Path.Combine(tmp, "broken.zip");
        File.WriteAllText(bad, "not a zip");
        Assert.False(ModLocator.ArchiveContains(bad, EvMe3));
        var e = Assert.Throws<BuildException>(() => ModLocator.Extract(bad, Path.Combine(tmp, "x"), EvMe3));
        Assert.Contains("Extract it yourself", e.Message);
    }

    [Fact]
    public void AnArchiveWithoutTheModsMe3IsRejected()
    {
        Directory.CreateDirectory(tmp);
        var zip = Path.Combine(tmp, "other.zip");
        ZipFile.CreateFromDirectory(fx.Mmv, zip);
        var e = Assert.Throws<BuildException>(() => ModLocator.Extract(zip, Path.Combine(tmp, "x"), EvMe3));
        Assert.Contains("not the expected mod download", e.Message);
    }

    // ---------------------------------------------------------------- detection and ranking

    [Fact]
    public void FindsFoldersAndArchives_RanksTheVerifiedFolderInsideNightreignFirst()
    {
        var nr = Path.Combine(tmp, "steamapps", "common", "ELDEN RING NIGHTREIGN");
        var downloads = Path.Combine(tmp, "Downloads");
        CopyDir(fx.Ev, Path.Combine(nr, "ELDEN VINS NIGHTREIGN"));
        Directory.CreateDirectory(downloads);
        ZipFile.CreateFromDirectory(fx.Ev, Path.Combine(downloads, "ev.zip"), CompressionLevel.Fastest, true);
        // an older copy with another regulation.bin in Documents
        var old = Path.Combine(tmp, "Documents", "mods", "EV old");
        CopyDir(fx.Ev, old);
        File.WriteAllText(Path.Combine(old, "mod", "regulation.bin"), "older");

        var ranked = ModLocator.Find(Ev, new[] { nr, downloads, Path.Combine(tmp, "Documents") }, nr);
        Assert.Equal(3, ranked.Count);
        Assert.Equal(Path.Combine(nr, "ELDEN VINS NIGHTREIGN"), ranked[0].Path);
        Assert.Equal(7, ranked[0].Score);
        Assert.Equal(old, ranked[1].Path);
        Assert.True(ranked[2].IsArchive);
        Assert.Same(ranked[0], ModLocator.Choose(ranked, out var ambiguous));
        Assert.False(ambiguous);
    }

    [Fact]
    public void TheMergedOutputAndTheOutputFolderAreNeverCandidates()
    {
        var root = Path.Combine(tmp, "r");
        var merged = Path.Combine(root, "merged");
        CopyDir(fx.Ev, merged);
        File.WriteAllText(Path.Combine(merged, ModManifest.MergedMe3), "x");
        var output = Path.Combine(root, "out");
        CopyDir(fx.Ev, Path.Combine(output, "_build", "mods", "ev"));
        Assert.Empty(ModLocator.Find(Ev, new[] { root }, null, new[] { output }));
    }

    [Fact]
    public void TwoEquallyGoodCopiesAreAmbiguous_InteractiveAsksWithThePicker()
    {
        var a = Path.Combine(tmp, "A", "EV");
        var b = Path.Combine(tmp, "B", "EV");
        CopyDir(fx.Ev, a);
        CopyDir(fx.Ev, b);
        var roots = new[] { Path.Combine(tmp, "A"), Path.Combine(tmp, "B") };
        var ranked = ModLocator.Find(Ev, roots);
        Assert.Null(ModLocator.Choose(ranked, out var ambiguous));
        Assert.True(ambiguous);

        var asked = new List<string>();
        var env = new BuilderEnv { Out = new StringWriter(), PickFile = (title, filter) => { asked.Add(title); return Path.Combine(b, EvMe3); } };
        Assert.Equal(b, Builder.LocateMod(Ev, null, env, roots, null, Path.Combine(tmp, "out")));
        Assert.Single(asked);
        Assert.Contains("several places", env.Out.ToString());
    }

    [Fact]
    public void AmbiguityInNonInteractiveModeIsAnErrorNamingTheCandidatesAndTheOption()
    {
        var a = Path.Combine(tmp, "A", "EV");
        var b = Path.Combine(tmp, "B", "EV");
        CopyDir(fx.Ev, a);
        CopyDir(fx.Ev, b);
        var env = new BuilderEnv { Interactive = false, Out = new StringWriter(), PickFile = (_, _) => throw new Exception("no dialogs") };
        var e = Assert.Throws<BuildException>(() => Builder.LocateMod(Ev, null, env, new[] { Path.Combine(tmp, "A"), Path.Combine(tmp, "B") }, null, Path.Combine(tmp, "o")));
        Assert.Contains(a, e.Message);
        Assert.Contains(b, e.Message);
        Assert.Contains("--ev", e.Message);
    }

    [Fact]
    public void NothingFound_AsksForAFile_PickingAnArchiveReturnsTheArchive()
    {
        Directory.CreateDirectory(tmp);
        var zip = Path.Combine(tmp, "ev.zip");
        ZipFile.CreateFromDirectory(fx.Ev, zip);
        var env = new BuilderEnv { Out = new StringWriter(), PickFile = (_, filter) => { Assert.Contains("*.7z", filter); return zip; } };
        Assert.Equal(zip, Builder.LocateMod(Ev, null, env, new[] { Path.Combine(tmp, "empty") }, null, Path.Combine(tmp, "o")));
        Assert.Contains("was not found", env.Out.ToString());
    }
}
