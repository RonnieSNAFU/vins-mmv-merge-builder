using SoulsFormats;
using System.Text;

namespace NRMerge;

/// <summary>
/// Stage (Task 11): enemy binders (TAE, behavior and FLVER entries merged inside), AI script binders (function-level merge of
/// normalised Lua, global-name lists united) and sound banks (EV's).
/// </summary>
public static class EnemyMerge
{
    /// <summary>AI hand resolutions: merge/ai/&lt;script&gt;.lua.rules.json (see <see cref="AiRules"/>).</summary>
    static string RulesDir => Path.Combine(Paths.Rules, "ai");

    /// <summary>Journal area for entry merges (the player stage reuses <see cref="MergeEntry"/>).</summary>
    public static string Area = "enemies";

    /// <summary>Entry-merge used for enemy binders.</summary>
    public static byte[] MergeEntry(string rel, string key, byte[] b, byte[] e, byte[] m)
    {
        if (key.EndsWith(".tae"))
        {
            var fallback = ErTae(rel, key);
            var bt = b != null ? TAE.Read(b) : fallback ?? EmptyLike(TAE.Read(e));
            var r = TaeMerge.Merge(bt, TAE.Read(e), TAE.Read(m), out var conflicts, fallback);
            foreach (var c in conflicts) Journal.Add(Area, $"{rel} :: {key}", c);
            Journal.Add(Area, $"{rel} :: {key}", $"TAE merged per animation ({conflicts.Count} conflict(s) kept EV)");
            var bytes = r.Write();
            TAE.Read(bytes);
            return bytes;
        }
        if (b == null) return null;   // added by both without a base: owner rule
        if (key.EndsWith(".hkx") && key.Contains(@"\behaviors\"))
        {
            var bytes = BehaviorMerge.Merge(b, e, m, out var notes);
            foreach (var n in notes) Journal.Add(Area, $"{rel} :: {key}", n);
            return bytes;
        }
        if (key.EndsWith(".flver"))
        {
            var bytes = FlverMerge.Merge(b, e, m, out var notes);
            Journal.Add(Area, $"{rel} :: {key}", bytes == null ? "FLVER: " + string.Join("; ", notes) + " -> EV" : "FLVER sections: " + string.Join("; ", notes));
            return bytes;
        }
        return null;
    }

    static TAE EmptyLike(TAE t) => new() { Format = t.Format, ID = t.ID, Flags = t.Flags, SkeletonName = t.SkeletonName, SibName = t.SibName, EventBank = t.EventBank, BigEndian = t.BigEndian, Animations = new() };

    /// <summary>The Elden Ring original of a TAE (fallback base for animations missing from a Nightreign base).</summary>
    public static TAE ErTae(string rel, string key)
    {
        var er = Paths.ErOriginal(rel, "TAE: no Elden Ring fallback base for animations missing from the Nightreign base (merged without base, EV kept on conflicts)");
        if (er == null || !File.Exists(er) || Paths.BaseOf(rel) == er) return null;
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(er)));
        var f = bnd.Files.FirstOrDefault(x => BehProbe.EntryKey(x.Name) == key || BndDiff3.Norm(x.Name) == BndDiff3.Norm(key));
        return f == null ? null : TAE.Read(f.Bytes.ToArray());
    }

    /// <summary>Lua 5.0 AI script: all three sides normalised by compile+decompile, merged per function, compile-checked.</summary>
    public static byte[] MergeLua(string rel, string key, byte[] b, byte[] e, byte[] m)
    {
        if (b == null)
        {
            // Only without Elden Ring (the verified build has a base for every AI script both mods changed): owner rule, EV's script.
            Journal.Add("ai", $"{rel} :: {key}", "no base script: EV's script kept (owner rule)");
            ErFallback.Note($"{rel} :: {key}", "no Elden Ring base: AI script not merged per function, EV's script kept");
            return null;
        }
        var name = Path.GetFileNameWithoutExtension(key);
        var dir = Path.Combine(Paths.Work, "ai", name);
        Directory.CreateDirectory(dir);
        foreach (var (side, data) in new[] { ("base", b), ("ev", e), ("mmv", m) })
        {
            File.WriteAllBytes(Path.Combine(dir, side + ".lua"), data);
            File.WriteAllText(Path.Combine(dir, side + ".norm.lua"), LuaNorm.Norm(data), new UTF8Encoding(false));
        }
        var outp = Path.Combine(dir, "merged.lua");
        var rules = AiRules.Load(Path.Combine(RulesDir, name + ".lua.rules.json"));
        string Norm(string side) => PyText.Read(Path.Combine(dir, side + ".norm.lua"));
        var text = LuaFuncMerge.Merge(Norm("base"), Norm("ev"), Norm("mmv"), rules, out var report, out var unresolved);
        PyText.Write(outp, text);
        PyText.Write(outp + ".report.txt", LuaFuncMerge.ReportText(report));
        // A rule for a function that no longer conflicts is unused: console warning only (journal unchanged).
        foreach (var k in rules.Rules.Keys.Where(k => !report.Any(r => r == "RESOLVED " + k || r.StartsWith("CONFLICT " + k + " ("))))
            Console.WriteLine($"warning: {rel} :: {key}: hand resolution for {k} not used (the function no longer conflicts)");
        if (unresolved.Count > 0)
        {
            var msg = $"{rel} :: {key}: {unresolved.Count} unresolved AI function conflict(s): " +
                string.Join("; ", report.Where(r => r.StartsWith("CONFLICT ")));
            if (!Paths.Config.Force) throw new BuildException(msg);
            Console.WriteLine("warning (--force): " + msg);
        }
        try { LuaNorm.Compile(File.ReadAllBytes(outp)); }
        catch (Exception ex) { throw new BuildException($"{rel} :: {key}: merged AI script does not compile: {ex.Message}"); }
        foreach (var line in report.Where(l => l.Length > 0))
            Journal.Add("ai", $"{rel} :: {key}", line);
        Journal.Add("ai", $"{rel} :: {key}", "function-level merge of normalised scripts; compiles");
        return File.ReadAllBytes(outp);
    }

    /// <summary>Union of global names, EV's order first.</summary>
    public static byte[] MergeGnl(byte[] e, byte[] m)
    {
        var ge = LUAGNL.Read(e); var gm = LUAGNL.Read(m);
        foreach (var g in gm.Globals) if (!ge.Globals.Contains(g)) ge.Globals.Add(g);
        return ge.Write();
    }

    /// <summary>
    /// Enemy clips play animation aXXX_YYYYYY = TAE animation XXX*1000000+YYYYYY. Animations a merged clip plays that the TAE merge
    /// removed are copied back from <paramref name="donor"/> (same TAE file). Returns "file id" for each restored animation.
    /// </summary>
    public static List<string> RestoreClipAnimations(Dictionary<string, TAE> merged, Dictionary<string, TAE> donor, IEnumerable<string> clipNames)
    {
        var restored = new List<string>();
        foreach (var n in clipNames.Where(n => n != null && n.Length >= 11).Distinct())
        {
            if (!int.TryParse(n.AsSpan(1, 3), out var cat) || !long.TryParse(n.AsSpan(5), out var id)) continue;
            long full = cat * 1000000L + id;
            if (merged.Values.Any(t => t.Animations.Any(a => a.ID == full))) continue;
            foreach (var (key, dt) in donor)
            {
                var src = dt.Animations.FirstOrDefault(a => a.ID == full);
                if (src == null || !merged.TryGetValue(key, out var mt)) continue;
                mt.Animations.Add(src);
                mt.Animations = mt.Animations.OrderBy(a => a.ID).ToList();
                restored.Add($"{key} {full}");
                break;
            }
        }
        return restored;
    }

    static Dictionary<string, TAE> TaesOf(BND4 bnd) =>
        bnd.Files.Where(f => f.Name.EndsWith(".tae")).GroupBy(f => BndDiff3.Norm(f.Name)).ToDictionary(g => g.Key, g => TAE.Read(g.First().Bytes.ToArray()));

    /// <summary>Post-pass over merged enemy anibnds: restore animations merged behavior clips play (MMV's TAE first, then the base).</summary>
    static void RestoreEnemyAnimations(IEnumerable<string> mergedRels)
    {
        foreach (var rel in mergedRels.Where(r => r.EndsWith(".anibnd.dcx")))
        {
            var behRel = rel.Replace(".anibnd.dcx", ".behbnd.dcx");
            var behPath = File.Exists(Paths.In(Paths.OutMod, behRel)) ? Paths.In(Paths.OutMod, behRel) : Paths.In(Paths.EV, behRel);
            if (!File.Exists(behPath)) continue;
            var clips = BehProbe.Entries(behPath).Where(k => k.Key.Contains(@"\behaviors\")).SelectMany(k =>
                BehGraph.AllObjects(BehGraph.Read(k.Value)).OfType<HKLib.hk2018.hkbClipGenerator>().Select(c => c.m_animationName)).ToList();
            var outPath = Paths.In(Paths.OutMod, rel);
            var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(outPath), out var type));
            var merged = TaesOf(bnd);
            var restored = RestoreClipAnimations(merged, TaesOf(BND4.Read(Dcx.Decompress(File.ReadAllBytes(MmvRewrite.MmvInput(rel))))), clips);
            var basePath = Paths.BaseOf(rel);
            if (basePath != null) restored.AddRange(RestoreClipAnimations(merged, TaesOf(BND4.Read(Dcx.Decompress(File.ReadAllBytes(basePath)))), clips));
            if (restored.Count == 0) continue;
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae"))) f.Bytes = merged[BndDiff3.Norm(f.Name)].Write();
            File.WriteAllBytes(outPath, bnd.Write(type).ToArray());
            foreach (var r in restored) Journal.Add("enemies", $"{rel} :: {r}", "restored (removed by the TAE merge but played by a merged behavior clip)");
            Console.WriteLine($"{rel}: restored {restored.Count} animation(s)");
        }
    }

    static bool IsEnemyChr(string rel) =>
        rel.StartsWith("chr/c") && !rel.StartsWith("chr/c0000") && (rel.EndsWith(".anibnd.dcx") || rel.EndsWith(".behbnd.dcx") || rel.EndsWith(".chrbnd.dcx"));

    public static int Run()
    {
        Journal.Clear();
        foreach (var line in File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1))
        {
            var p = line.Split('\t');
            if (p[0] != "merge") continue;
            var rel = p[1];
            var outPath = Paths.In(Paths.OutMod, rel);
            if (IsEnemyChr(rel))
            {
                var r = BinderMerge.Merge(Paths.BaseOf(rel), Paths.In(Paths.EV, rel), MmvRewrite.MmvInput(rel), outPath, _ => Side.EV, rel,
                    (key, b, e, m) => MergeEntry(rel, key, b, e, m), fullPathKeys: true, area: "binders-enemies");
                Console.WriteLine($"{rel}: {r}");
            }
            else if (rel.StartsWith("script/") && rel.EndsWith(".luabnd.dcx"))
            {
                var r = BinderMerge.Merge(Paths.BaseOf(rel), Paths.In(Paths.EV, rel), MmvRewrite.MmvInput(rel), outPath, _ => Side.EV, rel,
                    (key, b, e, m) => key.EndsWith(".lua") ? MergeLua(rel, key, b, e, m) : key.EndsWith(".luagnl") ? MergeGnl(e, m) : null, area: "binders-enemies");
                Console.WriteLine($"{rel}: {r}");
            }
            else if (rel.StartsWith("sd/") && rel.EndsWith(".bnk"))
            {
                File.Copy(Paths.In(Paths.EV, rel), outPath, true);
                Journal.Add("enemies", rel, "sound bank: EV's (same media and object count as MMV's; EV's event data differs by ~130 bytes)");
                Console.WriteLine($"{rel}: EV");
            }
        }
        RestoreEnemyAnimations(File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1).Select(l => l.Split('\t'))
            .Where(p => p[0] == "merge" && IsEnemyChr(p[1])).Select(p => p[1]));
        Journal.Save();
        return 0;
    }
}
