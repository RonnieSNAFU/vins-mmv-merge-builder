using SoulsFormats;
using System.Text;

namespace NRMerge;

/// <summary>
/// Stage: applies RefRewrite to MMV's own files (spec §14). Rewritten copies go to work\mmv_rw (inputs for merge
/// stages); MMV-only and take-MMV files are also refreshed in out\mod.
/// </summary>
public static class MmvRewrite
{
    public static string RwRoot => Path.Combine(Paths.Work, "mmv_rw");
    public static string TaeTemplatePath => Paths.TaeTemplate;
    public static string EmedfPath => Paths.Emedf;

    /// <summary>MMV input for a merge stage: the rewritten copy when one exists.</summary>
    public static string MmvInput(string rel)
    {
        var rw = Paths.In(RwRoot, rel);
        return File.Exists(rw) ? rw : Paths.In(Paths.MMV, rel);
    }

    static bool IsBytecode(ReadOnlySpan<byte> b) => b.Length > 0 && b[0] == 0x1B;

    public static int Run(bool dryRun)
    {
        Journal.Clear();
        var table = RemapTable.Load();
        var tmpl = TaeRefTemplate.Load(TaeTemplatePath);
        var emedf = Emedf.Load(EmedfPath);
        var textMap = table.For("SpEffectParam", "Bullet", "NpcParam");
        var classes = File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1)
            .Select(l => l.Split('\t')).ToDictionary(p => p[1], p => p[0], StringComparer.OrdinalIgnoreCase);

        var commonFiles = new[] { "event/common_func.emevd.dcx", "event/common.emevd.dcx" }
            .Select(r => Paths.In(Paths.MMV, r)).Where(File.Exists)
            .Select(p => EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(p)))).ToList();
        var commonSigs = RefRewrite.EventSignatures(commonFiles, emedf);

        int files = 0, total = 0;
        foreach (var path in Directory.EnumerateFiles(Paths.MMV, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Paths.MMV, path).Replace('\\', '/');
            if (Assemble.IsExcluded(rel) || rel.EndsWith(".bak")) continue;
            byte[] outBytes = null;
            int n = 0;
            string note = "";
            try
            {
                if (rel.EndsWith(".anibnd.dcx"))
                {
                    var raw = File.ReadAllBytes(path);
                    var data = Dcx.Decompress(raw, out var type);
                    var bnd = BND4.Read(data);
                    foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)))
                    {
                        var tae = TAE.Read(f.Bytes);
                        int k = RefRewrite.RewriteTae(tae, tmpl, table);
                        if (k == 0) continue;
                        f.Bytes = tae.Write();
                        n += k;
                        note += $"{Path.GetFileName(f.Name)}:{k} ";
                    }
                    if (n > 0) outBytes = bnd.Write(type).ToArray();
                }
                else if (rel.EndsWith(".emevd.dcx"))
                {
                    var data = Dcx.Decompress(File.ReadAllBytes(path), out var type);
                    var e = EMEVD.Read(data);
                    var sigs = new Dictionary<long, Dictionary<int, string>>(commonSigs);
                    foreach (var (k, v) in RefRewrite.EventSignatures(new[] { e }, emedf)) sigs[k] = v;
                    n = RefRewrite.RewriteEmevd(e, emedf, table, sigs);
                    if (n > 0) outBytes = e.Write(type).ToArray();
                }
                else if (rel.EndsWith(".msb.dcx"))
                {
                    var data = Dcx.Decompress(File.ReadAllBytes(path), out var type);
                    var m = MSB_NR.Read(data);
                    n = RefRewrite.RewriteMsb(m, table);
                    if (n > 0) outBytes = m.Write(type).ToArray();
                }
                else if (rel.StartsWith("script/") && rel.EndsWith(".luabnd.dcx"))
                {
                    var data = Dcx.Decompress(File.ReadAllBytes(path), out var type);
                    var bnd = BND4.Read(data);
                    foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (IsBytecode(f.Bytes.Span)) continue;
                        var text = Encoding.UTF8.GetString(f.Bytes.Span);
                        var nt = RefRewrite.RewriteText(text, textMap, out var k);
                        if (k == 0) continue;
                        f.Bytes = Encoding.UTF8.GetBytes(nt);
                        n += k;
                        note += $"{Path.GetFileName(f.Name)}:{k} ";
                    }
                    if (n > 0) outBytes = bnd.Write(type).ToArray();
                }
                else if (rel.StartsWith("action/script/") && rel.EndsWith(".hks"))
                {
                    var raw = File.ReadAllBytes(path);
                    if (!IsBytecode(raw))
                    {
                        var text = Encoding.UTF8.GetString(raw);
                        var nt = RefRewrite.RewriteText(text, table.For("SpEffectParam"), out n);
                        if (n > 0) outBytes = Encoding.UTF8.GetBytes(nt);
                    }
                }
            }
            catch (Exception ex)
            {
                Journal.Add("refs", rel, "ERROR " + ex.Message);
                Console.WriteLine($"ERROR {rel}: {ex.Message}");
                continue;
            }
            if (n == 0) continue;
            files++; total += n;
            classes.TryGetValue(rel, out var cls);
            Journal.Add("refs", rel, $"{n} references re-pointed to moved MMV rows {note}(assembly class: {cls})");
            Console.WriteLine($"{rel}\t{n}\t{cls}\t{note}");
            if (dryRun) continue;
            var rw = Paths.In(RwRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(rw));
            File.WriteAllBytes(rw, outBytes);
            if (cls is "mmv-only" or "take-mmv")
                File.WriteAllBytes(Paths.In(Paths.OutMod, rel), outBytes);
        }
        Console.WriteLine($"{files} files, {total} references");
        if (!dryRun) Journal.Save();
        return 0;
    }
}
