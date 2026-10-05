using Andre.Formats;
using NRMerge;
using SoulsFormats;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
LibraryLogging.Quiet(); // no Andre/SoulsFormats "dbug:" console lines
if (args.Length == 0)
{
    // Double-click: the turnkey build with every prompt enabled; keep the window open at the end.
    int rc = Builder.Main(Array.Empty<string>());
    Console.WriteLine();
    Console.Write("Press Enter to close this window.");
    Console.ReadLine();
    return rc;
}

switch (args[0])
{
    case "reginfo":
        foreach (var p in args.Skip(1))
        {
            var l = Regulation.Load(p, p.Contains("ELDEN RING/") || p.Contains("ELDEN RING\\") ? "ER" : "NR");
            Console.WriteLine($"{p}\n  version={l.Version} files={l.Bnd.Files.Count} params={l.Params.Count} errors={l.Errors.Count} rows={l.Params.Values.Sum(x => x.Rows.Count)}");
            foreach (var e in l.Errors.Take(20)) Console.WriteLine("  ERR " + e);
        }
        return 0;

    case "regdiff3":
        return RegDiff3.Run(args[1], args[2], args[3], args[4], args[5], args[6]);

    case "regdump":
        return RegDump.Run(args[1], args[2], args[3], args[4]);

    case "emevdview":
        return EmevdDiffView.Run(args[1], long.Parse(args[2]));

    case "emevddump":
        return EmevdDump.Run(args[1], long.Parse(args[2]));

    case "msbshow":
        {
            var msb = MSB_NR.Read(Dcx.Decompress(File.ReadAllBytes(args[1])));
            foreach (var x in msb.Parts.GetEntries().Cast<MSB_NR.Entry>().Concat(msb.Models.GetEntries()).Concat(msb.Events.GetEntries()).Concat(msb.Regions.GetEntries()))
                if (args.Skip(2).Any(n => x.Name.Contains(n))) Console.WriteLine($"{x.GetType().Name}	{x.Name}	{ObjMerge.Sig(x)}");
            return 0;
        }

    case "hkxtypes":
        return HkxTypes.Run(args[1], args.Skip(2).ToArray());

    case "hkxcompare":
        return HkxTypeCompare.Run(args[1]);

    case "behtest":
        foreach (var r in args.Skip(1)) { Console.WriteLine("### " + r); BehTest.Run(r); }
        return 0;

    case "behtables":
        return BehTables.Run(args[1]);

    case "bnddump":
        return BndDump.Run(args[1], args[2]);

    case "flverinfo":
        return FlverInfo.Run(args.Skip(1).ToArray());

    case "behprobe":
        foreach (var r in args.Skip(1)) { Console.WriteLine("### " + r); BehProbe.Run(r); }
        return 0;

    case "judgeusers":
        return JudgeUsers.Run(args[1], args[2].Split(',').Select(int.Parse).ToArray());

    case "taejudges":
        return TaeJudges.Run(args[1], args[2]);

    case "taepcbeh":
        return TaePcBehavior.Run(long.Parse(args[1]), long.Parse(args[2]));

    case "clipparents":
        return ClipParents.Run(args.Skip(1).ToArray());

    case "animusage":
        return AnimUsage.Run(args.Skip(1).ToArray());

    case "build":
        // build [options]: the whole pipeline for a player (see BuildOptions.Usage)
        return Builder.Main(args.Skip(1).ToArray());

    case "selftest":
        // selftest: checks lua502.dll, libzstd.dll, the Havok registry, SharpCompress and the release data folder (no game needed)
        return SelfTest.Run(Console.Out);

    case "compare-build":
        {
            // compare-build <modA> <modB>: files that differ; for DCX files also whether the decompressed content is equal
            // (e.g. recompressed with another Oodle version). Runtime logs (dll\logs\, *.log) are ignored.
            if (args.Length < 3) { Console.WriteLine("usage: compare-build <modA> <modB>"); return 1; }
            string a = Path.GetFullPath(args[1]), b = Path.GetFullPath(args[2]);
            NRMerge.Oodle.Prepare(BuildConfig.Dev(), Path.Combine(Path.GetTempPath(), "NRMerge", "oodle-9-6"));
            static IEnumerable<string> Rel(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(r => !r.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !r.Contains("dll/logs/", StringComparison.OrdinalIgnoreCase));
            var ra = Rel(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rb = Rel(b).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int same = 0, bytesOnly = 0, content = 0;
            foreach (var r in ra.Except(rb).Order(StringComparer.Ordinal)) Console.WriteLine($"only-in-a\t{r}");
            foreach (var r in rb.Except(ra).Order(StringComparer.Ordinal)) Console.WriteLine($"only-in-b\t{r}");
            foreach (var r in ra.Intersect(rb, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                var fa = Paths.In(a, r); var fb = Paths.In(b, r);
                if (Assemble.SameBytes(fa, fb)) { same++; continue; }
                string kind = "content";
                if (r.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase))
                    try
                    {
                        var da = Dcx.Decompress(File.ReadAllBytes(fa)); var db = Dcx.Decompress(File.ReadAllBytes(fb));
                        if (da.AsSpan().SequenceEqual(db)) kind = "dcx-bytes-only";
                    }
                    catch (Exception e) { kind = "content (dcx: " + e.Message + ")"; }
                if (kind == "dcx-bytes-only") bytesOnly++; else content++;
                Console.WriteLine($"{kind}\t{r}");
            }
            Console.WriteLine($"compare-build: {same} identical, {bytesOnly} differ only in DCX compression, {content} differ in content, {ra.Except(rb).Count() + rb.Except(ra).Count()} only on one side");
            return 0;
        }

    case "export-rulings":
        // export-rulings [ledger] [out]: the ledger's Ruling lines -> src/NRMerge/Resources/rulings.txt (embedded in MERGE_REPORT.md)
        return Report.ExportRulings(args.Length > 1 ? args[1] : Report.LedgerPath,
            args.Length > 2 ? args[2] : Path.Combine(BuildConfig.DevRepo, "src", "NRMerge", "Resources", "rulings.txt"));

    case "deliver":
        return Report.Deliver();

    case "report":
        File.WriteAllText(Path.Combine(Paths.Out, "MERGE_REPORT.md"), Report.Build(Report.LedgerPath));
        return 0;

    case "verify":
        return Verify.Run();

    case "profile":
        return Profile.Run();

    case "playercheck":
        return PlayerMerge.CheckOnly();

    case "playermerge":
        return PlayerMerge.Run();

    case "playerscripts":
        return PlayerScripts.Run();

    case "enemymerge":
        return EnemyMerge.Run();

    case "msbmerge":
        return MsbMerge.Run();

    case "emevdmerge":
        return EmevdMerge.Run();

    case "contentmerge":
        return ContentMerge.Run();

    case "mmvrewrite":
        return MmvRewrite.Run(args.Length > 1 && args[1] == "--dry");

    case "taefind":
        return TaeFind.Run(args[1], args[2]);

    case "roundtrip":
        return RoundTrip.Run(args.Skip(1).ToArray());

    case "refscan":
        return RefScan.Run(args[1], args[2], args[3]);

    case "fmgdump":
        return FmgDump.Run(args[1], args[2]);

    case "regmerge":
        return RegMerge.Run();

    case "reglayout":
        return RegLayout.Run(args[1], args[2]);

    case "assemble":
        return Assemble.Run();

    case "mapdiff3":
        return MapDiff3.Run(args[1], args[2]);

    case "bnddiff3":
        return BndDiff3.Run(args[1], args[2]);

    case "extract":
        return Extract.Run(args[1], args[2], args[3], args[4]);

    case "make-vanilla-manifest":
        {
            // make-vanilla-manifest [vanillaDir vanillaErDir [out.tsv]]  (dev: from the workspace's vanilla folders)
            var m = VanillaManifest.Create(args.Length > 2 ? args[1] : Paths.Vanilla, args.Length > 2 ? args[2] : Paths.VanillaER);
            var outPath = args.Length > 3 ? args[3] : VanillaManifest.DefaultPath;
            m.Save(outPath);
            Console.WriteLine($"{outPath}: {m.Entries.Count(e => e.Game == "NR" && !e.Absent)} NR, {m.Entries.Count(e => e.Game == "ER" && !e.Absent)} ER, {m.Entries.Count(e => e.Absent)} absent");
            return 0;
        }

    case "fetch":
        {
            // fetch [cacheDir]: downloads (or verifies in the cache) every pinned file of data\fetch.json
            var cfg = BuildConfig.Dev();
            if (args.Length > 1) cfg.CacheDir = Path.GetFullPath(args[1]);
            try
            {
                foreach (var id in Fetch.Catalog(cfg).Keys)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Console.WriteLine($"{id}: {Fetch.Get(id, cfg)} ({sw.Elapsed.TotalSeconds:F1}s)");
                }
            }
            catch (BuildException e) { Console.Error.WriteLine(e.Message); return 1; }
            return 0;
        }

    case "extract-vanilla":
        {
            // extract-vanilla [workDir]: extracts the manifest's files from the configured games into <workDir>\vanilla{,_er}
            var cfg = BuildConfig.Dev();
            if (args.Length > 1) cfg.WorkDir = Path.GetFullPath(args[1]);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                NRMerge.Oodle.Prepare(cfg);
                VanillaManifest.Extract(cfg, new ConsoleProgress());
            }
            catch (BuildException e) { Console.Error.WriteLine(e.Message); return 1; }
            var problems = VanillaManifest.Load(VanillaManifest.DefaultPath).Check(Path.Combine(cfg.WorkDir, "vanilla"), Path.Combine(cfg.WorkDir, "vanilla_er"));
            foreach (var p in problems) Console.WriteLine(p);
            Console.WriteLine($"extract-vanilla: {(problems.Count == 0 ? "all hashes match" : problems.Count + " problems")} in {sw.Elapsed.TotalSeconds:F1}s");
            return problems.Count == 0 ? 0 : 1;
        }

    case "make-mod-manifest":
        {
            // make-mod-manifest <evFolder> <mmvFolder> [out.json]: hashes both downloads (read-only) into data\mod-manifest.json
            if (args.Length < 3) { Console.WriteLine("usage: make-mod-manifest <evFolder> <mmvFolder> [out.json]"); return 1; }
            var outPath = args.Length > 3 ? args[3] : ModManifest.DefaultPath;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var m = new ModManifest { Created = date };
            var excluded = new Dictionary<string, List<string>>();
            try
            {
                foreach (var (info, folder) in new[] { (ModSpec.EvInfo(date), args[1]), (ModSpec.MmvInfo(), args[2]) })
                {
                    var ex = excluded[info.Id] = new List<string>();
                    var progress = new ConsoleHashProgress($"hashing {info.ShortName}:");
                    m.Mods.Add(ModManifest.Scan(info, folder, progress, ex));
                    progress.Done();
                }
            }
            catch (BuildException e) { Console.Error.WriteLine(e.Message); return 1; }
            File.WriteAllText(outPath, m.ToJson());
            foreach (var s in m.Mods)
            {
                Console.WriteLine($"{s.Id}: {s.Files.Count} files ({s.Files.Count(f => f.PresenceOnly)} presence-only), {s.Files.Sum(f => f.Size) / 1048576.0:0} MB");
                Console.WriteLine($"  excluded: {(excluded[s.Id].Count == 0 ? "none" : string.Join(", ", excluded[s.Id]))}");
            }
            Console.WriteLine($"{outPath} written in {sw.Elapsed.TotalSeconds:F1}s");
            return 0;
        }

    case "check-mods":
        {
            // check-mods [evFolder mmvFolder]: verifies the player's downloads against data\mod-manifest.json
            var folders = args.Length > 2 ? new[] { args[1], args[2] } : new[] { Paths.EV, Paths.MMV };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ModManifest m;
            try { m = ModManifest.Load(); }
            catch (BuildException e) { Console.Error.WriteLine(e.Message); return 1; }
            bool ok = true;
            foreach (var (id, folder) in new[] { ("ev", folders[0]), ("mmv", folders[1]) })
            {
                var spec = m.Get(id);
                Console.WriteLine($"Checking {spec.DisplayName}: {folder}");
                var progress = new ConsoleHashProgress("hashing");
                var r = m.Check(id, folder, progress);
                progress.Done();
                Console.WriteLine(r.FormatReport());
                ok &= r.Ok;
            }
            Console.WriteLine($"check-mods: {(ok ? "both mods OK" : "PROBLEMS found")} in {sw.Elapsed.TotalSeconds:F1}s");
            return ok ? 0 : 1;
        }

    case "airules-make":
        {
            // airules-make <script> [resolvedDir] [out.rules.json]: AI hand-resolution rules from <work>\ai\<script>\{base,ev,mmv}.norm.lua
            // and a directory of whole-function override files (<key>.lua, as the former .resolved dirs held).
            if (args.Length < 2) { Console.WriteLine("usage: airules-make <script> [resolvedDir] [out.rules.json]"); return 1; }
            var script = args[1].EndsWith(".lua") ? args[1][..^4] : args[1];
            var work = Path.Combine(Paths.Work, "ai", script);
            var resolvedDir = args.Length > 2 ? args[2] : Path.Combine(Paths.Rules, "ai", script + ".lua.resolved");
            var outPath = args.Length > 3 ? args[3] : Path.Combine(Paths.Rules, "ai", script + ".lua.rules.json");
            if (!Directory.Exists(resolvedDir)) { Console.WriteLine($"{resolvedDir}: not found"); return 1; }
            string Side(string side) => PyText.Read(Path.Combine(work, side + ".norm.lua"));
            var rules = AiRules.GenerateFromResolvedDir(Side("base"), Side("ev"), Side("mmv"), resolvedDir, out var log);
            foreach (var l in log) Console.WriteLine(l);
            rules.Save(outPath);
            Console.WriteLine($"{rules.Rules.Count} rule(s) -> {outPath}");
            return 0;
        }

    case "check-games":
        {
            // check-games [nrGame erGame]: Nightreign exe 1.3.3.0 + regulation 10350000, Elden Ring regulation 11611000
            var cfg = BuildConfig.Dev();
            if (args.Length > 2) { cfg.NrGame = Path.GetFullPath(args[1]); cfg.ErGame = Path.GetFullPath(args[2]); }
            foreach (var (name, dir, exe, game) in new[] { ("Nightreign", cfg.NrGame, "nightreign.exe", "NR"), ("Elden Ring", cfg.ErGame, "eldenring.exe", "ER") })
            {
                string exeVer, regVer;
                try { exeVer = GameCheck.ReadExeVersion(Path.Combine(dir, exe)) ?? "unknown"; } catch (Exception e) { exeVer = "error: " + e.Message; }
                try { regVer = GameCheck.ReadRegulationVersion(Path.Combine(dir, "regulation.bin"), game); } catch (Exception e) { regVer = "error: " + e.Message; }
                Console.WriteLine($"{name}: {dir}\n  exe {exeVer}, regulation {regVer} ({GameCheck.RegulationDisplay(regVer)})");
            }
            var problems = GameCheck.Check(cfg);
            foreach (var p in problems) Console.WriteLine(p);
            Console.WriteLine(problems.Count == 0 ? "check-games: games OK" : $"check-games: {problems.Count} problem(s)");
            return problems.Count == 0 ? 0 : 1;
        }

    default:
        Console.WriteLine("unknown command");
        return 1;
}
