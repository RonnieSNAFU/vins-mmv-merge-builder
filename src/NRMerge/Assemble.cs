using System.Text;

namespace NRMerge;

/// <summary>Stage 1: classify every file of both mods and copy everything that needs no content merge.</summary>
public static class Assemble
{
    const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    public static bool IsExcluded(string rel)
    {
        var r = rel.Replace('\\', '/');
        return r.StartsWith("dll/", OIC) || r.StartsWith("ServerRedirector/", OIC) || r.StartsWith(".smithbox/", OIC)
            || r.Equals("project.json", OIC) || r.Equals("regulation.bin", OIC) || r.Equals("regulation.bin.prev", OIC)
            || r.EndsWith(".log", OIC);
    }

    public static string Classify(string evPath, string mmvPath, string basePath)
    {
        bool e = File.Exists(evPath), m = File.Exists(mmvPath);
        if (e && !m) return "ev-only";
        if (!e && m) return "mmv-only";
        if (!e) throw new ArgumentException("file exists in neither mod");
        if (SameBytes(evPath, mmvPath)) return "identical";
        if (basePath != null && File.Exists(basePath))
        {
            if (SameBytes(evPath, basePath)) return "take-mmv";
            if (SameBytes(mmvPath, basePath)) return "take-ev";
        }
        return "merge";
    }

    /// <summary>Without Elden Ring a file whose base was an Elden Ring original is classified by comparing both mods' copies with
    /// that original's SHA-256 from the vanilla manifest: the same take-ev/take-mmv/merge result as the verified build, with no
    /// game file. Other files: as usual.</summary>
    public static string ClassifyWithoutEldenRing(string rel, string evPath, string mmvPath)
    {
        var nr = Paths.In(Paths.Vanilla, rel);
        var erSha = File.Exists(nr) ? null : ErFallback.ErBaseSha(rel);
        if (erSha == null) return Classify(evPath, mmvPath, Paths.BaseOf(rel));
        var cls = Classify(evPath, mmvPath, null);
        if (cls != "merge") return cls;
        cls = Sha(evPath) == erSha ? "take-mmv" : Sha(mmvPath) == erSha ? "take-ev" : "merge";
        ErFallback.Note(rel.Replace('\\', '/').TrimStart('/'), cls == "merge"
            ? "no Elden Ring base: both mods changed the Elden Ring original (by its manifest SHA-256); merged without a base (owner rule, EV kept where both changed it)"
            : $"no Elden Ring base: {cls} decided by the Elden Ring original's SHA-256 from the vanilla manifest (same as the verified build)");
        return cls;
    }

    static string Sha(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(s));
    }

    public static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = fa.OpenRead();
        using var sb = fb.OpenRead();
        var ba = new byte[1 << 20];
        var bb = new byte[1 << 20];
        while (true)
        {
            int na = sa.ReadAtLeast(ba, ba.Length, false);
            int nb = sb.ReadAtLeast(bb, bb.Length, false);
            if (na != nb) return false;
            if (na == 0) return true;
            if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
        }
    }

    static IEnumerable<string> RelFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));

    public static int Run()
    {
        var all = RelFiles(Paths.EV).Union(RelFiles(Paths.MMV), StringComparer.OrdinalIgnoreCase)
            .Where(r => !IsExcluded(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var counts = new SortedDictionary<string, int>();
        var tsv = new StringBuilder("class\tpath\n");
        foreach (var rel in all)
        {
            var ev = Paths.In(Paths.EV, rel);
            var mmv = Paths.In(Paths.MMV, rel);
            var cls = Paths.HasEldenRing ? Classify(ev, mmv, Paths.BaseOf(rel)) : ClassifyWithoutEldenRing(rel, ev, mmv);
            counts[cls] = counts.GetValueOrDefault(cls) + 1;
            tsv.Append(cls).Append('\t').Append(rel).Append('\n');
            string src = cls switch
            {
                "ev-only" or "take-ev" or "identical" => ev,
                "mmv-only" or "take-mmv" => mmv,
                _ => null
            };
            if (src == null) continue;
            var dst = Paths.In(Paths.OutMod, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (!File.Exists(dst) || !SameBytes(src, dst)) File.Copy(src, dst, true);
        }
        Directory.CreateDirectory(Paths.Out);
        File.WriteAllText(Path.Combine(Paths.Out, "assembly.tsv"), tsv.ToString());
        foreach (var (k, v) in counts) Console.WriteLine($"{k}\t{v}");
        return 0;
    }
}
