using System.Runtime.InteropServices;

namespace NRMerge;

/// <summary>
/// Oodle comes from the player's games, never from our release: SoulsFormats looks for <c>oo2core_*_win64.dll</c> in the
/// current directory and prefers version 6 (Elden Ring) over 9 (Nightreign) whenever both are present; today's build ran with
/// both, so both are required for identical DCX output.
/// </summary>
public static class Oodle
{
    public const string NrDll = "oo2core_9_win64.dll", ErDll = "oo2core_6_win64.dll";

    /// <summary>Copies the DLLs from the game folders into <paramref name="dir"/> (skipping identical copies); returns <paramref name="dir"/>.
    /// <paramref name="erGame"/> null = no Elden Ring: only Nightreign's oo2core_9 (a stale oo2core_6 in <paramref name="dir"/> is removed).</summary>
    public static string Stage(string nrGame, string erGame, string dir)
    {
        var src = new List<(string file, string folder, string game)> { (Path.Combine(nrGame ?? "", NrDll), nrGame, "Nightreign") };
        if (erGame != null) src.Add((Path.Combine(erGame, ErDll), erGame, "Elden Ring"));
        foreach (var (file, folder, game) in src)
            if (!File.Exists(file))
                throw new BuildException($"{Path.GetFileName(file)} not found in \"{folder}\". Point the builder at the {game} Game folder (the one containing the game's exe).");
        Directory.CreateDirectory(dir);
        if (erGame == null && File.Exists(Path.Combine(dir, ErDll))) File.Delete(Path.Combine(dir, ErDll));
        foreach (var (file, _, _) in src)
        {
            var dst = Path.Combine(dir, Path.GetFileName(file));
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(file).Length
                && File.ReadAllBytes(dst).AsSpan().SequenceEqual(File.ReadAllBytes(file))) continue;
            File.Copy(file, dst, true);
        }
        return dir;
    }

    /// <summary>Stages the DLLs into <paramref name="dir"/> (default <c>&lt;WorkDir&gt;\oodle</c>), makes that the current directory
    /// and loads them. Without Elden Ring only oo2core_9 is used, so recompressed DCX files differ in bytes (journaled).</summary>
    public static void Prepare(BuildConfig cfg, string dir = null)
    {
        dir = Stage(cfg.NrGame, cfg.ErGame, dir ?? Path.Combine(cfg.WorkDir, "oodle"));
        Directory.SetCurrentDirectory(dir);
        NativeLibrary.Load(Path.Combine(dir, NrDll));
        if (cfg.ErGame != null) NativeLibrary.Load(Path.Combine(dir, ErDll));
        else ErFallback.Note("Oodle", "no Elden Ring oo2core_6_win64.dll: DCX files are compressed with Nightreign's oo2core_9, so every .dcx the merge writes differs in bytes from the verified build (same content)");
    }
}
