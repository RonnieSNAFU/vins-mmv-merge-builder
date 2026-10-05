using Andre.Formats;
using SoulsFormats;
using System.Text.Json;

namespace NRMerge;

/// <summary>
/// Stage (Task 13): player animations (TAE merge, HKX union with renumbered clashes), behavior graph, model, and the
/// BehaviorParam_PC judge collisions (see <see cref="JudgeRemap"/>).
/// </summary>
public static class PlayerMerge
{
    const string Anibnd = "chr/c0000.anibnd.dcx", A6x = "chr/c0000_a6x.anibnd.dcx", Behbnd = "chr/c0000.behbnd.dcx", Chrbnd = "chr/c0000.chrbnd.dcx";

    /// <summary>
    /// Animations both mods added with different HKX that both use: EV keeps the file name; MMV's HKX moves to the new ID and
    /// MMV's TAE entry imports it (behavior clips keep the animation name, the TAE entry decides the HKX).
    /// </summary>
    public static readonly Dictionary<string, string> HkxRenames = new()
    {
        ["a861_040000"] = "a861_040900",
        ["a879_040053"] = "a879_040953",
    };

    static Dictionary<string, TAE> Taes(string path)
    {
        var d = new Dictionary<string, TAE>();
        if (path == null || !File.Exists(path)) return d;
        foreach (var f in BND4.Read(Dcx.Decompress(File.ReadAllBytes(path))).Files.Where(f => f.Name.EndsWith(".tae")))
            d[BndDiff3.Norm(f.Name)] = TAE.Read(f.Bytes.ToArray());
        return d;
    }

    static IEnumerable<int> PendingJudges()
    {
        var p = Path.Combine(Journal.Dir, "regulation-pending.tsv");
        return File.ReadAllLines(p).Skip(1).Where(l => l.StartsWith("BehaviorParam_PC "))
            .Select(l => (int)(long.Parse(l.Split('\t')[0].Split(' ')[1]) % 1000));
    }

    static HashSet<long> PendingIds() =>
        File.ReadAllLines(Path.Combine(Journal.Dir, "regulation-pending.tsv")).Skip(1).Where(l => l.StartsWith("BehaviorParam_PC "))
            .Select(l => long.Parse(l.Split('\t')[0].Split(' ')[1])).ToHashSet();

    /// <summary>Copies every BehaviorParam_PC row with a remapped judge to the new judge (pending variation-0 rows from MMV).</summary>
    static void PatchRegulation(Dictionary<int, int> map)
    {
        var regPath = Paths.In(Paths.OutMod, "regulation.bin");
        var reg = Regulation.Load(regPath);
        var mmv = Regulation.Load(Path.Combine(Paths.MMV, "regulation.bin"));
        var bp = reg.Params["BehaviorParam_PC"]; var mbp = mmv.Params["BehaviorParam_PC"];
        var judge = bp["behaviorJudgeId"]; var refType = bp["refType"]; var refId = bp["refId"];
        var pending = PendingIds();
        var remaps = RemapTable.Load();
        var ids = bp.Rows.Select(r => (long)r.ID).ToHashSet();
        var add = new List<Param.Row>();
        int copied = 0, fromMmv = 0;
        foreach (var r in bp.Rows.ToList())
        {
            int j = Convert.ToInt32(judge.GetValue(r));
            if (!map.TryGetValue(j, out var nj)) continue;
            long nid = r.ID - r.ID % 1000 + nj;
            while (ids.Contains(nid)) nid += 1000000;   // keep the judge digits, step past occupied slots
            var src = r;
            if (pending.Contains(r.ID))
            {
                src = mbp.Rows.First(x => x.ID == r.ID);
                fromMmv++;
            }
            var nr = new Param.Row(src, bp) { ID = (int)nid };
            judge.SetValue(nr, Convert.ChangeType(nj, judge.ValueType));
            if (src != r)
            {
                var param = Convert.ToInt32(refType.GetValue(nr)) switch { 0 => "AtkParam_Pc", 1 => "Bullet", 2 => "SpEffectParam", _ => null };
                if (param != null && remaps.TryMap(param, Convert.ToInt64(refId.GetValue(nr)), out var moved))
                    refId.SetValue(nr, Convert.ChangeType(moved, refId.ValueType));
            }
            ids.Add(nid);
            add.Add(nr);
            copied++;
        }
        bp.Rows = bp.Rows.Concat(add).OrderBy(r => r.ID).ToList();
        Regulation.Save(reg, regPath);
        File.WriteAllText(Path.Combine(Paths.Out, "regulation.player.sha256"), Sha(regPath));
        Journal.Add("player", "regulation BehaviorParam_PC", $"{copied} row(s) copied to remapped judges ({fromMmv} variation-0 fallback rows from MMV)");
    }

    static string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Re-runs start from the regulation the earlier stages produced, not from this stage's own output.</summary>
    static void RestoreRegulation()
    {
        var reg = Paths.In(Paths.OutMod, "regulation.bin");
        var snap = Path.Combine(Paths.Out, "regulation.pre-player.bin");
        var mark = Path.Combine(Paths.Out, "regulation.player.sha256");
        if (File.Exists(mark) && File.Exists(snap) && File.ReadAllText(mark) == Sha(reg)) File.Copy(snap, reg, true);
        else File.Copy(reg, snap, true);
    }

    public static int Run()
    {
        Journal.Clear();
        var work = Path.Combine(Paths.Work, "player");
        Directory.CreateDirectory(work);
        RestoreRegulation();
        EnemyMerge.Area = "player";

        // 1. Behavior-judge collisions: MMV's added events move to free judges.
        var tb = Taes(Paths.BaseOf(Anibnd)); var te = Taes(Paths.In(Paths.EV, Anibnd)); var tm = Taes(MmvRewrite.MmvInput(Anibnd));
        var ter = Taes(Paths.ErOriginal(Anibnd, "player TAE: no Elden Ring fallback base for the behavior-judge remap (animations missing from the Nightreign base compared without base)"));
        TAE BaseFor(string k) => tb.GetValueOrDefault(k) ?? ter.GetValueOrDefault(k);
        var pendingJ = PendingJudges().ToHashSet();
        var needed = tm.SelectMany(x => JudgeRemap.MmvAddedJudges(BaseFor(x.Key), te.GetValueOrDefault(x.Key), x.Value)).Where(pendingJ.Contains).Distinct().ToList();
        var reg = Regulation.Load(Paths.In(Paths.OutMod, "regulation.bin"));
        var jcol = reg.Params["BehaviorParam_PC"]["behaviorJudgeId"];
        var used = reg.Params["BehaviorParam_PC"].Rows.Select(r => Convert.ToInt32(jcol.GetValue(r))).ToHashSet();
        var map = JudgeRemap.Assign(needed, used);
        File.WriteAllText(Path.Combine(Paths.Out, "judge_remap.json"), JsonSerializer.Serialize(map.OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString(), x => x.Value), new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (j, nj) in map.OrderBy(x => x.Key)) Journal.Add("player", $"behavior judge {j}", $"MMV-added events -> judge {nj}");
        foreach (var j in pendingJ.Except(needed).OrderBy(x => x))
            Journal.Add("player", $"behavior judge {j}", "MMV's variation-0 row is used by no MMV-added event: EV's row kept");
        PatchRegulation(map);

        // MMV's anibnd with remapped judges and the HKX-clash imports, used as the MMV input of the TAE merge.
        var mmvBndPath = MmvRewrite.MmvInput(Anibnd);
        var mmvBnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(mmvBndPath), out var aniType));
        int rewritten = 0;
        foreach (var f in mmvBnd.Files.Where(f => f.Name.EndsWith(".tae")))
        {
            var k = BndDiff3.Norm(f.Name);
            var t = JudgeRemap.RewriteMmv(BaseFor(k), te.GetValueOrDefault(k), tm[k], map, out var n);
            if (n == 0) continue;
            rewritten += n;
            f.Bytes = t.Write();
        }
        Journal.Add("player", Anibnd, $"{rewritten} MMV-added behavior event(s) moved to remapped judges");
        var mmvAni = Path.Combine(work, "mmv_c0000.anibnd.dcx");
        File.WriteAllBytes(mmvAni, mmvBnd.Write(aniType).ToArray());

        // 2. TAE merge.
        var r1 = BinderMerge.Merge(Paths.BaseOf(Anibnd), Paths.In(Paths.EV, Anibnd), mmvAni, Paths.In(Paths.OutMod, Anibnd), _ => Side.EV, Anibnd,
            (key, b, e, m) => EnemyMerge.MergeEntry(Anibnd, key, b, e, m), fullPathKeys: true, area: "binders-player");
        Console.WriteLine($"{Anibnd}: {r1}");
        PointMmvAnimsAtRenamedHkx();

        // 3. Animation HKX union: clashes go to the side whose TAE/behavior uses them; both-used ones keep EV's name.
        var evUses = HkxRenames.Keys.Select(k => k + ".hkx").ToHashSet();
        var r2 = BinderMerge.Merge(Paths.BaseOf(A6x), Paths.In(Paths.EV, A6x), MmvRewrite.MmvInput(A6x), Paths.In(Paths.OutMod, A6x),
            key => evUses.Contains(BndDiff3.Norm(key)) ? Side.EV : Side.MMV, A6x, area: "binders-player");
        AddRenamedHkx();
        Console.WriteLine($"{A6x}: {r2}");

        // 4. Behavior graph and model.
        var r3 = BinderMerge.Merge(Paths.BaseOf(Behbnd), Paths.In(Paths.EV, Behbnd), MmvRewrite.MmvInput(Behbnd), Paths.In(Paths.OutMod, Behbnd), _ => Side.EV, Behbnd,
            (key, b, e, m) => EnemyMerge.MergeEntry(Behbnd, key, b, e, m), fullPathKeys: true, area: "binders-player");
        Console.WriteLine($"{Behbnd}: {r3}");
        var r4 = BinderMerge.Merge(Paths.BaseOf(Chrbnd), Paths.In(Paths.EV, Chrbnd), MmvRewrite.MmvInput(Chrbnd), Paths.In(Paths.OutMod, Chrbnd), _ => Side.EV, Chrbnd,
            (key, b, e, m) => EnemyMerge.MergeEntry(Chrbnd, key, b, e, m), fullPathKeys: true, area: "binders-player");
        Console.WriteLine($"{Chrbnd}: {r4}");

        RestoreReferencedAnimations();
        Journal.Save();
        return Check();
    }

    public static int CheckOnly()
    {
        return Check();
    }

    /// <summary>
    /// TAE animations that a merged behavior clip plays but the TAE merge removed (one side deleted a base animation the
    /// other side's new clips use) are restored from MMV's TAE, else the base.
    /// </summary>
    static void RestoreReferencedAnimations()
    {
        var path = Paths.In(Paths.OutMod, Anibnd);
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(path), out var type));
        var taes = bnd.Files.Where(f => f.Name.EndsWith(".tae")).ToDictionary(f => BndDiff3.Norm(f.Name), f => (file: f, tae: TAE.Read(f.Bytes.ToArray())));
        var mmv = Taes(Path.Combine(Paths.Work, "player", "mmv_c0000.anibnd.dcx")); var bas = Taes(Paths.BaseOf(Anibnd));
        var beh = BehProbe.Entries(Paths.In(Paths.OutMod, Behbnd)).First(k => k.Key.Contains(@"\behaviors\")).Value;
        var names = BehGraph.AllObjects(BehGraph.Read(beh)).OfType<HKLib.hk2018.hkbClipGenerator>().Select(c => c.m_animationName).Where(n => n != null && n.Length >= 11).Distinct();
        var changed = new HashSet<string>();
        foreach (var n in names)
        {
            if (!int.TryParse(n.AsSpan(1, 3), out var cat) || !long.TryParse(n.AsSpan(5), out var id)) continue;
            var key = new[] { $"a{cat:000}.tae", $"a{cat:00}.tae", $"a{cat}.tae" }.FirstOrDefault(taes.ContainsKey);
            if (key == null || taes[key].tae.Animations.Any(a => a.ID == id)) continue;
            var src = mmv.GetValueOrDefault(key)?.Animations.FirstOrDefault(a => a.ID == id) ?? bas.GetValueOrDefault(key)?.Animations.FirstOrDefault(a => a.ID == id);
            if (src == null) continue;
            var t = taes[key].tae;
            t.Animations.Add(src);
            t.Animations = t.Animations.OrderBy(a => a.ID).ToList();
            changed.Add(key);
            Journal.Add("player", $"{Anibnd} :: {key} {id}", "restored (removed by the TAE merge but played by a merged behavior clip)");
        }
        foreach (var k in changed) taes[k].file.Bytes = taes[k].tae.Write();
        if (changed.Count > 0) File.WriteAllBytes(path, bnd.Write(type).ToArray());
    }

    /// <summary>MMV's TAE entries for the renamed clashes import MMV's HKX under its new ID.</summary>
    static void PointMmvAnimsAtRenamedHkx()
    {
        var path = Paths.In(Paths.OutMod, Anibnd);
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(path), out var type));
        foreach (var (oldName, newName) in HkxRenames)
        {
            var tae = $"a{oldName[1..4]}.tae"; long id = long.Parse(oldName[5..]);
            var f = bnd.Files.First(x => BndDiff3.Norm(x.Name) == tae);
            var t = TAE.Read(f.Bytes.ToArray());
            var a = t.Animations.First(x => x.ID == id);
            long src = long.Parse(newName[1..4]) * 1000000L + long.Parse(newName[5..]);
            a.MiniHeader = new TAE.Animation.AnimMiniHeader.Standard { ImportsHKX = true, ImportHKXSourceAnimID = (int)src };
            f.Bytes = t.Write();
            Journal.Add("player", $"{Anibnd} :: {tae} {id}", $"imports MMV's HKX renamed to {newName} (EV keeps {oldName}.hkx for its own imports)");
        }
        File.WriteAllBytes(path, bnd.Write(type).ToArray());
    }

    static void AddRenamedHkx()
    {
        var path = Paths.In(Paths.OutMod, A6x);
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(path), out var type));
        var mmv = BND4.Read(Dcx.Decompress(File.ReadAllBytes(MmvRewrite.MmvInput(A6x))));
        int nextId = bnd.Files.Max(f => f.ID) + 1;
        foreach (var (oldName, newName) in HkxRenames)
        {
            var src = mmv.Files.First(f => BndDiff3.Norm(f.Name) == oldName + ".hkx");
            if (bnd.Files.Any(f => BndDiff3.Norm(f.Name) == newName + ".hkx")) throw new InvalidDataException($"{newName}.hkx already exists");
            var name = src.Name[..^(oldName.Length + 4)] + newName + ".hkx";
            bnd.Files.Add(new BinderFile(src.Flags, nextId++, name, src.Bytes.ToArray()) { CompressionType = src.CompressionType });
            Journal.Add("player", $"{A6x} :: {oldName}.hkx", $"both added and both use it: EV keeps the name, MMV's copy added as {newName}.hkx");
        }
        File.WriteAllBytes(path, bnd.Write(type).ToArray());
    }

    /// <summary>Clip animations without a TAE entry or HKX, for one mod root (merged output or a source mod) over vanilla.</summary>
    public static HashSet<string> Missing(string root)
    {
        var hkx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { root, Paths.VanillaNR })
            foreach (var f in Directory.GetFiles(Path.Combine(dir, "chr"), "c0000*.anibnd.dcx"))
                if (dir == Paths.VanillaNR && File.Exists(Path.Combine(root, "chr", Path.GetFileName(f)))) continue;
                else foreach (var e in BND4.Read(Dcx.Decompress(File.ReadAllBytes(f))).Files.Where(x => x.Name.EndsWith(".hkx")))
                    hkx.Add(Path.GetFileNameWithoutExtension(BndDiff3.Norm(e.Name)));
        var taes = Taes(Path.Combine(root, "chr", "c0000.anibnd.dcx"));
        var beh = BehProbe.Entries(Path.Combine(root, "chr", "c0000.behbnd.dcx")).First(k => k.Key.Contains(@"\behaviors\")).Value;
        var clips = BehGraph.AllObjects(BehGraph.Read(beh)).OfType<HKLib.hk2018.hkbClipGenerator>().Select(c => c.m_animationName).Distinct().ToList();
        var missing = new HashSet<string>();
        foreach (var n in clips)
        {
            if (n == null || n.Length < 11 || !int.TryParse(n.AsSpan(1, 3), out var cat) || !long.TryParse(n.AsSpan(5), out var id)) continue;
            var t = taes.GetValueOrDefault($"a{cat:000}.tae") ?? taes.GetValueOrDefault($"a{cat:00}.tae") ?? taes.GetValueOrDefault($"a{cat}.tae");
            var a = t?.Animations.FirstOrDefault(x => x.ID == id);
            if (a == null) { missing.Add(n + " (no TAE entry)"); continue; }
            long src = a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard { ImportsHKX: true } s ? s.ImportHKXSourceAnimID
                     : a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io ? io.ImportFromAnimID : cat * 1000000L + id;
            var hk = $"a{src / 1000000:000}_{src % 1000000:000000}";
            if (!hkx.Contains(hk)) missing.Add($"{n} -> {hk} (no HKX)");
        }
        return missing;
    }

    /// <summary>Behavior/animation consistency: problems in the merged output that neither source mod has.</summary>
    static int Check()
    {
        var merged = Missing(Paths.OutMod); var ev = Missing(Paths.EV); var mmv = Missing(Paths.MMV);
        var introduced = merged.Where(x => !ev.Contains(x) && !mmv.Contains(x)).OrderBy(x => x).ToList();
        Console.WriteLine($"check: unresolved clip animations merged={merged.Count} (EV alone {ev.Count}, MMV alone {mmv.Count}); introduced by the merge: {introduced.Count}");
        foreach (var m in introduced.Take(30)) Console.WriteLine("  " + m);
        File.AppendAllText(Path.Combine(Journal.Dir, "player.tsv"), $"check\tmerged {merged.Count}, EV {ev.Count}, MMV {mmv.Count}, introduced {introduced.Count}: {string.Join(", ", introduced.Take(50))}\n");
        return introduced.Count == 0 ? 0 : 2;
    }
}
