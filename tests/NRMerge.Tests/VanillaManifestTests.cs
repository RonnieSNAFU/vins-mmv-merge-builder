using System.Security.Cryptography;
using System.Text;
using NRMerge;
using Xunit;

namespace NRMerge.Tests;

[Collection("Paths")]
public class VanillaManifestTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "nrm-vm-" + Guid.NewGuid().ToString("N"));
    string Nr => Path.Combine(root, "vanilla");
    string Er => Path.Combine(root, "vanilla_er");

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    static void Put(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, text);
    }

    static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>A vanilla/vanilla_er pair like the real one: NR files, one absent NR file, ER files, an empty ER absent list.</summary>
    void MakeVanilla()
    {
        Put(Nr, "chr/c0000.anibnd.dcx", "nr-c0000");
        Put(Nr, "action/script/c0000.hks", "nr-hks");
        Put(Nr, "_not_in_vanilla.txt", "chr/c0000_a7x.anibnd.dcx\r\n");
        Put(Er, "chr/c2010.anibnd.dcx", "er-c2010");
        Put(Er, "_not_in_vanilla.txt", "");
    }

    [Fact]
    public void CreateListsEveryFileWithHashAndSizeAndKeepsAbsentRows()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        Assert.Equal(4, m.Entries.Count);
        var c0000 = m.Entries.Single(e => e.Rel == "chr/c0000.anibnd.dcx");
        Assert.Equal("NR", c0000.Game);
        Assert.Equal(Sha("nr-c0000"), c0000.Sha256);
        Assert.Equal(8, c0000.Size);
        Assert.Equal("ER", m.Entries.Single(e => e.Rel == "chr/c2010.anibnd.dcx").Game);
        var absent = m.Entries.Single(e => e.Rel == "chr/c0000_a7x.anibnd.dcx");
        Assert.True(absent.Absent);
        Assert.Equal("NR", absent.Game);
        Assert.DoesNotContain(m.Entries, e => e.Rel.EndsWith("_not_in_vanilla.txt"));
    }

    [Fact]
    public void SaveLoadRoundTrip()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        var path = Path.Combine(root, "vanilla-manifest.tsv");
        m.Save(path);
        Assert.StartsWith("game\trel\tsha256\tsize", File.ReadAllText(path));
        var back = VanillaManifest.Load(path);
        Assert.Equal(m.Entries, back.Entries);
    }

    [Fact]
    public void CheckReportsMissingChangedExtraAndPresentAbsentFiles()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        Assert.Empty(m.Check(Nr, Er));
        File.Delete(Path.Combine(Nr, "action", "script", "c0000.hks"));
        Put(Er, "chr/c2010.anibnd.dcx", "changed");
        Put(Er, "chr/c9999.anibnd.dcx", "extra");
        Put(Nr, "chr/c0000_a7x.anibnd.dcx", "should not exist");
        var problems = m.Check(Nr, Er);
        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("missing") && p.Contains("action/script/c0000.hks"));
        Assert.Contains(problems, p => p.Contains("changed") && p.Contains("chr/c2010.anibnd.dcx"));
        Assert.Contains(problems, p => p.Contains("extra") && p.Contains("chr/c9999.anibnd.dcx"));
        Assert.Contains(problems, p => p.Contains("should be absent") && p.Contains("chr/c0000_a7x.anibnd.dcx"));
    }

    [Fact]
    public void ExtractToWritesEveryListedFileFromTheRightGameAndTheAbsentLists()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        var dst = Path.Combine(root, "x");
        var reads = new List<string>();
        byte[] Read(string game, string rel)
        {
            reads.Add(game + ":" + rel);
            return File.ReadAllBytes(Path.Combine(game == "NR" ? Nr : Er, rel.Replace('/', Path.DirectorySeparatorChar)));
        }
        var problems = m.ExtractTo(Read, Path.Combine(dst, "vanilla"), Path.Combine(dst, "vanilla_er"), null);
        Assert.Empty(problems);
        Assert.Equal(new[] { "NR:action/script/c0000.hks", "NR:chr/c0000.anibnd.dcx", "ER:chr/c2010.anibnd.dcx" }.Order(), reads.Order());
        Assert.Empty(m.Check(Path.Combine(dst, "vanilla"), Path.Combine(dst, "vanilla_er")));
        Assert.Equal(File.ReadAllLines(Path.Combine(Nr, "_not_in_vanilla.txt")), File.ReadAllLines(Path.Combine(dst, "vanilla", "_not_in_vanilla.txt")));
        Assert.Empty(File.ReadAllLines(Path.Combine(dst, "vanilla_er", "_not_in_vanilla.txt")));
    }

    [Fact]
    public void ExtractToSkipsFilesAlreadyPresentWithTheRightHash()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        var reads = 0;
        var problems = m.ExtractTo((g, r) => { reads++; return null; }, Nr, Er, null);
        Assert.Empty(problems);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ExtractToReportsArchiveFilesThatAreMissingOrDiffer()
    {
        MakeVanilla();
        var m = VanillaManifest.Create(Nr, Er);
        var dst = Path.Combine(root, "x");
        byte[] Read(string game, string rel) => rel switch
        {
            "chr/c0000.anibnd.dcx" => null,
            "chr/c2010.anibnd.dcx" => Encoding.UTF8.GetBytes("other version"),
            _ => Encoding.UTF8.GetBytes("nr-hks"),
        };
        var problems = m.ExtractTo(Read, Path.Combine(dst, "vanilla"), Path.Combine(dst, "vanilla_er"), null);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("chr/c0000.anibnd.dcx") && p.Contains("not found"));
        Assert.Contains(problems, p => p.Contains("chr/c2010.anibnd.dcx") && p.Contains("differs"));
        // a file whose hash is wrong is not left behind for later stages
        Assert.False(File.Exists(Path.Combine(dst, "vanilla_er", "chr", "c2010.anibnd.dcx")));
    }

    [Fact]
    public void ShippedManifestKeepsTheTwoAbsentNightreignFiles()
    {
        var m = VanillaManifest.Load(VanillaManifest.DefaultPath);
        var absent = m.Entries.Where(e => e.Absent).Select(e => e.Game + ":" + e.Rel).Order().ToArray();
        Assert.Equal(new[] { "NR:chr/c0000_a7x.anibnd.dcx", "NR:chr/c0000_a8x.anibnd.dcx" }, absent);
        Assert.Equal(368, m.Entries.Count(e => e.Game == "NR" && !e.Absent));
        Assert.Equal(739, m.Entries.Count(e => e.Game == "ER" && !e.Absent));
    }
}

public class OodleTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "nrm-oo-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    [Fact]
    public void StageCopiesBothDllsFromTheGameFolders()
    {
        var nr = Path.Combine(root, "nr"); var er = Path.Combine(root, "er"); var dst = Path.Combine(root, "w");
        Directory.CreateDirectory(nr); Directory.CreateDirectory(er);
        File.WriteAllText(Path.Combine(nr, "oo2core_9_win64.dll"), "nine");
        File.WriteAllText(Path.Combine(er, "oo2core_6_win64.dll"), "six");
        var dir = Oodle.Stage(nr, er, dst);
        Assert.Equal(dst, dir);
        Assert.Equal("nine", File.ReadAllText(Path.Combine(dst, "oo2core_9_win64.dll")));
        Assert.Equal("six", File.ReadAllText(Path.Combine(dst, "oo2core_6_win64.dll")));
    }

    [Fact]
    public void StageNamesTheFolderWhenADllIsMissing()
    {
        var nr = Path.Combine(root, "nr"); var er = Path.Combine(root, "er");
        Directory.CreateDirectory(nr); Directory.CreateDirectory(er);
        File.WriteAllText(Path.Combine(nr, "oo2core_9_win64.dll"), "nine");
        var ex = Assert.Throws<BuildException>(() => Oodle.Stage(nr, er, Path.Combine(root, "w")));
        Assert.Contains("oo2core_6_win64.dll", ex.Message);
        Assert.Contains(er, ex.Message);
    }
}
