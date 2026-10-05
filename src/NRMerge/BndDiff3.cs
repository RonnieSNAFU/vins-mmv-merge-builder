using SoulsFormats;
using System.Security.Cryptography;
using System.Text;

namespace NRMerge;

/// <summary>Entry-level three-way comparison of binder files (BND4/BND3, optionally DCX compressed) and TPFs.</summary>
public static class BndDiff3
{
    public sealed class Entry
    {
        public string Name;
        public int ID;
        public string Hash;
        public int Size;
    }

    public static string Norm(string name)
    {
        var n = name.Replace('/', '\\').ToLowerInvariant();
        var i = n.LastIndexOf('\\');
        return i >= 0 ? n[(i + 1)..] : n;
    }

    public static Dictionary<string, Entry> ReadEntries(string path)
    {
        var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (path == null || !File.Exists(path)) return d;
        var bytes = File.ReadAllBytes(path);
        byte[] data = bytes;
        if (DCX.Is(data)) data = Dcx.Decompress(data);
        if (BND4.Is(data))
        {
            var b = BND4.Read(data);
            foreach (var f in b.Files) Add(d, f.Name ?? f.ID.ToString(), f.ID, f.Bytes.ToArray());
        }
        else if (BND3.Is(data))
        {
            var b = BND3.Read(data);
            foreach (var f in b.Files) Add(d, f.Name ?? f.ID.ToString(), f.ID, f.Bytes.ToArray());
        }
        else if (TPF.Is(data))
        {
            var t = TPF.Read(data);
            foreach (var tex in t.Textures) Add(d, tex.Name, 0, tex.Bytes.ToArray());
        }
        else throw new InvalidDataException("not a binder: " + path);
        return d;
    }

    static void Add(Dictionary<string, Entry> d, string name, int id, byte[] bytes)
    {
        var key = Norm(name);
        if (d.ContainsKey(key)) key += "#dup" + id;
        d[key] = new Entry { Name = name, ID = id, Size = bytes.Length, Hash = Convert.ToHexString(SHA1.HashData(bytes)) };
    }

    /// <summary>Writes a per-entry classification for one binder; returns a one-line summary.</summary>
    public static string Compare(string basePath, string aPath, string bPath, StringBuilder detail)
    {
        var bs = ReadEntries(basePath);
        var a = ReadEntries(aPath);
        var b = ReadEntries(bPath);
        var keys = bs.Keys.Union(a.Keys).Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal);
        var counts = new Dictionary<string, int>();
        foreach (var k in keys)
        {
            bs.TryGetValue(k, out var e0); a.TryGetValue(k, out var e1); b.TryGetValue(k, out var e2);
            string cls;
            if (e0 == null)
            {
                if (e1 != null && e2 != null) cls = e1.Hash == e2.Hash ? "add-both-same" : "add-both-DIFF";
                else cls = e1 != null ? "add-A" : "add-B";
            }
            else
            {
                bool aChg = e1 == null || e1.Hash != e0.Hash, bChg = e2 == null || e2.Hash != e0.Hash;
                if (!aChg && !bChg) cls = "same";
                else if (aChg && !bChg) cls = e1 == null ? "del-A" : "mod-A";
                else if (!aChg && bChg) cls = e2 == null ? "del-B" : "mod-B";
                else if (e1 != null && e2 != null && e1.Hash == e2.Hash) cls = "mod-both-same";
                else cls = "mod-both-DIFF";
            }
            counts[cls] = counts.GetValueOrDefault(cls) + 1;
            if (cls != "same" && cls != "add-both-same" && cls != "mod-both-same")
                detail.AppendLine($"{cls}\t{k}\t{e0?.Size}\t{e1?.Size}\t{e2?.Size}");
        }
        return string.Join(" ", counts.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
    }

    public static int Run(string listFile, string outFile)
    {
        // list lines: base \t a \t b \t label
        var sb = new StringBuilder();
        var summary = new StringBuilder();
        foreach (var line in File.ReadAllLines(listFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = line.Split('\t');
            var detail = new StringBuilder();
            string s;
            try { s = Compare(p[0] == "" ? null : p[0], p[1], p[2], detail); }
            catch (Exception e) { s = "ERROR " + e.Message; }
            summary.AppendLine($"{p[3]}\t{s}");
            sb.AppendLine($"#### {p[3]}  ::  {s}");
            sb.Append(detail);
        }
        File.WriteAllText(outFile, sb.ToString());
        Console.Write(summary.ToString());
        return 0;
    }
}
