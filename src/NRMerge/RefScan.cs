using SoulsFormats;
using System.Text;

namespace NRMerge;

/// <summary>Analysis helper: finds where 32-bit IDs (little endian) or their decimal text occur inside a mod's files, including binder entries.</summary>
public static class RefScan
{
    public static int Run(string root, string idsCsv, string outFile)
    {
        var ids = idsCsv.Split(',').Select(int.Parse).ToList();
        var pats = ids.ToDictionary(i => i, i => BitConverter.GetBytes(i));
        var sb = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f);
            if (rel.EndsWith(".bnk") || rel.EndsWith(".wem") || rel.Contains("texbnd") || rel.EndsWith(".tpf.dcx") || rel.StartsWith("sd")) continue;
            byte[] data;
            try { data = File.ReadAllBytes(f); if (DCX.Is(data)) data = Dcx.Decompress(data); } catch { continue; }
            var parts = new List<(string, byte[])>();
            try
            {
                if (BND4.Is(data)) foreach (var e in BND4.Read(data).Files) parts.Add((e.Name, e.Bytes.ToArray()));
                else if (BND3.Is(data)) foreach (var e in BND3.Read(data).Files) parts.Add((e.Name, e.Bytes.ToArray()));
                else parts.Add(("", data));
            }
            catch { parts.Add(("", data)); }
            foreach (var (name, bytes) in parts)
            {
                if (name.EndsWith(".hkx") || name.EndsWith(".flver") || name.EndsWith(".tpf")) continue;
                foreach (var (id, pat) in pats)
                {
                    int n = Count(bytes, pat);
                    int t = Count(bytes, Encoding.ASCII.GetBytes(id.ToString()));
                    if (n + t > 0) sb.AppendLine($"{id}\t{rel}\t{Path.GetFileName(name)}\tbin={n}\ttext={t}");
                }
            }
        }
        File.WriteAllText(outFile, sb.ToString());
        Console.Write(sb.ToString());
        return 0;
    }

    static int Count(byte[] hay, byte[] needle)
    {
        int n = 0, i = 0;
        var span = hay.AsSpan();
        while (true)
        {
            int j = span[i..].IndexOf(needle);
            if (j < 0) return n;
            n++; i += j + 1;
        }
    }
}
