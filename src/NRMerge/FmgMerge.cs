using SoulsFormats;

namespace NRMerge;

/// <summary>Per-entry three-way merge of FMG text, plus re-pointing of MMV text for items moved by the regulation stage.</summary>
public static class FmgMerge
{
    public static FMG Merge(FMG b, FMG ev, FMG mmv, Side winner, string label, out int conflicts)
    {
        var bm = Map(b); var em = Map(ev); var mm = Map(mmv);
        var res = new FMG { Version = ev.Version, BigEndian = ev.BigEndian, Unicode = ev.Unicode };
        conflicts = 0;
        foreach (var id in em.Keys.Union(mm.Keys).Union(bm.Keys).OrderBy(x => x))
        {
            bm.TryGetValue(id, out var t0);
            bool inE = em.TryGetValue(id, out var t1), inM = mm.TryGetValue(id, out var t2);
            string text;
            if (!inE && !inM) continue;
            if (!inE) text = bm.ContainsKey(id) ? null : t2;          // EV deleted a base entry: keep deleted
            else if (!inM) text = bm.ContainsKey(id) ? (t1 == t0 ? null : t1) : t1;
            else if (t1 == t2) text = t1;
            else if (bm.ContainsKey(id) && t1 == t0) text = t2;
            else if (bm.ContainsKey(id) && t2 == t0) text = t1;
            else
            {
                conflicts++;
                text = winner == Side.EV ? t1 : t2;
                Journal.Add("text", $"{label} {id}", $"both changed; {winner} kept: EV='{Short(t1)}' MMV='{Short(t2)}'");
            }
            if (text == null && !(inE && inM)) continue;
            res.Entries.Add(new FMG.Entry(res, id, text));
        }
        return res;
    }

    static string Short(string s) => s == null ? "null" : (s.Length > 60 ? s[..60] + "…" : s).Replace("\n", " ");

    static Dictionary<int, string> Map(FMG f)
    {
        var d = new Dictionary<int, string>();
        if (f != null) foreach (var e in f.Entries) d[e.ID] = e.Text;
        return d;
    }

    /// <summary>Moves entries whose IDs were remapped (old → new) inside one FMG.</summary>
    public static int ApplyRemap(FMG f, IReadOnlyDictionary<long, long> map)
    {
        int n = 0;
        foreach (var e in f.Entries)
            if (map.TryGetValue(e.ID, out var nv)) { e.ID = (int)nv; n++; }
        return n;
    }
}
