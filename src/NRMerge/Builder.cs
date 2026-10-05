using System.Diagnostics;
using System.Reflection;

namespace NRMerge;

/// <summary>Command-line options of <c>NRMerge build</c>. Every option is optional: a double-click finds everything.</summary>
public sealed class BuildOptions
{
    public string Ev, Mmv, Nightreign, EldenRing, Out, CacheDir, DataDir;
    public bool Force, KeepWork, NoEldenRing, NonInteractive;

    public const string Usage =
        "usage: NRMerge build [--ev <folder|archive>] [--mmv <folder|archive>] [--nightreign <GameDir>] [--eldenring <GameDir> | --no-eldenring]\n" +
        "                     [--out <dir>] [--cache-dir <dir>] [--data-dir <dir>] [--force] [--keep-work] [--non-interactive]";

    public static BuildOptions Parse(IEnumerable<string> args)
    {
        var o = new BuildOptions();
        var a = args.ToList();
        for (int i = 0; i < a.Count; i++)
        {
            string Value() => i + 1 < a.Count ? a[++i] : throw new BuildException($"{a[i]} needs a value.\n{Usage}");
            switch (a[i].ToLowerInvariant())
            {
                case "--ev": o.Ev = Value(); break;
                case "--mmv": o.Mmv = Value(); break;
                case "--nightreign": o.Nightreign = Value(); break;
                case "--eldenring": o.EldenRing = Value(); break;
                case "--out": o.Out = Value(); break;
                case "--cache-dir": o.CacheDir = Value(); break;
                case "--data-dir": o.DataDir = Value(); break;
                case "--force": o.Force = true; break;
                case "--keep-work": o.KeepWork = true; break;
                case "--no-eldenring": o.NoEldenRing = true; break;
                case "--non-interactive": o.NonInteractive = true; break;
                default: throw new BuildException($"unknown option '{a[i]}'\n{Usage}");
            }
        }
        if (o.NoEldenRing && o.EldenRing != null) throw new BuildException("--eldenring and --no-eldenring cannot be combined.");
        return o;
    }
}

/// <summary>Everything the builder does to the outside world, replaceable by tests (no dialogs, no processes, no network).</summary>
public sealed class BuilderEnv
{
    public TextWriter Out = Console.Out;
    /// <summary>False = <c>--non-interactive</c>: no prompt, dialog, Explorer window or process launch.</summary>
    public bool Interactive = true;
    /// <summary>y/N question; the default answer is No.</summary>
    public Func<string, bool> Confirm = ConsoleConfirm;
    /// <summary>(title, filter) -> chosen file or null (WinForms OpenFileDialog on an STA thread).</summary>
    public Func<string, string, string> PickFile = Dialogs.PickFile;
    public Action<string> OpenFolder = dir => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    /// <summary>(exe, arguments, wait) -> exit code (0 when not waiting).</summary>
    public Func<string, string, bool, int> RunProcess = (exe, args, wait) =>
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        if (!wait) return 0;
        p.WaitForExit();
        return p.ExitCode;
    };
    public Func<List<string>> SteamLibraries = SteamLocator.Libraries;
    public Func<string> FindMe3 = Me3.Find;
    /// <summary>Folders searched for the mod downloads (Nightreign Game folder, Steam libraries) -> roots.</summary>
    public Func<string, List<string>, List<string>> ModRoots = ModLocator.DefaultRoots;
    /// <summary>HTTP handler for pinned files and the ME3 installer (null = network).</summary>
    public HttpMessageHandler Http;
    public Func<BuildConfig, List<string>> CheckGames = GameCheck.Check;
    public Func<string, string> ReadErRegulationVersion = dir => GameCheck.ReadRegulationVersion(Path.Combine(dir, "regulation.bin"), "ER");

    static bool ConsoleConfirm(string question)
    {
        Console.Write(question + " [y/N] ");
        var line = Console.ReadLine();
        return line != null && line.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Folder/file pickers (WinForms on an STA thread); used only when detection fails or is ambiguous.</summary>
public static class Dialogs
{
    public static string PickFile(string title, string filter) => Sta(() =>
    {
        using var d = new System.Windows.Forms.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, RestoreDirectory = true };
        return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? d.FileName : null;
    });

    static string Sta(Func<string> f)
    {
        string result = null;
        Exception error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new BuildException("Could not open the file picker: " + error.Message, error);
        return result;
    }
}

/// <summary>
/// <c>NRMerge build</c>: finds the games and both mod downloads, validates them, prepares the bases (Oodle, vanilla extract,
/// pinned fetches), runs every merge stage, verifies and delivers into the output folder. Its workspace is
/// <c>&lt;output&gt;\_build</c>, deleted after success unless <c>--keep-work</c>.
/// </summary>
public static class Builder
{
    public const string WorkFolder = "_build";
    public const string DefaultOutputName = "Elden Vins with more map variations";
    public static readonly string ModFilter = "Mod download (*.me3, *.zip, *.7z, *.rar)|*.me3;*.zip;*.7z;*.rar|All files|*.*";

    public static string Version =>
        typeof(Builder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "dev";

    public static readonly (string Name, string What, Func<int> Run)[] Stages =
    {
        ("assemble", "classify and copy both mods' files", Assemble.Run),
        ("regmerge", "merge the regulation (params)", RegMerge.Run),
        ("mmvrewrite", "re-point MMV references to moved rows", () => MmvRewrite.Run(false)),
        ("contentmerge", "merge binders, effects, text, weapon models", ContentMerge.Run),
        ("emevdmerge", "merge event scripts", EmevdMerge.Run),
        ("msbmerge", "merge maps", MsbMerge.Run),
        ("enemymerge", "merge enemies (TAE, behavior, AI)", EnemyMerge.Run),
        ("playerscripts", "merge player scripts (c0000.hks)", PlayerScripts.Run),
        ("playermerge", "merge player animations and behavior", PlayerMerge.Run),
        ("profile", "write the ME3 profile and natives", Profile.Run),
        ("noerpatch", "without Elden Ring: replay the verified build's Elden-Ring-based decisions", NoErPatch.Run),
        ("verify", "verify the merged mod", Verify.Run),
    };

    // ------------------------------------------------------------------ entry point

    public static int Main(string[] args, BuilderEnv env = null)
    {
        env ??= new BuilderEnv();
        BuildOptions o;
        try { o = BuildOptions.Parse(args); }
        catch (BuildException e) { env.Out.WriteLine(e.Message); return 2; }
        if (o.NonInteractive) env.Interactive = false;
        try { return Run(o, env); }
        catch (BuildException e)
        {
            env.Out.WriteLine();
            env.Out.WriteLine("BUILD STOPPED: " + e.Message);
            return 1;
        }
    }

    sealed class Step(BuilderEnv env, int total)
    {
        int n;
        Stopwatch sw;
        string name;
        public void Begin(string what)
        {
            End();
            name = what;
            env.Out.WriteLine($"[{++n}/{total}] {what}");
            sw = Stopwatch.StartNew();
        }
        public void End()
        {
            if (sw == null) return;
            env.Out.WriteLine($"      {name}: done in {Fmt(sw.Elapsed)}");
            sw = null;
        }
    }

    static string Fmt(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m{t.Seconds:00}s" : $"{t.TotalSeconds:0.0}s";

    public static int Run(BuildOptions o, BuilderEnv env)
    {
        var total = Stopwatch.StartNew();
        var w = env.Out;
        w.WriteLine($"Elden Vins x More Map Variations merge builder {Version}");
        w.WriteLine();
        var step = new Step(env, 8);
        ErFallback.Reset();

        // 1. Find everything.
        step.Begin("Finding the games and the two mods");
        var cfg = new BuildConfig
        {
            DataDir = ResolveDataDir(o.DataDir),
            CacheDir = Path.GetFullPath(o.CacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VinsMMV-Merge-Builder", "cache")),
            Force = o.Force, KeepWork = o.KeepWork,
        };
        var libs = Safe(() => env.SteamLibraries()) ?? new List<string>();
        cfg.NrGame = LocateNightreign(o, env, libs);
        w.WriteLine($"      Nightreign:  {cfg.NrGame}");
        cfg.ErGame = LocateEldenRing(o, env, libs, out var erNote);
        w.WriteLine($"      Elden Ring:  {cfg.ErGame ?? erNote}");
        cfg.OutputDir = ResolveOutput(o.Out, cfg.NrGame);
        cfg.WorkDir = Path.Combine(cfg.OutputDir, WorkFolder);
        w.WriteLine($"      Output:      {cfg.OutputDir}");
        var manifest = LoadManifest(cfg);
        var outRoot = cfg.OutputDir;
        var modRoots = Safe(() => env.ModRoots(cfg.NrGame, libs)) ?? new List<string>();
        var nrRoot = Path.GetDirectoryName(cfg.NrGame.TrimEnd('\\'));
        var evSrc = LocateMod(manifest.Get("ev"), o.Ev, env, modRoots, nrRoot, outRoot);
        var mmvSrc = LocateMod(manifest.Get("mmv"), o.Mmv, env, modRoots, nrRoot, outRoot);
        if (cfg.ErGame == null) w.WriteLine(ErNotice);

        // 2. Games.
        step.Begin("Checking the game versions");
        var gameProblems = env.CheckGames(cfg);
        Refuse(gameProblems, o.Force, env, "game");
        w.WriteLine(gameProblems.Count == 0 ? "      games OK" : "      continuing anyway (--force)");

        // 3. Mods: extract archives, hash-check, swapped folders.
        step.Begin("Checking the two mod downloads (SHA-256 of every file)");
        PrepareWorkspace(cfg);
        var evRoot = Materialize(manifest.Get("ev"), evSrc, cfg, env);
        var mmvRoot = Materialize(manifest.Get("mmv"), mmvSrc, cfg, env);
        CheckMods(manifest, ref evRoot, ref mmvRoot, o.Force, env);
        cfg.EvMod = Path.Combine(evRoot, "mod");
        cfg.MmvMod = Path.Combine(mmvRoot, "mod");

        // 4. Mod Engine 3.
        step.Begin("Mod Engine 3");
        var me3 = EnsureMe3(cfg, env);

        // 5. Pinned downloads.
        step.Begin("Downloading the pinned third-party files");
        FetchPinned(cfg, env);

        // 6. Bases from the player's games.
        step.Begin("Extracting the vanilla base files from your games");
        Paths.Use(cfg);
        Oodle.Prepare(cfg, Path.Combine(Path.GetTempPath(), "NRMerge", cfg.ErGame == null ? "oodle-9" : "oodle-9-6"));
        VanillaManifest.Extract(cfg, new ConsoleProgress(s => w.WriteLine("      " + s)));

        // 7. Merge.
        step.Begin($"Merging ({Stages.Length} stages, about 20-30 minutes)");
        for (int i = 0; i < Stages.Length; i++)
        {
            var (name, what, run) = Stages[i];
            w.WriteLine($"      stage {i + 1}/{Stages.Length} {name}: {what}");
            var sw = Stopwatch.StartNew();
            int rc;
            try { rc = run(); }
            catch (BuildException) { throw; }
            catch (Exception e)
            {
                var log = Path.Combine(cfg.WorkDir, "builder-error.log");
                File.WriteAllText(log, e.ToString());
                throw new BuildException($"stage {name} failed: {e.Message}\n  details: {log}", e);
            }
            if (rc != 0) throw new BuildException($"stage {name} failed (exit code {rc}); see the messages above.");
            w.WriteLine($"      stage {i + 1}/{Stages.Length} {name}: {Fmt(sw.Elapsed)}");
        }

        // 8. Deliver.
        step.Begin("Delivering");
        ErFallback.Save(Journal.Dir);
        var coop = CoopCode.Compute(Paths.OutMod);
        if (Report.Deliver(Report.EmbeddedRulings(), Report.VersionText(Version, DateTime.Now, cfg.ErGame != null, coop)) != 0)
            throw new BuildException("delivery refused (see above).");
        Directory.SetCurrentDirectory(cfg.OutputDir);
        if (!o.KeepWork)
        {
            try { Directory.Delete(cfg.WorkDir, true); }
            catch (Exception e) { w.WriteLine($"      could not delete the work folder {cfg.WorkDir} ({e.Message}); you can delete it yourself."); }
        }
        step.End();

        w.WriteLine();
        w.WriteLine($"DONE in {Fmt(total.Elapsed)}. The merged mod is in:");
        w.WriteLine("  " + cfg.OutputDir);
        w.WriteLine($"  Play it with Mod Engine 3: \"{Path.Combine(cfg.OutputDir, Profile.ProfileName)}\"");
        w.WriteLine($"  Co-op code: {coop}  (everyone in a co-op group needs the same code; it is also in VERSION.txt)");
        if (cfg.ErGame == null) w.WriteLine(ErNotice);
        Finish(cfg, me3, env);
        return 0;
    }

    public const string ErNotice =
        "      NOTE: building without Elden Ring. The decisions that need Elden Ring's originals are replayed from the verified build\n" +
        "      (data\\noer-patches), so the game data is identical to a build with Elden Ring (same co-op code).";

    static T Safe<T>(Func<T> f) where T : class { try { return f(); } catch { return null; } }

    // ------------------------------------------------------------------ steps (public for tests)

    /// <summary>Release layout: <c>&lt;exe&gt;\data</c> when it holds the manifests; otherwise the dev checkouts (null).</summary>
    public static string ResolveDataDir(string given)
    {
        if (given != null) return Path.GetFullPath(given);
        var d = Path.Combine(AppContext.BaseDirectory, "data");
        return File.Exists(Path.Combine(d, "mod-manifest.json")) ? d : null;
    }

    static ModManifest LoadManifest(BuildConfig cfg)
    {
        Paths.Use(cfg);
        return ModManifest.Load();
    }

    public static string LocateNightreign(BuildOptions o, BuilderEnv env, List<string> libs)
    {
        if (o.Nightreign != null)
            return SteamLocator.NormalizeGameDir(o.Nightreign, SteamLocator.NightreignExe)
                ?? throw new BuildException($"--nightreign \"{o.Nightreign}\": no {SteamLocator.NightreignExe} there. Give the Nightreign 'Game' folder.");
        var found = SteamLocator.FindGame(libs, SteamLocator.NightreignFolder, SteamLocator.NightreignExe);
        if (found != null) return found;
        if (env.Interactive)
        {
            env.Out.WriteLine("      Nightreign was not found in your Steam libraries; please select nightreign.exe.");
            var pick = env.PickFile("Select nightreign.exe (ELDEN RING NIGHTREIGN\\Game)", "nightreign.exe|nightreign.exe");
            var dir = pick == null ? null : SteamLocator.NormalizeGameDir(pick, SteamLocator.NightreignExe);
            if (dir != null) return dir;
        }
        throw new BuildException("ELDEN RING NIGHTREIGN was not found in your Steam libraries. Run the builder with --nightreign \"<...\\ELDEN RING NIGHTREIGN\\Game>\".");
    }

    /// <summary>The Elden Ring Game folder, or null = build without Elden Ring (<c>--no-eldenring</c>, not installed, or not 1.16.1).</summary>
    public static string LocateEldenRing(BuildOptions o, BuilderEnv env, List<string> libs, out string note)
    {
        note = null;
        if (o.NoEldenRing) { note = "not used (--no-eldenring)"; return null; }
        string dir;
        if (o.EldenRing != null)
            dir = SteamLocator.NormalizeGameDir(o.EldenRing, SteamLocator.EldenRingExe)
                ?? throw new BuildException($"--eldenring \"{o.EldenRing}\": no {SteamLocator.EldenRingExe} there. Give the ELDEN RING 'Game' folder, or use --no-eldenring.");
        else
        {
            dir = SteamLocator.FindGame(libs, SteamLocator.EldenRingFolder, SteamLocator.EldenRingExe);
            if (dir == null) { note = "not found (optional): building without it"; return null; }
        }
        string reg;
        try { reg = env.ReadErRegulationVersion(dir); } catch (Exception e) { reg = "unreadable (" + e.Message + ")"; }
        if (reg != GameCheck.EldenRingRegulation)
        {
            if (o.EldenRing != null && !o.Force)
                throw new BuildException($"Elden Ring at \"{dir}\" is regulation {reg}, the merge needs {GameCheck.EldenRingRegulation} (1.16.1). Update it in Steam, or use --no-eldenring.");
            note = $"found at {dir} but its regulation is {reg}, not {GameCheck.EldenRingRegulation} (1.16.1): building without it";
            return o.Force && o.EldenRing != null ? dir : null;
        }
        return dir;
    }

    /// <summary>The output folder: <paramref name="given"/>, else "&lt;Nightreign install&gt;\Elden Vins with more map variations"
    /// (" (2)", " (3)"... when taken). It must be new, empty, or hold only a previous attempt's <c>_build</c>.</summary>
    public static string ResolveOutput(string given, string nrGame)
    {
        string dir;
        if (given != null) dir = Path.GetFullPath(given).TrimEnd('\\');
        else
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(nrGame).TrimEnd('\\'));
            dir = Path.Combine(root, DefaultOutputName);
            for (int i = 2; !Usable(dir); i++) dir = Path.Combine(root, $"{DefaultOutputName} ({i})");
        }
        if (!Usable(dir))
        {
            var merged = File.Exists(Path.Combine(dir, ModManifest.MergedMe3)) || File.Exists(Path.Combine(dir, "MERGE_REPORT.md"));
            throw new BuildException($"The output folder \"{dir}\" is not empty{(merged ? " (it holds a merged build)" : "")}. "
                + "Choose a new or empty folder with --out; the builder never overwrites an existing build or other files.");
        }
        return dir;
    }

    static bool Usable(string dir) =>
        !Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).All(e => string.Equals(Path.GetFileName(e), WorkFolder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Creates <c>_build</c>; a previous attempt's merge outputs are removed, its verified vanilla extract kept.</summary>
    static void PrepareWorkspace(BuildConfig cfg)
    {
        Directory.CreateDirectory(cfg.WorkDir);
        foreach (var d in new[] { "out", "work", "mods" })
            if (Directory.Exists(Path.Combine(cfg.WorkDir, d))) Directory.Delete(Path.Combine(cfg.WorkDir, d), true);
    }

    /// <summary>A mod's download (folder root or archive): given on the command line, else the best detected candidate; asks with a
    /// picker when nothing or several equally good candidates were found (non-interactive: error naming them).</summary>
    public static string LocateMod(ModSpec spec, string given, BuilderEnv env, IEnumerable<string> roots, string nrRoot, string outputDir)
    {
        if (given != null)
        {
            var full = Path.GetFullPath(given.Trim('"'));
            if (ModLocator.IsArchive(full) && File.Exists(full)) return full;
            if (!Directory.Exists(full) && !File.Exists(full)) throw new BuildException($"{spec.Name}: \"{given}\" does not exist.");
            return ModLocator.NormalizeFolder(full, spec.Me3);
        }
        var ranked = ModLocator.Find(spec, roots, nrRoot, new[] { outputDir });
        var pick = ModLocator.Choose(ranked, out var ambiguous);
        if (pick != null)
        {
            env.Out.WriteLine($"      {spec.ShortName ?? spec.Name}: {pick.Path} ({pick.Why})");
            return pick.Path;
        }
        var why = ranked.Count == 0
            ? $"{spec.DisplayName} was not found (looked for a folder or .zip/.7z/.rar containing \"{spec.Me3}\" in: {string.Join("; ", roots)})."
            : $"{spec.DisplayName} was found in several places:\n" + string.Join("\n", ranked.Select(c => $"        {c.Path} ({c.Why})"));
        if (env.Interactive)
        {
            env.Out.WriteLine("      " + why);
            env.Out.WriteLine($"      Please select {spec.Me3} in the extracted mod folder, or the downloaded archive.");
            var p = env.PickFile($"Select {spec.Name}: \"{spec.Me3}\" or the downloaded .zip/.7z/.rar ({spec.NexusUrl})", ModFilter);
            if (p != null) return ModLocator.IsArchive(p) ? Path.GetFullPath(p) : ModLocator.NormalizeFolder(p, spec.Me3);
        }
        var opt = spec.Id == "ev" ? "--ev" : "--mmv";
        throw new BuildException($"{why}\n  Download it from {spec.NexusUrl} and/or run the builder with {opt} \"<folder or archive>\".");
    }

    /// <summary>An archive is extracted into <c>_build\mods\&lt;id&gt;</c>; a folder is used in place (read-only).</summary>
    static string Materialize(ModSpec spec, string src, BuildConfig cfg, BuilderEnv env)
    {
        if (!ModLocator.IsArchive(src)) return src;
        env.Out.WriteLine($"      extracting {Path.GetFileName(src)}");
        return ModLocator.Extract(src, Path.Combine(cfg.WorkDir, "mods", spec.Id), spec.Me3, new ConsoleProgress(s => env.Out.WriteLine("    " + s)));
    }

    /// <summary>Hash-checks both downloads. Both folders swapped -> swapped back with a notice. Any other mismatch refuses
    /// unless <paramref name="force"/>.</summary>
    public static void CheckMods(ModManifest m, ref string evRoot, ref string mmvRoot, bool force, BuilderEnv env)
    {
        ModCheckResult Check(string id, string folder)
        {
            var spec = m.Get(id);
            env.Out.WriteLine($"      {spec.DisplayName}: {folder}");
            var progress = new ConsoleHashProgress("      hashing");
            var r = m.Check(id, folder, progress);
            progress.Done();
            env.Out.WriteLine(Indent(r.FormatReport()));
            return r;
        }
        var ev = Check("ev", evRoot);
        var mmv = Check("mmv", mmvRoot);
        if (ev.Kind == ModDiagnosis.Swapped && mmv.Kind == ModDiagnosis.Swapped)
        {
            env.Out.WriteLine("      The two mod folders were given the wrong way round (Elden Vins <-> MMV): using them swapped.");
            (evRoot, mmvRoot) = (mmvRoot, evRoot);
            ev = Check("ev", evRoot);
            mmv = Check("mmv", mmvRoot);
        }
        evRoot = ev.Root ?? evRoot;
        mmvRoot = mmv.Root ?? mmvRoot;
        var problems = new[] { ev, mmv }.Where(r => !r.Ok).Select(r => $"{r.Spec.DisplayName}: {r.Diagnosis}").ToList();
        Refuse(problems, force, env, "mod");
    }

    static string Indent(string s) => string.Join("\n", (s ?? "").Replace("\r\n", "\n").Split('\n').Select(l => "      " + l));

    static void Refuse(List<string> problems, bool force, BuilderEnv env, string what)
    {
        if (problems.Count == 0) return;
        var text = string.Join("\n  ", problems);
        if (!force)
            throw new BuildException($"{text}\n  The merge's hand resolutions were made for exactly these versions. Fix the {what} files, or run with --force to build anyway (the result may be broken).");
        env.Out.WriteLine("      WARNING (--force): " + text.Replace("\n", "\n      "));
    }

    /// <summary>me3.exe, installing it on request when missing (never in non-interactive mode); null when absent.</summary>
    public static string EnsureMe3(BuildConfig cfg, BuilderEnv env)
    {
        var me3 = Safe(() => env.FindMe3());
        if (me3 != null) { env.Out.WriteLine($"      found {me3}"); return me3; }
        env.Out.WriteLine("      Mod Engine 3 is not installed (it is needed to PLAY the merged mod, not to build it).");
        if (!env.Interactive || !env.Confirm("      Download and run the official Mod Engine 3 installer from GitHub (garyttierney/me3) now?"))
        {
            env.Out.WriteLine("      Install it later from https://github.com/garyttierney/me3/releases");
            return null;
        }
        var installer = Me3.DownloadInstaller(Path.Combine(cfg.WorkDir, "me3"), env.Http);
        env.Out.WriteLine($"      running {installer}");
        var rc = env.RunProcess(installer, "", true);
        me3 = Safe(() => env.FindMe3());
        env.Out.WriteLine(me3 != null ? $"      installed: {me3}" : $"      the installer finished (exit code {rc}) but me3 was not found; install it later from https://github.com/garyttierney/me3/releases");
        return me3;
    }

    /// <summary>Downloads (or finds in the cache) every pinned file before the long work starts.</summary>
    public static void FetchPinned(BuildConfig cfg, BuilderEnv env)
    {
        foreach (var id in Fetch.Catalog(cfg).Keys)
        {
            var sw = Stopwatch.StartNew();
            var p = Fetch.Get(id, cfg, env.Http);
            env.Out.WriteLine($"      {id}: {p} ({Fmt(sw.Elapsed)})");
        }
    }

    static void Finish(BuildConfig cfg, string me3, BuilderEnv env)
    {
        if (!env.Interactive) return;
        try { env.OpenFolder(cfg.OutputDir); } catch { }
        if (me3 == null) return;
        if (env.Confirm("Launch ELDEN RING NIGHTREIGN with the merged mod now?"))
            env.RunProcess(me3, Me3.LaunchArguments(Path.Combine(cfg.OutputDir, Profile.ProfileName)), false);
    }
}
