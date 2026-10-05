namespace NRMerge;

/// <summary>Analysis helper: lists params whose row layout differs between two regulations.</summary>
public static class RegLayout
{
    public static int Run(string a, string b)
    {
        var ra = Regulation.Load(a);
        var rb = Regulation.Load(b);
        Console.WriteLine($"A v{ra.Version}  B v{rb.Version}");
        foreach (var (name, pa) in ra.Params)
        {
            if (!rb.Params.TryGetValue(name, out var pb)) { Console.WriteLine($"{name}: only in A"); continue; }
            var ca = pa.Columns.Select(c => c.Def.InternalName).ToList();
            var cb = pb.Columns.Select(c => c.Def.InternalName).ToList();
            if (pa.RowSize != pb.RowSize || !ca.SequenceEqual(cb))
                Console.WriteLine($"{name}: rowsize {pa.RowSize}->{pb.RowSize}; only A: {string.Join(",", ca.Except(cb))}; only B: {string.Join(",", cb.Except(ca))}");
        }
        foreach (var name in rb.Params.Keys.Except(ra.Params.Keys)) Console.WriteLine($"{name}: only in B");
        return 0;
    }
}
