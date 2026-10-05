using NRMerge;
using Xunit;

namespace NRMerge.Tests;

/// <summary>Paths is process-wide state: these tests switch it, so they must not run alongside other tests.</summary>
[CollectionDefinition("Paths", DisableParallelization = true)]
public class PathsCollection { }

[Collection("Paths")]
public class BuildConfigTests : IDisposable
{
    public void Dispose() => Paths.Use(BuildConfig.Dev());

    static readonly string Repo = BuildConfig.DevRepo;
    static readonly string NR = BuildConfig.DevNRRoot;

    [Fact]
    public void DefaultIsDev()
    {
        Assert.Equal(Path.Combine(NR, "ELDEN VINS NIGHTREIGN", "mod"), Paths.EV);
        Assert.Equal(Repo, Paths.Workspace);
    }

    [Fact]
    public void DevReproducesTodaysLocations()
    {
        Paths.Use(BuildConfig.Dev());
        Assert.Equal(Path.Combine(NR, "Game"), Paths.GameNR);
        Assert.Equal(BuildConfig.DevERGame, Paths.GameER);
        Assert.Equal(Path.Combine(NR, "ELDEN VINS NIGHTREIGN", "mod"), Paths.EV);
        Assert.Equal(Path.Combine(NR, "More Map Variations 2.1.8-hotfix3 & Weapons Mod", "mod"), Paths.MMV);
        Assert.Equal(Path.Combine(NR, "Elden Vins with more map variations"), Paths.FinalDir);
        Assert.Equal(Repo, Paths.Workspace);
        Assert.Equal(Path.Combine(Repo, "vanilla"), Paths.Vanilla);
        Assert.Equal(Paths.Vanilla, Paths.VanillaNR);
        Assert.Equal(Path.Combine(Repo, "vanilla_er"), Paths.VanillaER);
        Assert.Equal(Path.Combine(Repo, "out"), Paths.Out);
        Assert.Equal(Path.Combine(Repo, "out", "mod"), Paths.OutMod);
        Assert.Equal(Path.Combine(Repo, "work"), Paths.Work);
        Assert.Equal(Path.Combine(Repo, "out", "journal"), Journal.Dir);
        Assert.Equal(Path.Combine(Repo, "work", "mmv_rw"), MmvRewrite.RwRoot);
        // Tooling metadata from the dev checkouts.
        var assets = Path.Combine(Repo, "src", "Smithbox", "src", "Smithbox.Data", "Assets");
        Assert.Equal(assets, Paths.SmithboxData);
        Assert.Equal(assets, Regulation.SmithboxData);
        Assert.Equal(Path.Combine(assets, "PARAM", "NR", "Defs"), Paths.ParamDefs("NR"));
        Assert.Equal(Path.Combine(assets, "PARAM", "ER", "Param Meta"), Paths.ParamMeta("ER"));
        Assert.Equal(Path.Combine(assets, "PARAM", "NR", "Param Type Info.json"), Paths.ParamTypeInfo("NR"));
        Assert.Equal(Path.Combine(assets, "TAE", "TAE.Template.NR.xml"), Paths.TaeTemplate);
        Assert.Equal(Paths.TaeTemplate, MmvRewrite.TaeTemplatePath);
        Assert.Equal(Path.Combine(Repo, "src", "Smithbox", "src", "Andre", "Andre.Formats", "Resources"), Paths.AndreResources);
        Assert.Equal(Path.Combine(Repo, "tools", "darkscript", "nr-common.emedf.json"), Paths.Emedf);
        Assert.Equal(Paths.Emedf, MmvRewrite.EmedfPath);
        Assert.Equal(Path.Combine(Repo, "merge"), Paths.Rules);
        Assert.Equal(Path.Combine(assets, "PARAM", "NR", "Regulations", "1.03.4 (10340000)", "regulation.bin"),
            Regulation.VanillaRegulationForVersion("10340000"));
    }

    [Fact]
    public void UseChangesEveryDerivedPath()
    {
        var t = Path.Combine(Path.GetTempPath(), "nrm-cfg-" + Guid.NewGuid().ToString("N"));
        var cfg = new BuildConfig
        {
            NrGame = Path.Combine(t, "NRGame"), ErGame = Path.Combine(t, "ERGame"),
            EvMod = Path.Combine(t, "ev"), MmvMod = Path.Combine(t, "mmv"),
            OutputDir = Path.Combine(t, "output"), WorkDir = Path.Combine(t, "output", "_build"),
            DataDir = Path.Combine(t, "Data"), CacheDir = Path.Combine(t, "cache"),
        };
        Paths.Use(cfg);
        Assert.Same(cfg, Paths.Config);
        Assert.Equal(cfg.NrGame, Paths.GameNR);
        Assert.Equal(cfg.ErGame, Paths.GameER);
        Assert.Equal(cfg.EvMod, Paths.EV);
        Assert.Equal(cfg.MmvMod, Paths.MMV);
        Assert.Equal(cfg.OutputDir, Paths.FinalDir);
        Assert.Equal(cfg.WorkDir, Paths.Workspace);
        Assert.Equal(Path.Combine(cfg.WorkDir, "vanilla"), Paths.Vanilla);
        Assert.Equal(Path.Combine(cfg.WorkDir, "vanilla_er"), Paths.VanillaER);
        Assert.Equal(Path.Combine(cfg.WorkDir, "out"), Paths.Out);
        Assert.Equal(Path.Combine(cfg.WorkDir, "out", "mod"), Paths.OutMod);
        Assert.Equal(Path.Combine(cfg.WorkDir, "work"), Paths.Work);
        Assert.Equal(Path.Combine(cfg.WorkDir, "out", "journal"), Journal.Dir);
        Assert.Equal(Path.Combine(cfg.WorkDir, "work", "mmv_rw"), MmvRewrite.RwRoot);
        Assert.Equal(cfg.DataDir, Paths.Data);
        Assert.Equal(Path.Combine(cfg.DataDir, "smithbox"), Paths.SmithboxData);
        Assert.Equal(Path.Combine(cfg.DataDir, "smithbox", "PARAM", "NR", "Defs"), Paths.ParamDefs("NR"));
        Assert.Equal(Path.Combine(cfg.DataDir, "smithbox", "PARAM", "ER", "Param Meta"), Paths.ParamMeta("ER"));
        Assert.Equal(Path.Combine(cfg.DataDir, "smithbox", "PARAM", "ER", "Param Type Info.json"), Paths.ParamTypeInfo("ER"));
        Assert.Equal(Path.Combine(cfg.DataDir, "smithbox", "TAE", "TAE.Template.NR.xml"), Paths.TaeTemplate);
        Assert.Equal(Paths.TaeTemplate, MmvRewrite.TaeTemplatePath);
        Assert.Equal(Path.Combine(cfg.DataDir, "andre"), Paths.AndreResources);
        Assert.Equal(Path.Combine(cfg.DataDir, "merge"), Paths.Rules);
        Assert.Equal(cfg.CacheDir, Paths.Config.CacheDir);
        // A file in the vanilla extract becomes the base.
        Directory.CreateDirectory(Path.Combine(Paths.Vanilla, "x"));
        File.WriteAllText(Path.Combine(Paths.Vanilla, "x", "a.txt"), "a");
        try { Assert.Equal(Path.Combine(Paths.Vanilla, "x", "a.txt"), Paths.BaseOf("x/a.txt")); }
        finally { Directory.Delete(t, true); }
    }

    [Fact]
    public void JournalDirOverrideIsKeptUntilUse()
    {
        Journal.Dir = @"C:\somewhere";
        Assert.Equal(@"C:\somewhere", Journal.Dir);
        Paths.Use(BuildConfig.Dev());
        Assert.Equal(Path.Combine(Repo, "out", "journal"), Journal.Dir);
    }

    [Fact]
    public void SaveDirsListsEverySteamIdFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "nrm-saves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "76561198000000001"));
        Directory.CreateDirectory(Path.Combine(root, "76561198000000002"));
        Directory.CreateDirectory(Path.Combine(root, "NotAnId"));
        File.WriteAllText(Path.Combine(root, "GraphicsConfig.xml"), "");
        try
        {
            var dirs = Profile.SaveDirs(root).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "76561198000000001", "76561198000000002" }, dirs);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SaveDirsOfMissingRootIsEmpty()
    {
        Assert.Empty(Profile.SaveDirs(Path.Combine(Path.GetTempPath(), "nrm-none-" + Guid.NewGuid().ToString("N"))));
    }
}
