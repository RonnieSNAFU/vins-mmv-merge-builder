using Andre.Formats;

namespace NRMerge;

/// <summary>Row identity inside a param: duplicate IDs are disambiguated by occurrence index.</summary>
public readonly record struct RowKey(int ID, int Occ)
{
    public override string ToString() => Occ == 0 ? ID.ToString() : $"{ID}#{Occ}";
}

public sealed class RowDelta
{
    public RowKey Key;
    public string Name;
    public bool Added;      // row did not exist in the base
    public bool Removed;    // row existed in base but not in the mod
    public Dictionary<string, object> Fields = new(StringComparer.Ordinal); // changed (or all, when Added) field values
    public Param.Row SourceRow; // the mod's row (null when Removed)
}

public sealed class ParamDeltaSet
{
    public string ModName;
    public ulong BaseVersion;
    public Dictionary<string, Dictionary<RowKey, RowDelta>> Params = new(StringComparer.Ordinal);
    public List<string> MissingParams = new(); // params present in base, absent in mod
    public List<string> ExtraParams = new();   // params present in mod, absent in base

    public int CountRows() => Params.Values.Sum(p => p.Count);
}

public static class ParamDiff
{
    public static Dictionary<RowKey, Param.Row> Index(Param p)
    {
        var d = new Dictionary<RowKey, Param.Row>();
        var occ = new Dictionary<int, int>();
        foreach (var r in p.Rows)
        {
            occ.TryGetValue(r.ID, out var n);
            d[new RowKey(r.ID, n)] = r;
            occ[r.ID] = n + 1;
        }
        return d;
    }

    public static bool ValEq(object a, object b)
    {
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        if (a is float fa && b is float fb) return BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb);
        return Equals(a, b);
    }

    public static ParamDeltaSet Compute(string modName, Regulation.Loaded baseReg, Regulation.Loaded mod)
    {
        var set = new ParamDeltaSet { ModName = modName, BaseVersion = baseReg.Version };
        foreach (var (name, bp) in baseReg.Params)
        {
            if (!mod.Params.TryGetValue(name, out var mp)) { set.MissingParams.Add(name); continue; }
            var bi = Index(bp);
            var mi = Index(mp);
            var rows = new Dictionary<RowKey, RowDelta>();
            // field names common to both (handles version differences)
            var mcols = mp.Columns.Select(c => c.Def.InternalName).ToHashSet(StringComparer.Ordinal);
            var cols = bp.Columns.Where(c => mcols.Contains(c.Def.InternalName)).ToList();
            var mcolByName = new Dictionary<string, Param.Column>(StringComparer.Ordinal);
            foreach (var c in mp.Columns) mcolByName.TryAdd(c.Def.InternalName, c);

            foreach (var (k, mrow) in mi)
            {
                if (!bi.TryGetValue(k, out var brow))
                {
                    var rd = new RowDelta { Key = k, Name = mrow.Name, Added = true, SourceRow = mrow };
                    foreach (var c in mp.Columns) rd.Fields[c.Def.InternalName] = c.GetValue(mrow);
                    rows[k] = rd;
                    continue;
                }
                RowDelta d = null;
                foreach (var bc in cols)
                {
                    var mc = mcolByName[bc.Def.InternalName];
                    var bv = bc.GetValue(brow);
                    var mv = mc.GetValue(mrow);
                    if (!ValEq(bv, mv))
                    {
                        d ??= new RowDelta { Key = k, Name = mrow.Name, SourceRow = mrow };
                        d.Fields[bc.Def.InternalName] = mv;
                    }
                }
                if (d != null) rows[k] = d;
            }
            foreach (var (k, brow) in bi)
                if (!mi.ContainsKey(k))
                    rows[k] = new RowDelta { Key = k, Name = brow.Name, Removed = true };
            if (rows.Count > 0) set.Params[name] = rows;
        }
        foreach (var name in mod.Params.Keys)
            if (!baseReg.Params.ContainsKey(name)) set.ExtraParams.Add(name);
        return set;
    }
}
