using System.Text;

namespace NRMerge;

/// <summary>
/// Stage (Task 14): ME3 profile, natives and their configs, staged under out\ (spec §10), and a backup copy of the save.
/// </summary>
public static class Profile
{
    public const string ProfileName = "Elden Vins with more map variations.me3";
    /// <summary>%APPDATA%\Nightreign: one folder per Steam account (named by its 64-bit Steam ID).</summary>
    public static string SaveRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nightreign");

    /// <summary>Every per-account save folder under <paramref name="root"/> (default <see cref="SaveRoot"/>); none when it is missing.</summary>
    public static IEnumerable<string> SaveDirs(string root = null)
    {
        root ??= SaveRoot;
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.GetDirectories(root).Where(d => ulong.TryParse(Path.GetFileName(d), out _)).OrderBy(d => d, StringComparer.Ordinal).ToArray();
    }

    public const string Me3 =
        "profileVersion = \"v1\"\n" +
        "start_online = true\n" +
        "disable_arxan = true\n" +
        "\n" +
        "[[supports]]\n" +
        "game = \"nightrein\"\n" +
        "\n" +
        "[[packages]]\n" +
        "id = \"mod\"\n" +
        "source = \"mod\"\n" +
        "\n" +
        "# MMV's newer server redirector build (one redirector only); config: mod/ServerRedirector/cl_server_redirector.ini\n" +
        "[[natives]]\n" +
        "path = \"mod/ServerRedirector/cl_server_redirector.dll\"\n" +
        "load_early = true\n" +
        "\n" +
        "[[natives]]\n" +
        "path = \"mod/dll/custom_drop_fxrs.dll\"\n" +
        "load_early = true\n" +
        "\n" +
        "# EV's natives (nighter.dll is referenced by EV's profile but not shipped with EV; config nighter.json is included)\n" +
        "[[natives]]\n" +
        "path = \"mod/dll/nighter.dll\"\n" +
        "\n" +
        "[[natives]]\n" +
        "path = \"mod/dll/NightreignFPSFOV.dll\"\n";

    /// <summary>EV's redirector settings with the new shard name and the alternative (EV) save enabled.</summary>
    public static string RedirectorIni(string mmvIni)
    {
        var lines = mmvIni.Replace("\r\n", "\n").Split('\n').ToList();
        void Set(string key, string value)
        {
            int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + "="));
            if (i >= 0) lines[i] = $"{key}={value}";
            else lines.Insert(lines.FindIndex(l => l.Trim() == "[cl_server_redirector]") + 1, $"{key}={value}");
        }
        Set("CL_SERVER_URL", "https://nightreign.fs-emu.net");
        Set("CL_CUSTOM_SHARD_NAME", "VINSMMV");
        Set("CL_USE_ALT_SAVE", "true");
        return string.Join("\r\n", lines);
    }

    static void Copy(string src, string rel)
    {
        var dst = Paths.In(Paths.OutMod, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.Copy(src, dst, true);
        Journal.Add("profile", rel, $"from {Path.GetFileName(Path.GetDirectoryName(src))}\\{Path.GetFileName(src)}");
    }

    public static int Run()
    {
        Journal.Clear();
        var mmvSr = Path.Combine(Paths.MMV, "ServerRedirector");
        foreach (var f in new[] { "cl_server_redirector.dll", "NightreignCustomServerLauncher.exe", "OpenSourceDisclosure.txt", "ReadMe.txt" })
            Copy(Path.Combine(mmvSr, f), "ServerRedirector/" + f);
        var ini = RedirectorIni(File.ReadAllText(Path.Combine(mmvSr, "cl_server_redirector.ini")));
        File.WriteAllText(Paths.In(Paths.OutMod, "ServerRedirector/cl_server_redirector.ini"), ini, new UTF8Encoding(false));
        Journal.Add("profile", "ServerRedirector/cl_server_redirector.ini", "MMV's file; shard VINSMMV, alternative save on (EV's save)");

        var evDll = Path.Combine(Paths.EV, "dll");
        Copy(Path.Combine(evDll, "NightreignFPSFOV.dll"), "dll/NightreignFPSFOV.dll");
        Copy(Path.Combine(evDll, "NightreignFPSFOV", "config.ini"), "dll/NightreignFPSFOV/config.ini");
        Copy(Path.Combine(evDll, "nighter.json"), "dll/nighter.json");
        if (File.Exists(Path.Combine(evDll, "nighter.dll"))) Copy(Path.Combine(evDll, "nighter.dll"), "dll/nighter.dll");
        else Journal.Add("profile", "dll/nighter.dll", "not present in EV's mod folder (EV's profile references it); profile entry kept as in EV");
        var mmvDll = Path.Combine(Paths.MMV, "dll");
        Copy(Path.Combine(mmvDll, "custom_drop_fxrs.dll"), "dll/custom_drop_fxrs.dll");
        Copy(Path.Combine(mmvDll, "custom_drop_fxrs.yaml"), "dll/custom_drop_fxrs.yaml");

        File.WriteAllText(Path.Combine(Paths.Out, ProfileName), Me3, new UTF8Encoding(false));
        Journal.Add("profile", ProfileName, "written (no savefile key: EV's alternative save through the redirector)");

        // Save backup (copy, never move), for every Steam account on this PC.
        bool any = false;
        foreach (var saveDir in SaveDirs())
        {
            var save = Path.Combine(saveDir, "NR0000.CL_SAVE");
            if (!File.Exists(save)) continue;
            var dir = Path.Combine(saveDir, "backup-before-merge");
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, $"NR0000.CL_SAVE.{DateTime.Now:yyyyMMdd-HHmmss}");
            File.Copy(save, dst, false);
            Journal.Add("profile", "save backup", dst);
            Console.WriteLine("save backed up to " + dst);
            any = true;
        }
        if (!any) Journal.Add("profile", "save backup", "NR0000.CL_SAVE not found");
        Journal.Save();
        return 0;
    }
}
