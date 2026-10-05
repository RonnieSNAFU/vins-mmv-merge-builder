using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: prints FMG entries of a msgbnd matching given IDs (or all entries of one FMG).</summary>
public static class FmgDump
{
    public static int Run(string msgbnd, string idsCsv)
    {
        var ids = idsCsv == "*" ? null : idsCsv.Split(',').Select(int.Parse).ToHashSet();
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(msgbnd)));
        foreach (var f in bnd.Files)
        {
            var fmg = FMG.Read(f.Bytes);
            foreach (var e in fmg.Entries)
                if (ids == null || ids.Contains(e.ID))
                    Console.WriteLine($"{Path.GetFileName(f.Name)}\t{e.ID}\t{e.Text?.Replace("\n", "\n")}");
        }
        return 0;
    }
}
