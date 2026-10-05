using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: lists TAE events whose parameter bytes contain a given int32 value.</summary>
public static class TaeFind
{
    public static int Run(string anibnd, string idsCsv)
    {
        var ids = idsCsv.Split(',').Select(int.Parse).ToHashSet();
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(anibnd)));
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae")))
        {
            var tae = TAE.Read(f.Bytes);
            foreach (var a in tae.Animations)
                foreach (var e in a.Events)
                {
                    var b = e.GetParameterBytes(false);
                    for (int i = 0; i + 4 <= b.Length; i++)
                    {
                        int v = BitConverter.ToInt32(b, i);
                        if (ids.Contains(v)) Console.WriteLine($"{Path.GetFileName(f.Name)} anim {a.ID} event type {e.Type} offset {i} value {v} params {Convert.ToHexString(b)}");
                    }
                }
        }
        return 0;
    }
}
