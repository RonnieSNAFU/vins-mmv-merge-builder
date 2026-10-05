namespace NRMerge;

/// <summary>Where a build reads its inputs and writes its outputs. <see cref="Dev"/> is the maintainer's layout on this PC.</summary>
public sealed class BuildConfig
{
    /// <summary>Repository checkout used by <see cref="Dev"/>; also where dev-only tooling and metadata live.
    /// Resolved from the <c>NRMERGE_REPO</c> environment variable, else the nearest parent of the exe holding
    /// <c>src\NRMerge\NRMerge.csproj</c>, else the current directory.</summary>
    public static readonly string DevRepo = FindRepo();
    /// <summary>Nightreign install root (folder holding <c>Game</c> and the mod folders): <c>NRMERGE_NIGHTREIGN_ROOT</c>,
    /// else the Steam library that has Nightreign, else the default Steam library.</summary>
    public static readonly string DevNRRoot = Env("NRMERGE_NIGHTREIGN_ROOT") ?? SteamGame("ELDEN RING NIGHTREIGN");
    /// <summary>Elden Ring <c>Game</c> folder: <c>NRMERGE_ELDENRING_GAME</c>, else found through Steam.</summary>
    public static readonly string DevERGame = Env("NRMERGE_ELDENRING_GAME") ?? Path.Combine(SteamGame("ELDEN RING"), "Game");

    static string Env(string name) { var v = Environment.GetEnvironmentVariable(name); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }

    static string FindRepo()
    {
        var env = Env("NRMERGE_REPO");
        if (env != null) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "NRMerge", "NRMerge.csproj"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    static string SteamGame(string folder)
    {
        try
        {
            foreach (var lib in SteamLocator.Libraries())
            {
                var p = Path.Combine(lib, "steamapps", "common", folder);
                if (Directory.Exists(p)) return p;
            }
        }
        catch { }
        return Path.Combine(SteamLocator.DefaultSteamRoot, "steamapps", "common", folder);
    }

    /// <summary>Nightreign <c>Game</c> folder.</summary>
    public string NrGame;
    /// <summary>Elden Ring <c>Game</c> folder; null = build without Elden Ring (<c>--no-eldenring</c> or not installed): every
    /// Elden-Ring-based decision falls back (see <see cref="ErFallback"/>) and the output differs from the verified build.</summary>
    public string ErGame;
    /// <summary>Elden Vins <c>mod</c> folder.</summary>
    public string EvMod;
    /// <summary>MMV <c>mod</c> folder.</summary>
    public string MmvMod;
    /// <summary>Delivery folder (receives <c>mod\</c>, the profile, report and journals).</summary>
    public string OutputDir;
    /// <summary>Workspace holding <c>vanilla</c>, <c>vanilla_er</c>, <c>out</c> and <c>work</c>.</summary>
    public string WorkDir;
    /// <summary>Shipped tooling metadata (smithbox, andre, darkscript, merge rules); null = dev checkouts under <see cref="DevRepo"/>.</summary>
    public string DataDir;
    /// <summary>Cache for pinned downloads.</summary>
    public string CacheDir;
    public bool Force, KeepWork;

    public static BuildConfig Dev() => new()
    {
        NrGame = Path.Combine(DevNRRoot, "Game"),
        ErGame = DevERGame,
        EvMod = Path.Combine(DevNRRoot, "ELDEN VINS NIGHTREIGN", "mod"),
        MmvMod = Path.Combine(DevNRRoot, "More Map Variations 2.1.8-hotfix3 & Weapons Mod", "mod"),
        OutputDir = Path.Combine(DevNRRoot, "Elden Vins with more map variations"),
        WorkDir = DevRepo,
        DataDir = null,
        CacheDir = Path.Combine(DevRepo, "out", "cache"),
    };
}
