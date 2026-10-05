using SoulsFormats;

namespace NRMerge;

/// <summary>Stage (Task 7): per-event three-way merge of EMEVD; events both mods changed get an instruction-level merge.</summary>
public static class EmevdMerge
{
    sealed class Unit
    {
        public EMEVD.Instruction Ins;
        public List<EMEVD.Parameter> Params = new();
        public string Key;
    }

    static string InsKey(EMEVD.Instruction i) => $"{i.Bank}:{i.ID}:{i.Layer?.ToString() ?? "-"}:{Convert.ToHexString(i.ArgData)}";

    static List<Unit> Units(EMEVD.Event e)
    {
        if (e == null) return new List<Unit>();
        var units = e.Instructions.Select(i => new Unit { Ins = i }).ToList();
        foreach (var p in e.Parameters)
            if (p.InstructionIndex >= 0 && p.InstructionIndex < units.Count) units[(int)p.InstructionIndex].Params.Add(p);
        foreach (var u in units)
            u.Key = InsKey(u.Ins) + "|" + string.Join(";", u.Params.Select(p => $"{p.TargetStartByte},{p.SourceStartByte},{p.ByteCount}"));
        return units;
    }

    public static List<string> LastConflictDetails = new();

    static Emedf _emedf;
    static Emedf Defs => _emedf ??= Emedf.Load(MmvRewrite.EmedfPath);

    static bool IsSkip(EMEVD.Instruction i) =>
        Defs.Instrs.TryGetValue((i.Bank, i.ID), out var d) && d.Args.Count > 0 && d.Args[0].Size == 1 && d.Args[0].Offset == 0
        && d.Args[0].Name.Contains("Skipped", StringComparison.OrdinalIgnoreCase) && i.ArgData.Length > 0;

    /// <summary>
    /// Re-counts SKIP instructions so each covers the surviving lines it skipped. Every side's view of the skip is
    /// evaluated; among views whose skipped lines are all still present, the smallest covering span wins.
    /// </summary>
    static void FixSkips(List<Unit> merged, params List<Unit>[] sides)
    {
        var outPos = new Dictionary<Unit, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < merged.Count; i++) outPos[merged[i]] = i;
        for (int p = 0; p < merged.Count; p++)
        {
            var u = merged[p];
            if (!IsSkip(u.Ins)) continue;
            int best = int.MaxValue, owned = -1;
            foreach (var side in sides)
            {
                int i = side.FindIndex(x => ReferenceEquals(x, u));
                bool own = i >= 0;
                if (i < 0)
                {
                    double rel = merged.Count == 0 ? 0 : (double)p * side.Count / merged.Count;
                    i = Enumerable.Range(0, side.Count).Where(k => side[k].Key == u.Key)
                        .OrderBy(k => Math.Abs(k - rel)).DefaultIfEmpty(-1).First();
                    if (i < 0) continue;
                }
                int n = side[i].Ins.ArgData[0], maxPos = p, last = p;
                bool all = true;
                var claimed = new HashSet<int>();
                for (int t = i + 1; t <= Math.Min(i + n, side.Count - 1); t++)
                {
                    int q = outPos.TryGetValue(side[t], out var qq) && qq > last ? qq : -1;
                    if (q < 0)
                        for (int k = last + 1; k < merged.Count; k++)
                            if (merged[k].Key == side[t].Key && !claimed.Contains(k)) { q = k; break; }
                    if (q < 0) { all = false; continue; }
                    claimed.Add(q); last = q; maxPos = Math.Max(maxPos, q);
                }
                int nn = maxPos - p;
                if (all) best = Math.Min(best, nn);
                if (own) owned = nn;
            }
            int target = best != int.MaxValue ? best : owned;
            if (target < 0 || target == u.Ins.ArgData[0]) continue;
            var data = (byte[])u.Ins.ArgData.Clone();
            int before = data[0];
            data[0] = (byte)target;
            u.Ins = new EMEVD.Instruction(u.Ins.Bank, u.Ins.ID, data) { Layer = u.Ins.Layer };
            Journal.Add("events", $"skip fix {u.Ins.Bank}[{u.Ins.ID}] at line {p}", $"line count {before} -> {target}");
        }
    }

    /// <summary>True when every line <paramref name="x"/> added or removed (vs base) is equally added/removed in <paramref name="y"/>.</summary>
    static bool Subsumed(List<Unit> bas, List<Unit> x, List<Unit> y)
    {
        var kb = bas.Select(u => u.Key).ToHashSet(); var kx = x.Select(u => u.Key).ToHashSet(); var ky = y.Select(u => u.Key).ToHashSet();
        return kx.Where(k => !kb.Contains(k)).All(ky.Contains) && kb.Where(k => !kx.Contains(k)).All(k => !ky.Contains(k));
    }

    /// <summary>A conflicting hunk where one side's edits are all contained in the other side's hunk takes the other side's hunk.</summary>
    static List<Unit> ResolveHunk(Seq3.Conflict<Unit> c)
    {
        var kb = c.Base.Select(u => u.Key).ToHashSet(); var ka = c.A.Select(u => u.Key).ToHashSet(); var kc = c.B.Select(u => u.Key).ToHashSet();
        bool aInB = ka.Where(k => !kb.Contains(k)).All(kc.Contains) && kb.Where(k => !ka.Contains(k)).All(k => !kc.Contains(k));
        if (aInB) return c.B.ToList();
        bool bInA = kc.Where(k => !kb.Contains(k)).All(ka.Contains) && kb.Where(k => !kc.Contains(k)).All(k => !ka.Contains(k));
        return bInA ? c.A.ToList() : null;
    }

    static EMEVD.Event Copy(EMEVD.Event src, long id) =>
        new(id, src.RestBehavior) { Name = src.Name, Instructions = src.Instructions.ToList(), Parameters = src.Parameters.ToList() };

    public static EMEVD.Event MergeEvent(EMEVD.Event b, EMEVD.Event e, EMEVD.Event m, out int conflicts)
    {
        var ue = Units(e); var um = Units(m); var ub0 = Units(b);
        conflicts = 0;
        if (Subsumed(ub0, ue, um)) { LastConflictDetails = new() { "EV's changes contained in MMV's: took MMV's event" }; return Copy(m, e.ID); }
        if (Subsumed(ub0, um, ue)) { LastConflictDetails = new() { "MMV's changes contained in EV's: took EV's event" }; return Copy(e, e.ID); }
        var merged = Seq3.Merge(Units(b), ue, um, u => u.Key, out var cs, ResolveHunk);
        FixSkips(merged, ue, um);
        conflicts = cs.Count;
        LastConflictDetails = cs.Select(c =>
            $"base[{string.Join(" ", c.Base.Select(u => InsKey(u.Ins)))}] EV[{string.Join(" ", c.A.Select(u => InsKey(u.Ins)))}] MMV[{string.Join(" ", c.B.Select(u => InsKey(u.Ins)))}]").ToList();
        var r = new EMEVD.Event(e.ID, e.RestBehavior) { Name = e.Name };
        for (int i = 0; i < merged.Count; i++)
        {
            r.Instructions.Add(merged[i].Ins);
            foreach (var p in merged[i].Params) r.Parameters.Add(new EMEVD.Parameter(i, p.TargetStartByte, p.SourceStartByte, p.ByteCount));
        }
        return r;
    }

    /// <summary>
    /// Boss arenas where EV swapped the main boss and MMV added variant bosses (spec §9): before merging, MMV's lines
    /// for the main boss entity are aligned to EV's values (boss/BGM), leaving MMV's variant-entity lines untouched.
    /// </summary>
    static readonly Dictionary<string, (int Main, int[] ExtraVariants, (int From, int To)[] Subs)> Align = new()
    {
        ["event/m46_57_00_00.emevd.dcx"] = (46570800, Array.Empty<int>(), new[] { (905011000, 905820600), (920610, 950010) }),
        ["event/m46_69_00_00.emevd.dcx"] = (46690800, Array.Empty<int>(), new[] { (904690000, 905250200), (920910, 920210) }),
        ["event/m30_00_00_00.emevd.dcx"] = (30000800, new[] { 30005830 }, new[] { (904311000, 905840000) }),
    };

    static int AlignMainBoss(EMEVD mmv, string rel)
    {
        if (!Align.TryGetValue(rel, out var a)) return 0;
        int n = 0;
        foreach (var ev in mmv.Events.Where(x => x.ID == 0))
            for (int i = 0; i < ev.Instructions.Count; i++)
            {
                var ins = ev.Instructions[i];
                if (ins.Bank != 2000) continue;
                var ints = Enumerable.Range(0, ins.ArgData.Length / 4).Select(k => BitConverter.ToInt32(ins.ArgData, k * 4)).ToList();
                if (!ints.Contains(a.Main)) continue;
                if (ints.Any(v => v != a.Main && (v / 100 == a.Main / 100 || a.ExtraVariants.Contains(v)))) continue;
                var data = (byte[])ins.ArgData.Clone();
                bool changed = false;
                for (int k = 0; k < ints.Count; k++)
                    foreach (var (from, to) in a.Subs)
                        if (ints[k] == from) { BitConverter.GetBytes(to).CopyTo(data, k * 4); changed = true; }
                if (!changed) continue;
                ev.Instructions[i] = new EMEVD.Instruction(ins.Bank, ins.ID, data) { Layer = ins.Layer };
                n++;
            }
        Journal.Add("events", $"{rel} event 0", $"MMV main-boss lines for entity {a.Main} aligned to EV's boss swap ({n} lines); MMV variant bosses kept");
        return n;
    }

    public static void MergeFile(string rel)
    {
        var evPath = Paths.In(Paths.EV, rel);
        var data = Dcx.Decompress(File.ReadAllBytes(evPath), out var type);
        var ev = EMEVD.Read(data);
        var mmv = EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(MmvRewrite.MmvInput(rel))));
        AlignMainBoss(mmv, rel);
        var bp = Paths.BaseOf(rel);
        var bas = bp != null ? EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(bp))) : new EMEVD(ev.Format);
        var be = bas.Events.ToDictionary(x => x.ID); var me = mmv.Events.ToDictionary(x => x.ID);
        var result = new List<EMEVD.Event>();
        int taken = 0, added = 0, merged = 0, removed = 0, totalConf = 0;
        foreach (var e in ev.Events)
        {
            be.TryGetValue(e.ID, out var b); me.TryGetValue(e.ID, out var m);
            string sb = b == null ? null : MapDiff3.EventSig(b), se = MapDiff3.EventSig(e), sm = m == null ? null : MapDiff3.EventSig(m);
            if (sm == se || sm == sb) { result.Add(e); continue; }
            if (se == sb)
            {
                if (m == null) { removed++; continue; }
                result.Add(m); taken++; continue;
            }
            if (m == null) { result.Add(e); Journal.Add("events", $"{rel} event {e.ID}", "EV changed, MMV deleted: kept EV's"); continue; }
            var r = MergeEvent(b, e, m, out var conflicts);
            result.Add(r); merged++; totalConf += conflicts;
            Journal.Add("events", $"{rel} event {e.ID}", $"both changed: instruction-level merge, {conflicts} conflicting hunk(s) kept as EV-then-MMV" +
                (conflicts > 0 ? " :: " + string.Join(" || ", LastConflictDetails) : ""));
        }
        var evIds = ev.Events.Select(x => x.ID).ToHashSet();
        foreach (var m in mmv.Events)
            if (!evIds.Contains(m.ID) && !be.ContainsKey(m.ID)) { result.Add(m); added++; }
        ev.Events = result;
        File.WriteAllBytes(Paths.In(Paths.OutMod, rel), ev.Write(type).ToArray());
        Console.WriteLine($"{rel}: mmv-taken={taken} mmv-added={added} merged={merged} conflicts={totalConf} removed={removed}");
    }

    public static int Run()
    {
        Journal.Clear();
        foreach (var line in File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1))
        {
            var p = line.Split('\t');
            if (p[0] == "merge" && p[1].EndsWith(".emevd.dcx")) MergeFile(p[1]);
        }
        Journal.Save();
        return 0;
    }
}
