using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: prints one event of base/EV/MMV/merged EMEVD in readable form (instruction names from EMEDF, int args).</summary>
public static class EmevdDump
{
    public static int Run(string rel, long eventId)
    {
        var d = Emedf.Load(MmvRewrite.EmedfPath);
        foreach (var (tag, path) in new[] { ("BASE", Paths.BaseOf(rel)), ("EV", Paths.In(Paths.EV, rel)), ("MMV", MmvRewrite.MmvInput(rel)), ("OUT", Paths.In(Paths.OutMod, rel)) })
        {
            if (path == null || !File.Exists(path)) continue;
            var e = EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(path))).Events.FirstOrDefault(x => x.ID == eventId);
            Console.WriteLine($"== {tag} ({e?.Instructions.Count} instr, {e?.Parameters.Count} params)");
            if (e == null) continue;
            for (int i = 0; i < e.Instructions.Count; i++)
            {
                var ins = e.Instructions[i];
                d.Instrs.TryGetValue((ins.Bank, ins.ID), out var def);
                var ints = Enumerable.Range(0, ins.ArgData.Length / 4).Select(k => BitConverter.ToInt32(ins.ArgData, k * 4));
                var ps = e.Parameters.Where(p => p.InstructionIndex == i).Select(p => $"p{p.SourceStartByte}->{p.TargetStartByte}");
                Console.WriteLine($"  {i,3} {ins.Bank}[{ins.ID}] {def?.Name ?? "?"}: {string.Join(" ", ints)} {string.Join(",", ps)}");
            }
        }
        return 0;
    }
}
