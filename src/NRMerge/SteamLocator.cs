using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>Finds Steam libraries (registry SteamPath + steamapps\libraryfolders.vdf) and the games' Game folders in them.</summary>
public static class SteamLocator
{
    public const string NightreignFolder = "ELDEN RING NIGHTREIGN", NightreignExe = "nightreign.exe";
    public const string EldenRingFolder = "ELDEN RING", EldenRingExe = "eldenring.exe";
    /// <summary>Where Steam installs itself when the registry has no SteamPath.</summary>
    public static readonly string DefaultSteamRoot = @"C:\Program Files (x86)\Steam";

    static readonly Regex PathKey = new(@"""path""\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Library folders listed in a libraryfolders.vdf ("path" keys, VDF escapes undone), in file order.</summary>
    public static List<string> ParseLibraryFolders(string vdf)
    {
        var list = new List<string>();
        foreach (Match m in PathKey.Matches(vdf ?? ""))
            list.Add(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"));
        return list;
    }

    /// <summary>Steam's install folder from HKCU\Software\Valve\Steam\SteamPath (forward slashes normalized), or null.</summary>
    public static string SteamPathFromRegistry()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var v = k?.GetValue("SteamPath") as string;
            return string.IsNullOrWhiteSpace(v) ? null : Path.GetFullPath(v.Replace('/', '\\'));
        }
        catch { return null; }
    }

    /// <summary>Every Steam library folder: the Steam root(s) and the libraries their libraryfolders.vdf lists; existing ones only, de-duplicated.</summary>
    public static List<string> Libraries(IEnumerable<string> steamRoots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var res = new List<string>();
        void Add(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            string full;
            try { full = Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return; }
            if (Directory.Exists(full) && seen.Add(full)) res.Add(full);
        }
        foreach (var root in steamRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                try { foreach (var p in ParseLibraryFolders(File.ReadAllText(vdf))) Add(p); } catch (IOException) { }
        }
        return res;
    }

    /// <summary>Libraries of this PC (registry Steam path, else the default install folder).</summary>
    public static List<string> Libraries() => Libraries(new[] { SteamPathFromRegistry(), DefaultSteamRoot }.Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary><c>&lt;lib&gt;\steamapps\common\&lt;folder&gt;\Game</c> of the first library holding <paramref name="exe"/>, or null.</summary>
    public static string FindGame(IEnumerable<string> libraries, string folder, string exe)
    {
        foreach (var lib in libraries)
        {
            var game = Path.Combine(lib, "steamapps", "common", folder, "Game");
            if (File.Exists(Path.Combine(game, exe))) return game;
        }
        return null;
    }

    /// <summary>A user-given game location (exe, Game folder or install folder) as the folder holding <paramref name="exe"/>, or null.</summary>
    public static string NormalizeGameDir(string path, string exe)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (File.Exists(full)) full = Path.GetDirectoryName(full);
        if (File.Exists(Path.Combine(full, exe))) return full;
        if (File.Exists(Path.Combine(full, "Game", exe))) return Path.Combine(full, "Game");
        return null;
    }
}
