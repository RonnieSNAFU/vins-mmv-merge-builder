using Andre.Formats;

namespace NRMerge;

/// <summary>Moves MMV rows whose ID collides with unrelated EV content to free IDs and rewrites MMV's references.</summary>
public static class IdRemap
{
    public static int FindFreeOffset(IEnumerable<int> ids, Func<int, bool> isUsed, int step)
    {
        var list = ids.ToList();
        for (int k = 1; k < 100000; k++)
        {
            long off = (long)k * step;
            if (list.All(id => id + off <= int.MaxValue && !isUsed((int)(id + off)))) return (int)off;
        }
        throw new InvalidOperationException("no free ID range");
    }

    static bool CondOk(Func<string, object> get, ParamRefs.RefSpec s) =>
        s.CondField == null || Regulation.Fmt(get(s.CondField)) == s.CondValue;

    /// <summary>Dictionary-row version (unit-testable); returns how many references were rewritten.</summary>
    public static int RewriteRefs(IList<Dictionary<string, object>> rows, List<ParamRefs.RefSpec> specs, string target, int oldId, int newId)
    {
        int n = 0;
        var old = oldId.ToString();
        foreach (var row in rows)
            foreach (var s in specs.Where(s => s.Target == target))
            {
                if (!row.TryGetValue(s.Field, out var v) || Regulation.Fmt(v) != old) continue;
                if (!CondOk(f => row.TryGetValue(f, out var cv) ? cv : null, s)) continue;
                row[s.Field] = Convert.ChangeType(newId, v.GetType());
                n++;
            }
        return n;
    }

    /// <summary>Param version: rewrites references in every row of <paramref name="p"/>.</summary>
    public static int RewriteParam(Param p, string target, int oldId, int newId)
    {
        var specs = ParamRefs.For(p.ParamType).Where(s => s.Target == target).ToList();
        if (specs.Count == 0) return 0;
        var cols = new Dictionary<string, Param.Column>(StringComparer.Ordinal);
        foreach (var c in p.Columns) cols.TryAdd(c.Def.InternalName, c);
        var old = oldId.ToString();
        int n = 0;
        foreach (var row in p.Rows)
            foreach (var s in specs)
            {
                if (!cols.TryGetValue(s.Field, out var col)) continue;
                if (Regulation.Fmt(col.GetValue(row)) != old) continue;
                if (s.CondField != null && (!cols.TryGetValue(s.CondField, out var cc) || Regulation.Fmt(cc.GetValue(row)) != s.CondValue)) continue;
                col.SetValue(row, Convert.ChangeType(newId, col.ValueType));
                n++;
            }
        return n;
    }
}
