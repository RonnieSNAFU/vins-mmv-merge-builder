namespace NRMerge;

/// <summary>
/// Spec §5 lottery tables: rows sharing an ID form a weighted table. Tables are merged as sequences matched by entry
/// content (not row position); a base entry replaced differently by both mods keeps both replacements, each with its
/// weight halved.
/// </summary>
public static class TableMerge
{
    public sealed class Entry
    {
        public string Key;
        public int Weight;
        public int Weight2;
        public object Source;

        public Entry(string key, int weight, int weight2 = 0, object source = null)
        {
            Key = key; Weight = weight; Weight2 = weight2; Source = source;
        }

        public string MatchKey => $"{Key}|{Weight}|{Weight2}";
    }

    public static int Halve(int w) => w <= 0 ? w : Math.Max(1, (int)Math.Round(w / 2.0, MidpointRounding.AwayFromZero));

    public static List<Entry> MergeEntries(List<Entry> baseL, List<Entry> ev, List<Entry> mmv, out int conflicts)
    {
        var result = Seq3.Merge(baseL, ev, mmv, e => e.MatchKey, out var cs);
        conflicts = 0;
        foreach (var c in cs)
        {
            if (c.Base.Count == 0) continue; // both inserted at the same spot: keep both as additions
            conflicts++;
            var inResult = new HashSet<Entry>(result, ReferenceEqualityComparer.Instance);
            foreach (var e in c.A.Concat(c.B).Where(inResult.Contains).Distinct(ReferenceEqualityComparer.Instance).Cast<Entry>())
            {
                e.Weight = Halve(e.Weight);
                e.Weight2 = Halve(e.Weight2);
            }
        }
        return result;
    }
}
