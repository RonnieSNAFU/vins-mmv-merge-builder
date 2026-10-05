using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: prints the merged event with origin tags (E = in EV, M = in MMV, B = in base) per instruction.</summary>
public static class EmevdDiffView
{
    static string Key(EMEVD.Instruction i) => $"{i.Bank}:{i.ID}:{Convert.ToHexString(i.ArgData)}";
    public static int Run(string rel, long id)
    {
        EMEVD.Event Get(string p) => p == null || !File.Exists(p) ? null : EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(p))).Events.FirstOrDefault(x => x.ID == id);
        var b = Get(Paths.BaseOf(rel)); var e = Get(Paths.In(Paths.EV, rel)); var m = Get(MmvRewrite.MmvInput(rel)); var o = Get(Paths.In(Paths.OutMod, rel));
        var kb = b?.Instructions.Select(Key).ToHashSet() ?? new(); var ke = e?.Instructions.Select(Key).ToHashSet() ?? new(); var km = m?.Instructions.Select(Key).ToHashSet() ?? new();
        var d = Emedf.Load(MmvRewrite.EmedfPath);
        Console.WriteLine($"{rel} event {id}: base {b?.Instructions.Count} EV {e?.Instructions.Count} MMV {m?.Instructions.Count} OUT {o?.Instructions.Count}");
        for (int i = 0; i < o.Instructions.Count; i++)
        {
            var ins = o.Instructions[i]; var k = Key(ins);
            var tag = $"{(kb.Contains(k) ? "B" : "-")}{(ke.Contains(k) ? "E" : "-")}{(km.Contains(k) ? "M" : "-")}";
            d.Instrs.TryGetValue((ins.Bank, ins.ID), out var def);
            var ints = Enumerable.Range(0, ins.ArgData.Length / 4).Select(x => BitConverter.ToInt32(ins.ArgData, x * 4));
            Console.WriteLine($" {i,3} {tag} {def?.Name ?? $"{ins.Bank}[{ins.ID}]"}: {string.Join(" ", ints)}");
        }
        return 0;
    }
}
