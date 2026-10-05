using System.Diagnostics;

namespace NRMerge;

/// <summary>Checks the installed games are the versions this merge was made for.
/// Nightreign: nightreign.exe FileVersion and regulation version must both match.
/// Elden Ring: required (merge base for ER-ported content); only its regulation version is enforced,
/// the exe version is shown in messages (observed 2.6.1.0 with regulation 11611000 = 1.16.1).</summary>
public static class GameCheck
{
    public const string NightreignExeVersion = "1.3.3.0";
    public const string NightreignRegulation = "10350000";
    public const string EldenRingRegulation = "11611000";

    /// <summary>"10350000" -> "1.03.5", "11611000" -> "1.16.1" (major, two-digit minor, patch).</summary>
    public static string RegulationDisplay(string version)
    {
        if (version == null || version.Length < 4 || !version.All(char.IsAsciiDigit)) return version ?? "unknown";
        return $"{version[0]}.{version.Substring(1, 2)}.{version[3]}";
    }

    /// <summary>Checks the configured games with the real readers (SoulsFormats regulation decrypt, exe FileVersion).</summary>
    /// <remarks>cfg.ErGame null = building without Elden Ring: only Nightreign is checked.</remarks>
    public static List<string> Check(BuildConfig cfg) =>
        Check(cfg.NrGame, cfg.ErGame, path => ReadRegulationVersion(path, IsUnder(path, cfg.ErGame) ? "ER" : "NR"), eldenRingOptional: cfg.ErGame == null);

    /// <summary>Version string of an encrypted regulation.bin (e.g. "10350000"): the BND4 header version after decryption.</summary>
    public static string ReadRegulationVersion(string path, string game)
    {
        var bytes = File.ReadAllBytes(path);
        var bnd = game == "ER" ? SoulsFormats.SFUtil.DecryptERRegulation(bytes) : SoulsFormats.SFUtil.DecryptNightreignRegulation(bytes);
        return bnd.Version;
    }

    static bool IsUnder(string path, string dir) =>
        !string.IsNullOrEmpty(dir) &&
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <param name="regulationVersionReader">Path of a regulation.bin -> its version string (e.g. "10350000").
    /// The builder passes a SoulsFormats-based reader (the file is encrypted).</param>
    /// <param name="exeVersionReader">Path of an exe -> FileVersion; defaults to <see cref="FileVersionInfo"/>.</param>
    public static List<string> Check(string nightreignGameDir, string eldenRingGameDir,
        Func<string, string> regulationVersionReader, Func<string, string> exeVersionReader = null, bool eldenRingOptional = false)
    {
        exeVersionReader ??= ReadExeVersion;
        var problems = new List<string>();

        string nrWant = RegulationDisplay(NightreignRegulation);
        var nrExe = nightreignGameDir == null ? null : Path.Combine(nightreignGameDir, "nightreign.exe");
        if (nrExe == null || !File.Exists(nrExe))
            problems.Add($"Nightreign not found: '{nrExe ?? "nightreign.exe"}' does not exist. Select the Nightreign 'Game' folder (…\\steamapps\\common\\ELDEN RING NIGHTREIGN\\Game).");
        else
        {
            string exeVer = Safe(() => exeVersionReader(nrExe));
            var (reg, err) = ReadRegulation(regulationVersionReader, nightreignGameDir);
            if (err != null)
                problems.Add($"Could not read the Nightreign regulation version from '{Path.Combine(nightreignGameDir, "regulation.bin")}': {err}. Verify the game files in Steam.");
            else if (exeVer != NightreignExeVersion || reg != NightreignRegulation)
                problems.Add($"Nightreign {nrWant} required, found {RegulationDisplay(reg)} (exe {exeVer ?? "unknown"}, regulation {reg}; expected exe {NightreignExeVersion}, regulation {NightreignRegulation}). Update the game in Steam or verify its files.");
        }

        if (eldenRingGameDir == null && eldenRingOptional) return problems;
        string erWant = RegulationDisplay(EldenRingRegulation);
        var erExe = eldenRingGameDir == null ? null : Path.Combine(eldenRingGameDir, "eldenring.exe");
        if (erExe == null || !File.Exists(erExe))
            problems.Add($"Elden Ring not found{(erExe == null ? "" : $": '{erExe}' does not exist")}. Elden Ring {erWant} (regulation {EldenRingRegulation}) must be installed: it is required because the merge takes the Elden Ring content that Elden Vins ports into Nightreign from your Elden Ring install. Select the Elden Ring 'Game' folder.");
        else
        {
            string exeVer = Safe(() => exeVersionReader(erExe));
            var (reg, err) = ReadRegulation(regulationVersionReader, eldenRingGameDir);
            if (err != null)
                problems.Add($"Could not read the Elden Ring regulation version from '{Path.Combine(eldenRingGameDir, "regulation.bin")}': {err}. Verify the game files in Steam.");
            else if (reg != EldenRingRegulation)
                problems.Add($"Elden Ring {erWant} required (regulation {EldenRingRegulation}), found {RegulationDisplay(reg)} (exe {exeVer ?? "unknown"}, regulation {reg}). Update the game in Steam or verify its files.");
        }
        return problems;
    }

    static (string version, string error) ReadRegulation(Func<string, string> reader, string gameDir)
    {
        var path = Path.Combine(gameDir, "regulation.bin");
        if (!File.Exists(path)) return (null, "file not found");
        try
        {
            var v = reader(path);
            return string.IsNullOrWhiteSpace(v) ? (null, "no version found") : (v.Trim(), null);
        }
        catch (Exception e) { return (null, e.Message); }
    }

    static string Safe(Func<string> f)
    {
        try { var s = f(); return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); } catch { return null; }
    }

    public static string ReadExeVersion(string path)
    {
        var fv = FileVersionInfo.GetVersionInfo(path);
        return fv.FileMajorPart == 0 && fv.FileMinorPart == 0 && fv.FileBuildPart == 0 && fv.FilePrivatePart == 0
            ? null
            : $"{fv.FileMajorPart}.{fv.FileMinorPart}.{fv.FileBuildPart}.{fv.FilePrivatePart}";
    }
}
