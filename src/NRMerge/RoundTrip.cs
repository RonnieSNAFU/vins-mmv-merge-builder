using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: checks that SoulsFormats re-writes unchanged TAE/EMEVD/MSB files to identical (or semantically identical) data.</summary>
public static class RoundTrip
{
    public static int Run(string[] paths)
    {
        foreach (var p in paths)
        {
            var raw = File.ReadAllBytes(p);
            var data = Dcx.Decompress(raw, out var type);
            string r;
            if (p.EndsWith(".anibnd.dcx"))
            {
                var bnd = BND4.Read(data);
                int same = 0, diff = 0, err = 0, semSame = 0, shown = 0;
                foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae")))
                {
                    try
                    {
                        var orig = f.Bytes.ToArray();
                        var w = TAE.Read(orig).Write();
                        if (w.AsSpan().SequenceEqual(orig)) { same++; continue; }
                        diff++;
                        var a = TaeSig.Full(TAE.Read(orig)).Split('\n');
                        var b = TaeSig.Full(TAE.Read(w)).Split('\n');
                        if (a.SequenceEqual(b)) { semSame++; continue; }
                        if (shown++ >= 2) continue;
                        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                        {
                            if (a[i] == b[i]) continue;
                            Console.WriteLine($"  {Path.GetFileName(f.Name)} first semantic diff at line {i}:");
                            Console.WriteLine($"   A {a[i][..Math.Min(300, a[i].Length)]}");
                            Console.WriteLine($"   B {b[i][..Math.Min(300, b[i].Length)]}");
                            break;
                        }
                        if (a.Length != b.Length) Console.WriteLine($"  line counts {a.Length} vs {b.Length}");
                    }
                    catch (Exception ex) { err++; if (shown++ < 2) Console.WriteLine($"  {f.Name}: {ex.Message}"); }
                }
                r = $"tae same={same} diff={diff} (semantically same {semSame}) err={err}";
            }
            else if (p.EndsWith(".emevd.dcx"))
            {
                var w = EMEVD.Read(data).Write();
                r = w.AsSpan().SequenceEqual(data) ? "emevd identical" : $"emevd differs ({data.Length} vs {w.Length})";
            }
            else if (p.EndsWith(".msb.dcx"))
            {
                var w = MSB_NR.Read(data).Write();
                r = w.AsSpan().SequenceEqual(data) ? "msb identical" : $"msb differs ({data.Length} vs {w.Length})";
            }
            else r = "skip";
            Console.WriteLine($"{Path.GetFileName(p)} [{type}]: {r}");
        }
        return 0;
    }
}
