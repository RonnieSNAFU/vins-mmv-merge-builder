using System.Text;
using System.Text.Json;
using Xunit;

namespace NRMerge.Tests;

[Collection("Paths")]
public class BuilderTests : IDisposable
{
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-build-" + Guid.NewGuid().ToString("N")[..8]);
    readonly StringWriter log = new();
    BuilderEnv Env(bool interactive = false) => new()
    {
        Out = log, Interactive = interactive,
        Confirm = _ => throw new Exception("unexpected prompt"),
        PickFile = (_, _) => throw new Exception("unexpected dialog"),
        OpenFolder = _ => throw new Exception("unexpected Explorer window"),
        RunProcess = (_, _, _) => throw new Exception("unexpected process"),
    };

    public BuilderTests() { Fetch.RetryDelay = TimeSpan.Zero; }

    public void Dispose()
    {
        Paths.Use(BuildConfig.Dev());
        ErFallback.Reset();
        try { Directory.Delete(tmp, true); } catch { }
    }

    // ---------------------------------------------------------------- options

    [Fact]
    public void ParsesEveryOption()
    {
        var o = BuildOptions.Parse(new[] { "--ev", "a", "--mmv", "b.7z", "--nightreign", "n", "--eldenring", "e", "--out", "o", "--cache-dir", "c",
            "--data-dir", "d", "--force", "--keep-work", "--non-interactive" });
        Assert.Equal(("a", "b.7z", "n", "e", "o", "c", "d"), (o.Ev, o.Mmv, o.Nightreign, o.EldenRing, o.Out, o.CacheDir, o.DataDir));
        Assert.True(o.Force && o.KeepWork && o.NonInteractive && !o.NoEldenRing);
        Assert.True(BuildOptions.Parse(new[] { "--no-eldenring" }).NoEldenRing);
        Assert.Contains("unknown option", Assert.Throws<BuildException>(() => BuildOptions.Parse(new[] { "--evv", "x" })).Message);
        Assert.Throws<BuildException>(() => BuildOptions.Parse(new[] { "--out" }));
        Assert.Throws<BuildException>(() => BuildOptions.Parse(new[] { "--eldenring", "x", "--no-eldenring" }));
    }

    [Fact]
    public void BadOptionsPrintUsageAndExitWithoutWork()
    {
        Assert.Equal(2, Builder.Main(new[] { "--bogus" }, Env()));
        Assert.Contains("usage: NRMerge build", log.ToString());
    }

    // ---------------------------------------------------------------- output folder

    [Fact]
    public void ANonEmptyOutputFolderIsRefused()
    {
        var o = Path.Combine(tmp, "out");
        Directory.CreateDirectory(o);
        File.WriteAllText(Path.Combine(o, "something.txt"), "x");
        var e = Assert.Throws<BuildException>(() => Builder.ResolveOutput(o, null));
        Assert.Contains("not empty", e.Message);
        Assert.Contains("--out", e.Message);
    }

    [Fact]
    public void AnOutputFolderHoldingAMergedBuild_LikeTheInstalledOne_IsRefusedAndSaysSo()
    {
        var o = Path.Combine(tmp, "Elden Vins with more map variations");
        Directory.CreateDirectory(Path.Combine(o, "mod"));
        File.WriteAllText(Path.Combine(o, ModManifest.MergedMe3), "x");
        Assert.Contains("holds a merged build", Assert.Throws<BuildException>(() => Builder.ResolveOutput(o, null)).Message);
    }

    [Fact]
    public void NewEmptyOrPreviousAttemptOutputFoldersAreAccepted()
    {
        var fresh = Path.Combine(tmp, "new");
        Assert.Equal(fresh, Builder.ResolveOutput(fresh, null));
        Directory.CreateDirectory(fresh);
        Assert.Equal(fresh, Builder.ResolveOutput(fresh + "\\", null));
        Directory.CreateDirectory(Path.Combine(fresh, Builder.WorkFolder, "vanilla"));
        Assert.Equal(fresh, Builder.ResolveOutput(fresh, null));
    }

    [Fact]
    public void TheDefaultOutputSitsNextToTheGameAndNeverReusesATakenFolder()
    {
        var game = Path.Combine(tmp, "ELDEN RING NIGHTREIGN", "Game");
        Directory.CreateDirectory(game);
        var first = Path.Combine(tmp, "ELDEN RING NIGHTREIGN", Builder.DefaultOutputName);
        Assert.Equal(first, Builder.ResolveOutput(null, game));
        Directory.CreateDirectory(first);
        File.WriteAllText(Path.Combine(first, ModManifest.MergedMe3), "installed build");
        Assert.Equal(first + " (2)", Builder.ResolveOutput(null, game));
    }

    // ---------------------------------------------------------------- mods

    [Fact]
    public void SwappedModFoldersAreDetectedAndUsedTheRightWayRound()
    {
        using var fx = new ModFixture();
        string ev = fx.Mmv, mmv = fx.Ev;   // the player mixed them up
        Builder.CheckMods(fx.Manifest(), ref ev, ref mmv, false, Env());
        Assert.Equal(fx.Ev, ev);
        Assert.Equal(fx.Mmv, mmv);
        Assert.Contains("wrong way round", log.ToString());
    }

    [Fact]
    public void AChangedModFileIsRefusedUnlessForced()
    {
        using var fx = new ModFixture();
        var m = fx.Manifest();
        File.WriteAllText(Path.Combine(fx.Ev, "mod", "chr", "c0003.chrbnd.dcx"), "edited");
        string ev = fx.Ev, mmv = fx.Mmv;
        var e = Assert.Throws<BuildException>(() => Builder.CheckMods(m, ref ev, ref mmv, false, Env()));
        Assert.Contains("--force", e.Message);
        Assert.Contains("Elden Vins", e.Message);
        Builder.CheckMods(m, ref ev, ref mmv, true, Env());
        Assert.Contains("WARNING (--force)", log.ToString());
    }

    // ---------------------------------------------------------------- games

    [Fact]
    public void NightreignNotFound_NonInteractiveExplainsTheOption_InteractiveAsks()
    {
        var e = Assert.Throws<BuildException>(() => Builder.LocateNightreign(new BuildOptions(), Env(), new List<string>()));
        Assert.Contains("--nightreign", e.Message);

        var game = Path.Combine(tmp, "NR", "Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "nightreign.exe"), "x");
        var env = Env(interactive: true);
        env.PickFile = (_, filter) => Path.Combine(game, "nightreign.exe");
        Assert.Equal(game, Builder.LocateNightreign(new BuildOptions(), env, new List<string>()));
    }

    string MakeEldenRing(string lib)
    {
        var game = Path.Combine(lib, "steamapps", "common", "ELDEN RING", "Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "eldenring.exe"), "x");
        return game;
    }

    [Fact]
    public void EldenRingIsOptional_NotFoundOrDisabledOrWrongVersionMeansBuildingWithoutIt()
    {
        var lib = Path.Combine(tmp, "lib");
        var game = MakeEldenRing(lib);
        var env = Env();
        env.ReadErRegulationVersion = _ => GameCheck.EldenRingRegulation;

        Assert.Equal(game, Builder.LocateEldenRing(new BuildOptions(), env, new List<string> { lib }, out _));
        Assert.Null(Builder.LocateEldenRing(new BuildOptions { NoEldenRing = true }, env, new List<string> { lib }, out var n1));
        Assert.Contains("--no-eldenring", n1);
        Assert.Null(Builder.LocateEldenRing(new BuildOptions(), env, new List<string>(), out var n2));
        Assert.Contains("not found", n2);
        env.ReadErRegulationVersion = _ => "11600000";
        Assert.Null(Builder.LocateEldenRing(new BuildOptions(), env, new List<string> { lib }, out var n3));
        Assert.Contains("11600000", n3);
        // an explicitly given wrong Elden Ring is an error that names the way out
        Assert.Contains("--no-eldenring", Assert.Throws<BuildException>(() => Builder.LocateEldenRing(new BuildOptions { EldenRing = game }, env, new(), out _)).Message);
    }

    [Fact]
    public void GameCheckWithoutEldenRingChecksOnlyNightreign()
    {
        var nr = Path.Combine(tmp, "nr");
        Directory.CreateDirectory(nr);
        File.WriteAllText(Path.Combine(nr, "nightreign.exe"), "x");
        File.WriteAllText(Path.Combine(nr, "regulation.bin"), "x");
        var ok = GameCheck.Check(nr, null, _ => GameCheck.NightreignRegulation, _ => GameCheck.NightreignExeVersion, eldenRingOptional: true);
        Assert.Empty(ok);
        var required = GameCheck.Check(nr, null, _ => GameCheck.NightreignRegulation, _ => GameCheck.NightreignExeVersion);
        Assert.Contains(required, p => p.Contains("Elden Ring not found"));
    }

    // ---------------------------------------------------------------- ME3

    [Fact]
    public void Me3IsFoundViaRegistryThenPathThenTheDefaultFolder()
    {
        var reg = @"C:\Users\p\AppData\Local\Programs\garyttierney\me3";
        string regExe = Path.Combine(reg, "bin", "me3.exe"), pathExe = @"D:\tools\me3\me3.exe";
        string defExe = @"C:\Users\p\AppData\Local\Programs\garyttierney\me3\bin\me3.exe";
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { regExe, pathExe, defExe };
        Assert.Equal(regExe, Me3.Find(reg, @"C:\Windows;D:\tools\me3", @"C:\Users\p\AppData\Local", all.Contains));
        Assert.Equal(pathExe, Me3.Find(null, @"C:\Windows;""D:\tools\me3""", @"C:\Users\x", all.Contains));
        Assert.Equal(defExe, Me3.Find(null, @"C:\Windows", @"C:\Users\p\AppData\Local", all.Contains));
        Assert.Null(Me3.Find(@"E:\nowhere", @"C:\Windows", @"C:\Users\x", all.Contains));
    }

    const string ReleaseJson = "{\"tag_name\":\"v0.9.0\",\"assets\":[{\"name\":\"me3-windows-amd64.zip\",\"browser_download_url\":\"https://example.invalid/me3.zip\"},"
        + "{\"name\":\"me3_installer.exe\",\"browser_download_url\":\"https://example.invalid/me3_installer.exe\"}]}";

    [Fact]
    public void TheInstallerUrlComesFromTheLatestReleaseAssets()
    {
        Assert.Equal("https://example.invalid/me3_installer.exe", Me3.InstallerUrl(ReleaseJson));
        Assert.Null(Me3.InstallerUrl("{\"assets\":[]}"));
    }

    [Fact]
    public void MissingMe3_NonInteractiveNeverPromptsOrDownloads()
    {
        var env = Env();
        env.FindMe3 = () => null;
        Assert.Null(Builder.EnsureMe3(new BuildConfig { WorkDir = tmp }, env));
        Assert.Contains("not installed", log.ToString());
    }

    [Fact]
    public void MissingMe3_InteractiveYes_DownloadsAndRunsTheOfficialInstaller()
    {
        var http = new FakeHttp();
        http.Files[Me3.LatestReleaseApi] = Encoding.UTF8.GetBytes(ReleaseJson);
        http.Files["https://example.invalid/me3_installer.exe"] = new byte[] { 0x4D, 0x5A, 1 };
        bool installed = false;
        var ran = new List<(string, bool)>();
        var env = Env(interactive: true);
        env.Http = http;
        env.FindMe3 = () => installed ? @"C:\me3\bin\me3.exe" : null;
        env.Confirm = q => { Assert.Contains("Mod Engine 3", q); return true; };
        env.RunProcess = (exe, args, wait) => { ran.Add((exe, wait)); installed = true; return 0; };
        Assert.Equal(@"C:\me3\bin\me3.exe", Builder.EnsureMe3(new BuildConfig { WorkDir = tmp }, env));
        var installer = Path.Combine(tmp, "me3", "me3_installer.exe");
        Assert.Equal(new[] { (installer, true) }, ran);
        Assert.Equal(new byte[] { 0x4D, 0x5A, 1 }, File.ReadAllBytes(installer));
    }

    [Fact]
    public void MissingMe3_InteractiveNo_DownloadsNothing()
    {
        var http = new FakeHttp();
        var env = Env(interactive: true);
        env.Http = http;
        env.FindMe3 = () => null;
        env.Confirm = _ => false;
        Assert.Null(Builder.EnsureMe3(new BuildConfig { WorkDir = tmp }, env));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void TheLaunchCommandUsesTheMergedProfile()
    {
        var a = Me3.LaunchArguments(@"D:\Merged\Elden Vins with more map variations.me3");
        Assert.Equal("launch --game nightreign --profile \"D:\\Merged\\Elden Vins with more map variations.me3\"", a);
    }

    // ---------------------------------------------------------------- pinned files

    [Fact]
    public void OfflineThePinnedFetchFailsBeforeTheLongWorkWithAClearMessage()
    {
        var data = Path.Combine(tmp, "Data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "fetch.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["c0000"] = new { url = "https://example.invalid/c0000.hks", sha256 = new string('a', 64), normalize = "crlf-to-lf", file = "c0000_base.hks" },
        }));
        var env = Env();
        env.Http = new FakeHttp { Offline = true };
        var e = Assert.Throws<BuildException>(() => Builder.FetchPinned(new BuildConfig { DataDir = data, CacheDir = Path.Combine(tmp, "cache") }, env));
        Assert.Contains("offline", e.Message);
        Assert.Contains("https://example.invalid/c0000.hks", e.Message);
        Assert.Contains("--cache-dir", e.Message);
        Assert.Contains("c0000_base.hks", e.Message);
    }

    // ---------------------------------------------------------------- delivery

    [Fact]
    public void DeliveryAcceptsTheWorkspaceInsideTheOutputAndAddsVersionAndTemplates()
    {
        var output = Path.Combine(tmp, "Merged");
        var cfg = new BuildConfig { OutputDir = output, WorkDir = Path.Combine(output, Builder.WorkFolder), DataDir = Path.Combine(tmp, "Data") };
        Directory.CreateDirectory(Path.Combine(cfg.DataDir, "templates"));
        File.WriteAllText(Path.Combine(cfg.DataDir, "templates", "README.txt"), "readme");
        Paths.Use(cfg);
        Directory.CreateDirectory(Path.Combine(Paths.OutMod, "chr"));
        File.WriteAllText(Path.Combine(Paths.OutMod, "chr", "a.bin"), "a");
        foreach (var f in new[] { "assembly.tsv", "remap.json", "judge_remap.json", Profile.ProfileName }) File.WriteAllText(Path.Combine(Paths.Out, f), f);
        Journal.Clear();
        Journal.Add("profile", "x", "y");
        ErFallback.Note("chr/c9999.anibnd.dcx", "no Elden Ring base: test");
        Journal.Save();
        File.WriteAllText(Path.Combine(Paths.Out, "verify.ok"), "ok");

        Assert.Equal(0, Report.Deliver(new[] { "Task 1: Ruling: test ruling — cost if wrong: none" }, Report.VersionText("9.9.9", new DateTime(2026, 10, 4), eldenRing: false)));
        Assert.True(File.Exists(Path.Combine(output, "mod", "chr", "a.bin")));
        Assert.Contains("WITHOUT Elden Ring", File.ReadAllText(Path.Combine(output, "VERSION.txt")));
        Assert.Equal("readme", File.ReadAllText(Path.Combine(output, "README.txt")));
        Assert.True(File.Exists(Path.Combine(output, "merge-journal", "no-eldenring.tsv")));
        var report = File.ReadAllText(Path.Combine(output, "MERGE_REPORT.md"));
        Assert.Contains("test ruling", report);
        Assert.Contains("Elden Ring absent", report);
    }

    [Fact]
    public void TheEmbeddedRulingsAreTheLedgersRulingLines()
    {
        var r = Report.EmbeddedRulings();
        Assert.True(r.Length > 50);
        Assert.All(r, l => Assert.Contains("Ruling:", l));
        var ledger = Report.LedgerPath;
        if (File.Exists(ledger)) Assert.Equal(File.ReadAllLines(ledger).Where(l => l.Contains("Ruling:")).Take(r.Length), r);
    }
}
