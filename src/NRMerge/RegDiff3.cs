using Andre.Formats;
using System.Text;

namespace NRMerge;

/// <summary>Three-way regulation analysis: each mod is diffed against the vanilla regulation of its own version.</summary>
public static class RegDiff3
{
    public static Regulation.Loaded BaseFor(Regulation.Loaded mod, Regulation.Loaded current)
    {
        if (mod.Version == current.Version) return current;
        var p = Regulation.VanillaRegulationForVersion(mod.Version.ToString());
        if (p == null)
        {
            Console.WriteLine($"  !! no bundled vanilla regulation for version {mod.Version}; falling back to current vanilla {current.Version}");
            return current;
        }
        Console.WriteLine($"  base for {Path.GetFileName(mod.Path)} (v{mod.Version}) -> {p}");
        return Regulation.Load(p);
    }

    public static int Run(string vanillaPath, string nameA, string regA, string nameB, string regB, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var cur = Regulation.Load(vanillaPath);
        var a = Regulation.Load(regA);
        var b = Regulation.Load(regB);
        Console.WriteLine($"vanilla v{cur.Version}  {nameA} v{a.Version}  {nameB} v{b.Version}");
        foreach (var l in new[] { cur, a, b })
            foreach (var e in l.Errors) Console.WriteLine($"  load error [{Path.GetFileName(l.Path)}] {e}");

        var baseA = BaseFor(a, cur);
        var baseB = BaseFor(b, cur);
        var da = ParamDiff.Compute(nameA, baseA, a);
        var db = ParamDiff.Compute(nameB, baseB, b);

        WriteChanges(Path.Combine(outDir, $"{nameA}_changes.tsv"), da, baseA);
        WriteChanges(Path.Combine(outDir, $"{nameB}_changes.tsv"), db, baseB);

        var sum = new StringBuilder();
        sum.AppendLine("param\tA_add\tA_rem\tA_mod\tB_add\tB_rem\tB_mod\tbothTouched\tconflictRows\tconflictFields\taddCollide\taddIdentical");
        var conf = new StringBuilder();
        conf.AppendLine("param\trow\tkind\tfield\tvanilla\tA\tB\tnameA\tnameB");
        var allParams = da.Params.Keys.Union(db.Params.Keys).OrderBy(x => x, StringComparer.Ordinal);
        int totConfRows = 0, totConfFields = 0, totCollide = 0;
        foreach (var pn in allParams)
        {
            da.Params.TryGetValue(pn, out var ra); ra ??= new();
            db.Params.TryGetValue(pn, out var rb); rb ??= new();
            cur.Params.TryGetValue(pn, out var vp);
            var vi = vp != null ? ParamDiff.Index(vp) : new();
            int both = 0, confRows = 0, confFields = 0, collide = 0, addIdent = 0;
            foreach (var (k, x) in ra)
            {
                if (!rb.TryGetValue(k, out var y)) continue;
                both++;
                if (x.Added && y.Added)
                {
                    var diffs = x.Fields.Where(f => y.Fields.TryGetValue(f.Key, out var yv) && !ParamDiff.ValEq(f.Value, yv)).ToList();
                    if (diffs.Count == 0) { addIdent++; continue; }
                    collide++;
                    foreach (var f in diffs)
                        conf.AppendLine($"{pn}\t{k}\tADD-COLLIDE\t{f.Key}\t\t{Regulation.Fmt(f.Value)}\t{Regulation.Fmt(y.Fields[f.Key])}\t{x.Name}\t{y.Name}");
                    continue;
                }
                if (x.Removed || y.Removed)
                {
                    if (x.Removed != y.Removed)
                    {
                        confRows++;
                        conf.AppendLine($"{pn}\t{k}\tREMOVE-vs-{(x.Removed ? "B" : "A")}\t\t\t{(x.Removed ? "REMOVED" : "kept")}\t{(y.Removed ? "REMOVED" : "kept")}\t{x.Name}\t{y.Name}");
                    }
                    continue;
                }
                bool rowConf = false;
                foreach (var (f, av) in x.Fields)
                {
                    if (!y.Fields.TryGetValue(f, out var bv)) continue;
                    if (ParamDiff.ValEq(av, bv)) continue;
                    rowConf = true; confFields++;
                    object van = null;
                    if (vi.TryGetValue(k, out var vrow)) { var col = vp[f]; if (col != null) van = col.GetValue(vrow); }
                    conf.AppendLine($"{pn}\t{k}\tMOD-CONFLICT\t{f}\t{Regulation.Fmt(van)}\t{Regulation.Fmt(av)}\t{Regulation.Fmt(bv)}\t{x.Name}\t{y.Name}");
                }
                if (rowConf) confRows++;
            }
            totConfRows += confRows; totConfFields += confFields; totCollide += collide;
            sum.AppendLine($"{pn}\t{ra.Values.Count(r => r.Added)}\t{ra.Values.Count(r => r.Removed)}\t{ra.Values.Count(r => !r.Added && !r.Removed)}\t{rb.Values.Count(r => r.Added)}\t{rb.Values.Count(r => r.Removed)}\t{rb.Values.Count(r => !r.Added && !r.Removed)}\t{both}\t{confRows}\t{confFields}\t{collide}\t{addIdent}");
        }
        File.WriteAllText(Path.Combine(outDir, "summary.tsv"), sum.ToString());
        File.WriteAllText(Path.Combine(outDir, "conflicts.tsv"), conf.ToString());
        Console.WriteLine($"{nameA}: {da.CountRows()} touched rows in {da.Params.Count} params (missing params: {string.Join(",", da.MissingParams)}; extra: {string.Join(",", da.ExtraParams)})");
        Console.WriteLine($"{nameB}: {db.CountRows()} touched rows in {db.Params.Count} params (missing params: {string.Join(",", db.MissingParams)}; extra: {string.Join(",", db.ExtraParams)})");
        Console.WriteLine($"conflicting modified rows={totConfRows} fields={totConfFields}; colliding added rows={totCollide}");
        return 0;
    }

    static void WriteChanges(string path, ParamDeltaSet d, Regulation.Loaded baseReg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("param\trow\tname\tkind\tfield\tbase\tnew");
        foreach (var (pn, rows) in d.Params.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            baseReg.Params.TryGetValue(pn, out var bp);
            var bi = bp != null ? ParamDiff.Index(bp) : new();
            foreach (var (k, r) in rows.OrderBy(x => x.Key.ID).ThenBy(x => x.Key.Occ))
            {
                if (r.Added) { sb.AppendLine($"{pn}\t{k}\t{r.Name}\tADDED\t\t\t"); continue; }
                if (r.Removed) { sb.AppendLine($"{pn}\t{k}\t{r.Name}\tREMOVED\t\t\t"); continue; }
                foreach (var (f, v) in r.Fields)
                {
                    object bv = null;
                    if (bi.TryGetValue(k, out var brow)) { var c = bp[f]; if (c != null) bv = c.GetValue(brow); }
                    sb.AppendLine($"{pn}\t{k}\t{r.Name}\tMOD\t{f}\t{Regulation.Fmt(bv)}\t{Regulation.Fmt(v)}");
                }
            }
        }
        File.WriteAllText(path, sb.ToString());
    }
}
