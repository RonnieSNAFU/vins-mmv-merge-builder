using System.Text;

namespace NRMerge;

/// <summary>Dumps whole params to TSV (one row per param row, one column per field) for offline analysis.</summary>
public static class RegDump
{
    public static int Run(string game, string regPath, string paramList, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var reg = Regulation.Load(regPath, game);
        var names = paramList == "*" ? reg.Params.Keys.ToList() : paramList.Split(',').ToList();
        foreach (var pn in names)
        {
            if (!reg.Params.TryGetValue(pn, out var p)) { Console.WriteLine($"missing {pn}"); continue; }
            var cols = p.Columns.ToList();
            using var w = new StreamWriter(Path.Combine(outDir, pn + ".tsv"), false, new UTF8Encoding(false));
            w.Write("ID\tName");
            foreach (var c in cols) w.Write("\t" + c.Def.InternalName);
            w.WriteLine();
            foreach (var r in p.Rows)
            {
                w.Write(r.ID); w.Write('\t'); w.Write((r.Name ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
                foreach (var c in cols) { w.Write('\t'); w.Write(Regulation.Fmt(c.GetValue(r))); }
                w.WriteLine();
            }
        }
        Console.WriteLine($"dumped {names.Count} params from v{reg.Version}");
        return 0;
    }
}
