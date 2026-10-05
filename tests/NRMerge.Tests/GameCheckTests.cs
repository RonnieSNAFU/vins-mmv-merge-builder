using Xunit;

namespace NRMerge.Tests;

public class GameCheckTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "gamecheck-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string nr, er;
    readonly Dictionary<string, string> exe = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> reg = new(StringComparer.OrdinalIgnoreCase);

    public GameCheckTests()
    {
        nr = Path.Combine(dir, "ELDEN RING NIGHTREIGN", "Game");
        er = Path.Combine(dir, "ELDEN RING", "Game");
        Make(nr, "nightreign.exe", "1.3.3.0", "10350000");
        Make(er, "eldenring.exe", "2.6.1.0", "11611000");
    }

    void Make(string game, string exeName, string exeVer, string regVer)
    {
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, exeName), "exe");
        File.WriteAllText(Path.Combine(game, "regulation.bin"), "reg");
        exe[Path.Combine(game, exeName)] = exeVer;
        reg[Path.Combine(game, "regulation.bin")] = regVer;
    }

    List<string> Run(string nrDir = null, string erDir = null) =>
        GameCheck.Check(nrDir ?? nr, erDir ?? er, p => reg[p], p => exe[p]);

    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Theory]
    [InlineData("10350000", "1.03.5")]
    [InlineData("10340000", "1.03.4")]
    [InlineData("11611000", "1.16.1")]
    public void RegulationDisplay_FormatsGameVersion(string v, string expected) =>
        Assert.Equal(expected, GameCheck.RegulationDisplay(v));

    [Fact]
    public void ExpectedVersions_HaveNoProblems() => Assert.Empty(Run());

    [Fact]
    public void OldNightreign_IsReportedWithExeAndVersion()
    {
        exe[Path.Combine(nr, "nightreign.exe")] = "1.3.2.0";
        reg[Path.Combine(nr, "regulation.bin")] = "10340000";
        var p = Assert.Single(Run());
        Assert.StartsWith("Nightreign 1.03.5 required, found 1.03.4 (exe 1.3.2.0", p);
    }

    [Fact]
    public void NightreignRegulationMismatch_WithRightExe_IsReported()
    {
        reg[Path.Combine(nr, "regulation.bin")] = "10360000";
        var p = Assert.Single(Run());
        Assert.Contains("Nightreign 1.03.5 required, found 1.03.6", p);
        Assert.Contains("regulation 10360000", p);
    }

    [Fact]
    public void MissingNightreign_IsReported()
    {
        var p = Assert.Single(Run(nrDir: Path.Combine(dir, "nope")));
        Assert.Contains("Nightreign not found", p);
        Assert.Contains("nightreign.exe", p);
    }

    [Fact]
    public void UnreadableRegulation_IsReported()
    {
        var problems = GameCheck.Check(nr, er, p => throw new InvalidDataException("bad bnd"), p => exe[p]);
        Assert.Equal(2, problems.Count);
        Assert.Contains("Could not read the Nightreign regulation version", problems[0]);
        Assert.Contains("bad bnd", problems[0]);
    }

    [Fact]
    public void MissingEldenRing_IsReportedAsRequired()
    {
        var p = Assert.Single(Run(erDir: Path.Combine(dir, "noer")));
        Assert.Contains("Elden Ring not found", p);
        Assert.Contains("required", p);
    }

    [Fact]
    public void NullEldenRingDir_IsReportedAsRequired()
    {
        var p = Assert.Single(GameCheck.Check(nr, null, q => reg[q], q => exe[q]));
        Assert.Contains("Elden Ring not found", p);
    }

    [Fact]
    public void WrongEldenRingRegulation_IsReported()
    {
        reg[Path.Combine(er, "regulation.bin")] = "11600000";
        var p = Assert.Single(Run());
        Assert.StartsWith("Elden Ring 1.16.1 required (regulation 11611000), found 1.16.0 (exe 2.6.1.0, regulation 11600000)", p);
    }

    [Fact]
    public void DefaultRegulationReader_ReportsUnreadableFiles()
    {
        // real readers (SoulsFormats decrypt + FileVersionInfo): fake files are reported, never a crash
        var problems = GameCheck.Check(new BuildConfig { NrGame = nr, ErGame = er });
        Assert.Equal(2, problems.Count);
        Assert.Contains("Could not read the Nightreign regulation version", problems[0]);
        Assert.Contains("Could not read the Elden Ring regulation version", problems[1]);
    }

    [Fact]
    public void DefaultExeReader_ReadsFileVersion()
    {
        // FileVersionInfo of a non-PE file is empty: reported as unknown, never a crash
        var problems = GameCheck.Check(nr, er, p => reg[p]);
        Assert.Contains(problems, p => p.Contains("exe unknown"));
    }
}
