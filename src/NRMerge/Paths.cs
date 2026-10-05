namespace NRMerge;

/// <summary>Locations of the inputs, bases, outputs and tooling metadata of the merge, derived from the active <see cref="BuildConfig"/>.</summary>
public static class Paths
{
    /// <summary>The active configuration (defaults to <see cref="BuildConfig.Dev"/>).</summary>
    public static BuildConfig Config { get; private set; } = BuildConfig.Dev();

    /// <summary>Switches every derived path to <paramref name="cfg"/>; also drops path overrides (e.g. a test's journal dir).</summary>
    public static void Use(BuildConfig cfg)
    {
        Config = cfg ?? throw new ArgumentNullException(nameof(cfg));
        Journal.Dir = null;
    }

    public static string GameNR => Config.NrGame;
    public static string GameER => Config.ErGame;
    /// <summary>False when the build runs without Elden Ring (<see cref="BuildConfig.ErGame"/> null).</summary>
    public static bool HasEldenRing => !string.IsNullOrEmpty(Config.ErGame);
    public static string EV => Config.EvMod;
    public static string MMV => Config.MmvMod;
    public static string FinalDir => Config.OutputDir;

    public static string Workspace => Config.WorkDir;
    public static string Vanilla => Path.Combine(Workspace, "vanilla");
    public static string VanillaNR => Vanilla;
    public static string VanillaER => Path.Combine(Workspace, "vanilla_er");
    public static string Out => Path.Combine(Workspace, "out");
    public static string OutMod => Path.Combine(Out, "mod");
    public static string Work => Path.Combine(Workspace, "work");

    // Tooling metadata. Release layout under DataDir: smithbox\PARAM\{NR,ER}\..., smithbox\TAE\TAE.Template.NR.xml,
    // andre\*.txt, merge\ (rules), vanilla-manifest.tsv, fetch.json. Pinned downloads (Fetch: regulation base, c0000 base,
    // EMEDF) go to CacheDir. Without DataDir: the dev checkouts in the repo.

    /// <summary>True when no DataDir is configured and metadata comes from the dev checkouts.</summary>
    public static bool DevData => string.IsNullOrEmpty(Config.DataDir);
    /// <summary>Root of the shipped tooling metadata (the repo checkout in dev mode).</summary>
    public static string Data => DevData ? BuildConfig.DevRepo : Config.DataDir;
    /// <summary>Smithbox assets root (contains PARAM\ and TAE\).</summary>
    public static string SmithboxData => DevData
        ? Path.Combine(BuildConfig.DevRepo, "src", "Smithbox", "src", "Smithbox.Data", "Assets")
        : Path.Combine(Data, "smithbox");
    public static string ParamDefs(string game) => Path.Combine(SmithboxData, "PARAM", game, "Defs");
    public static string ParamMeta(string game) => Path.Combine(SmithboxData, "PARAM", game, "Param Meta");
    public static string ParamTypeInfo(string game) => Path.Combine(SmithboxData, "PARAM", game, "Param Type Info.json");
    public static string TaeTemplate => Path.Combine(SmithboxData, "TAE", "TAE.Template.NR.xml");
    /// <summary>Andre name dictionaries (EldenRingDictionary.txt, EldenRingNightreignDictionary.txt).</summary>
    public static string AndreResources => DevData
        ? Path.Combine(BuildConfig.DevRepo, "src", "Smithbox", "src", "Andre", "Andre.Formats", "Resources")
        : Path.Combine(Data, "andre");
    /// <summary>DarkScript3's Nightreign EMEDF: the dev copy in tools\darkscript, else fetched (pinned + hashed) into the cache.</summary>
    public static string Emedf => DevData
        ? Path.Combine(BuildConfig.DevRepo, "tools", "darkscript", "nr-common.emedf.json")
        : Fetch.Get("nr-emedf", Config);
    /// <summary>Shipped manifests (vanilla-manifest.tsv, fetch.json): the repo's data\ in dev mode, Data itself in a release.</summary>
    public static string Manifests => DevData ? Path.Combine(BuildConfig.DevRepo, "data") : Data;
    /// <summary>Merge rules (ai\, hks\): the repo's merge\ in dev mode, Data\merge in a release.</summary>
    public static string Rules => Path.Combine(Data, "merge");

    /// <summary>Path of a mod-relative file inside a root, using OS separators.</summary>
    public static string In(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Base file for a mod-relative path: vanilla Nightreign first, then the Elden Ring original, else null.
    /// Without Elden Ring the second step is skipped and journaled (<see cref="ErFallback"/>).</summary>
    public static string BaseOf(string rel)
    {
        var nr = In(Vanilla, rel);
        if (File.Exists(nr)) return nr;
        var er = ErOriginal(rel, "merged without a base (treated as added by both mods: owner rule, EV kept where both changed it)");
        return er != null && File.Exists(er) ? er : null;
    }

    /// <summary>Path of the Elden Ring original of <paramref name="rel"/> in vanilla_er (may not exist); null without Elden Ring,
    /// in which case the fallback (<paramref name="fallback"/>) is journaled when the verified build had that file.</summary>
    public static string ErOriginal(string rel, string fallback)
    {
        if (HasEldenRing) return In(VanillaER, rel);
        ErFallback.NoErBase(rel, fallback);
        return null;
    }
}
