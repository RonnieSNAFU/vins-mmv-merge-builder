using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoulsFormats;
using RegKeys = SoulsFormats.Util.Keys;

namespace NRMerge;

/// <summary>
/// Makes a build without Elden Ring come out identical in content to the verified build.
/// <para>The merge without Elden Ring is deterministic, so the maintainer records, for each file whose content differs from the
/// verified build, a delta from the no-Elden-Ring result to the verified result (<c>make-noer-patches</c>). The delta copies from
/// the no-Elden-Ring result itself and from the player's own EV, MMV and vanilla Nightreign files, so it carries almost no
/// bytes of its own (the literal count is printed and stored). The <c>noerpatch</c> stage replays the deltas when Elden Ring is
/// absent; every input and output is checked by the SHA-256 of its content.</para>
/// Content means decompressed bytes (DCX) and, for regulation.bin, decrypted and decompressed bytes: Elden Ring's Oodle build
/// is not available without Elden Ring, so the compressed bytes differ while the game reads the same data.
/// </summary>
public static class NoErPatch
{
    public const string Folder = "noer-patches";
    public const string IndexName = "index.json";
    /// <summary>Maintainers set this to 1 for the raw no-Elden-Ring build that <c>make-noer-patches</c> starts from.</summary>
    public const string SkipVariable = "NRMERGE_SKIP_NOER_PATCHES";
    public static string Dir => Path.Combine(Paths.Manifests, Folder);

    public sealed class Source { public string Root { get; set; } public string Sha { get; set; } }

    public sealed class Entry
    {
        public string Rel { get; set; }
        /// <summary>"patch" (file in both builds), "add" (only in the verified build), "delete" (only without Elden Ring).</summary>
        public string Op { get; set; }
        public int Dcx { get; set; }
        public string Pre { get; set; }
        public string Post { get; set; }
        public List<Source> Sources { get; set; } = new();
        public string Patch { get; set; }
        public long Literal { get; set; }
    }

    public sealed class Index
    {
        public string Note { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ------------------------------------------------------------------ content

    public static bool IsRegulation(string rel) => rel.Replace('\\', '/').TrimStart('/').Equals("regulation.bin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Decompressed (and for regulation.bin decrypted) bytes of a file.</summary>
    public static byte[] ReadContent(string path, string rel, out DCX.Type type)
    {
        var b = File.ReadAllBytes(path);
        if (IsRegulation(rel) && !(b.Length >= 4 && (b.AsSpan(0, 4).SequenceEqual("DCX\0"u8) || b.AsSpan(0, 4).SequenceEqual("BND4"u8))))
            b = SFUtil.DecryptByteArray(RegKeys.NR_REGULATION_KEY, b);
        return Dcx.Decompress(b, out type);
    }

    /// <summary>Inverse of <see cref="ReadContent"/>: compresses with <paramref name="type"/> and, for regulation.bin, encrypts
    /// with a zero IV exactly as <see cref="Regulation.Save"/> does.</summary>
    public static byte[] Pack(string rel, byte[] content, DCX.Type type)
    {
        var b = type == DCX.Type.None ? content : DCX.Compress(content, type);
        return IsRegulation(rel) ? Encrypt(b) : b;
    }

    static byte[] Encrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.KeySize = 256; aes.BlockSize = 128;
        var iv = new byte[16];
        using var ms = new MemoryStream();
        ms.Write(iv);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(RegKeys.NR_REGULATION_KEY, iv), CryptoStreamMode.Write))
            cs.Write(data);
        return ms.ToArray();
    }

    public static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static string Norm(string rel) => rel.Replace('\\', '/').TrimStart('/');

    static IEnumerable<string> ModFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(f => Norm(Path.GetRelativePath(root, f)))
        .Where(r => !r.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !r.Contains("dll/logs/", StringComparison.OrdinalIgnoreCase));

    /// <summary>The source files a delta for <paramref name="rel"/> may copy from, besides the no-Elden-Ring result: the same
    /// path in EV, in MMV and in the extracted vanilla Nightreign files (whichever exist).</summary>
    static List<(string Root, string Path)> SourcesOf(string rel) =>
        new[] { ("ev", Paths.EV), ("mmv", Paths.MMV), ("vanilla", Paths.Vanilla) }
            .Where(s => s.Item1 != "vanilla" || VanillaRels().Contains(Norm(rel)))
            .Select(s => (s.Item1, Paths.In(s.Item2, rel))).Where(s => File.Exists(s.Item2)).ToList();

    /// <summary>Vanilla Nightreign files every build extracts (the shipped manifest); a dev extract may hold more.</summary>
    static HashSet<string> VanillaRels()
    {
        var src = VanillaManifest.DefaultPath;
        if (_vanilla == null || _vanillaSource != src)
            (_vanilla, _vanillaSource) = (File.Exists(src)
                ? VanillaManifest.Load(src).Entries.Where(e => e.Game == "NR" && !e.Absent).Select(e => Norm(e.Rel)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase), src);
        return _vanilla;
    }
    static HashSet<string> _vanilla;
    static string _vanillaSource;

    // ------------------------------------------------------------------ make (maintainer)

    /// <summary>make-noer-patches &lt;verifiedMod&gt; &lt;noErMod&gt; &lt;outDir&gt;: writes the deltas and index.json.
    /// EV, MMV and vanilla come from the current <see cref="Paths"/> configuration.</summary>
    public static int Make(string fullMod, string noerMod, string outDir)
    {
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        var a = ModFiles(fullMod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = ModFiles(noerMod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = new Index { Note = "Deltas from the build without Elden Ring to the verified build (content level); see NoErPatch.cs." };
        long literal = 0, target = 0;
        foreach (var rel in a.Union(b, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (!a.Contains(rel)) { index.Entries.Add(new Entry { Rel = rel, Op = "delete", Pre = Sha(ReadContent(Paths.In(noerMod, rel), rel, out _)) }); continue; }
            var post = ReadContent(Paths.In(fullMod, rel), rel, out var type);
            byte[] pre = null;
            if (b.Contains(rel))
            {
                pre = ReadContent(Paths.In(noerMod, rel), rel, out _);
                if (pre.AsSpan().SequenceEqual(post)) continue;
            }
            var e = new Entry { Rel = rel, Op = pre == null ? "add" : "patch", Dcx = (int)type, Pre = pre == null ? null : Sha(pre), Post = Sha(post) };
            var parts = new List<byte[]>();
            if (pre != null) parts.Add(pre);
            foreach (var (root, path) in SourcesOf(rel))
            {
                var c = ReadContent(path, rel, out _);
                parts.Add(c);
                e.Sources.Add(new Source { Root = root, Sha = Sha(c) });
            }
            var delta = Delta.Encode(Concat(parts), post, out var lit);
            e.Literal = lit;
            e.Patch = rel.Replace('/', '_') + ".nrpatch";
            File.WriteAllBytes(Path.Combine(outDir, e.Patch), delta);
            index.Entries.Add(e);
            literal += lit; target += post.Length;
            Console.WriteLine($"{e.Op}\t{rel}\t{post.Length} bytes, {lit} literal, patch {delta.Length}");
        }
        File.WriteAllText(Path.Combine(outDir, IndexName), JsonSerializer.Serialize(index, Json), new UTF8Encoding(false));
        Console.WriteLine($"{index.Entries.Count} file(s); {literal} literal byte(s) of {target} target byte(s) -> {outDir}");
        return 0;
    }

    static byte[] Concat(List<byte[]> parts)
    {
        var r = new byte[parts.Sum(p => (long)p.Length)];
        long o = 0;
        foreach (var p in parts) { p.CopyTo(r, o); o += p.Length; }
        return r;
    }

    // ------------------------------------------------------------------ apply (stage)

    /// <summary>Stage <c>noerpatch</c>: with Elden Ring nothing to do; without it, replays every delta onto <see cref="Paths.OutMod"/>.</summary>
    public static int Run()
    {
        if (Paths.HasEldenRing) { Console.WriteLine("Elden Ring present: nothing to replay"); return 0; }
        if (Environment.GetEnvironmentVariable(SkipVariable) == "1")
        {
            ErFallback.Note(Folder, $"{SkipVariable}=1: replay skipped (maintainer build used to make the patches); the output differs from the verified build");
            Console.WriteLine($"{SkipVariable}=1: replay skipped");
            return 0;
        }
        var idx = Path.Combine(Dir, IndexName);
        if (!File.Exists(idx)) throw new BuildException($"the no-Elden-Ring patches are missing ({idx}); the builder release is incomplete.");
        var index = JsonSerializer.Deserialize<Index>(File.ReadAllText(idx), Json);
        int n = Apply(index, Dir, Paths.OutMod);
        ErFallback.Note(Folder, $"replayed {n} file(s) from the verified build: the content is identical to the verified build");
        Console.WriteLine($"replayed {n} file(s): content now identical to the verified build");
        return 0;
    }

    public static int Apply(Index index, string patchDir, string mod)
    {
        int n = 0;
        foreach (var e in index.Entries)
        {
            var path = Paths.In(mod, e.Rel);
            byte[] pre = null;
            if (e.Op != "add")
            {
                if (!File.Exists(path)) throw new BuildException($"no-Elden-Ring replay: {e.Rel} is missing from the merge result.");
                pre = ReadContent(path, e.Rel, out _);
                if (Sha(pre) != e.Pre)
                    throw new BuildException($"no-Elden-Ring replay: {e.Rel} is not what this builder version produces without Elden Ring (content SHA-256 {Sha(pre)}, expected {e.Pre}).");
            }
            if (e.Op == "delete") { File.Delete(path); n++; continue; }
            var parts = new List<byte[]>();
            if (pre != null) parts.Add(pre);
            var srcs = SourcesOf(e.Rel);
            if (srcs.Count != e.Sources.Count || srcs.Zip(e.Sources).Any(x => x.First.Root != x.Second.Root))
                throw new BuildException($"no-Elden-Ring replay: the source files of {e.Rel} differ from the builder's (expected {string.Join(", ", e.Sources.Select(s => s.Root))}).");
            foreach (var ((root, sp), want) in srcs.Zip(e.Sources))
            {
                var c = ReadContent(sp, e.Rel, out _);
                if (Sha(c) != want.Sha) throw new BuildException($"no-Elden-Ring replay: {root} file {e.Rel} differs from the one the builder expects.");
                parts.Add(c);
            }
            var post = Delta.Decode(Concat(parts), File.ReadAllBytes(Path.Combine(patchDir, e.Patch)));
            if (Sha(post) != e.Post) throw new BuildException($"no-Elden-Ring replay: {e.Rel} came out wrong (internal error).");
            var packed = Pack(e.Rel, post, (DCX.Type)e.Dcx);
            if (Sha(ReadContent(Write(path, packed), e.Rel, out _)) != e.Post) throw new BuildException($"no-Elden-Ring replay: {e.Rel} did not round-trip.");
            n++;
        }
        return n;
    }

    static string Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

/// <summary>
/// Copy/insert delta against a dictionary (the concatenated source files). Matches are found through a sparse rolling-hash index
/// of the dictionary (every 16th position, 32-byte windows) or at the last copy's alignment (8 bytes), and extended in both
/// directions, so literal bytes are essentially the bytes no source has at that place. Format (Brotli-compressed): varint (len &lt;&lt; 1 | 1), varint offset = copy;
/// varint (len &lt;&lt; 1), bytes = literal.
/// </summary>
public static class Delta
{
    const int W = 32, Step = 16, A = 8;
    const ulong B = 1099511628211UL;

    static ulong Hash(byte[] d, long i)
    {
        ulong h = 0;
        for (int j = 0; j < W; j++) h = h * B + d[i + j];
        return h;
    }

    public static byte[] Encode(byte[] dict, byte[] target, out long literal)
    {
        int bits = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(2, dict.LongLength / Step))) + 1, 10, 27);
        var table = new long[1 << bits];
        Array.Fill(table, -1L);
        for (long i = 0; i + W <= dict.LongLength; i += Step)
        {
            var k = (int)(Hash(dict, i) >> (64 - bits));
            if (table[k] < 0) table[k] = i;
        }
        ulong bw = 1;
        for (int j = 0; j < W - 1; j++) bw *= B;

        using var raw = new MemoryStream();
        literal = 0;
        long lit = 0, t = 0;
        // Alignment of the last copy (dict position - target position). The pre-image starts the dictionary, so 0 is the
        // first guess. Re-trying the same alignment with a short window resyncs right after a few changed bytes (shifted
        // offsets in tables, a changed field), so only the bytes that really differ become literals.
        long align = 0;
        void Lit(long end) { if (end > lit) { VarInt(raw, (ulong)(end - lit) << 1); raw.Write(target, (int)lit, (int)(end - lit)); } }
        ulong h = target.Length >= W ? Hash(target, 0) : 0;
        while (t + A <= target.LongLength)
        {
            long p = -1;
            int verified = 0;
            long pa = t + align;
            if (pa >= 0 && pa + A <= dict.LongLength && Eq(dict, pa, target, t, A)) { p = pa; verified = A; }
            else if (t + W <= target.LongLength)
            {
                var c = table[(int)(h >> (64 - bits))];
                if (c >= 0 && Eq(dict, c, target, t, W)) { p = c; verified = W; }
            }
            if (p < 0)
            {
                if (t + W < target.LongLength) h = (h - target[t] * bw) * B + target[t + W];
                t++;
                continue;
            }
            long s = t, q = p;
            while (s > lit && q > 0 && target[s - 1] == dict[q - 1]) { s--; q--; }
            long e = t + verified, r = p + verified;
            while (e < target.LongLength && r < dict.LongLength && target[e] == dict[r]) { e++; r++; }
            literal += s - lit;
            Lit(s);
            VarInt(raw, (ulong)(e - s) << 1 | 1);
            VarInt(raw, (ulong)q);
            lit = t = e; align = r - e;
            if (t + W <= target.LongLength) h = Hash(target, t);
        }
        literal += target.LongLength - lit;
        Lit(target.LongLength);
        using var outp = new MemoryStream();
        using (var br = new BrotliStream(outp, CompressionLevel.SmallestSize, true)) raw.WriteTo(br);
        return outp.ToArray();
    }

    public static byte[] Decode(byte[] dict, byte[] patch)
    {
        using var raw = new MemoryStream();
        using (var br = new BrotliStream(new MemoryStream(patch), CompressionMode.Decompress)) br.CopyTo(raw);
        var d = raw.ToArray();
        using var outp = new MemoryStream();
        int i = 0;
        while (i < d.Length)
        {
            var tag = ReadVarInt(d, ref i);
            var len = (long)(tag >> 1);
            if ((tag & 1) == 1)
            {
                var off = (long)ReadVarInt(d, ref i);
                if (off < 0 || off + len > dict.LongLength) throw new InvalidDataException("delta copy out of range");
                outp.Write(dict, (int)off, (int)len);
            }
            else
            {
                if (i + len > d.Length) throw new InvalidDataException("delta literal out of range");
                outp.Write(d, i, (int)len);
                i += (int)len;
            }
        }
        return outp.ToArray();
    }

    static bool Eq(byte[] a, long ai, byte[] b, long bi, int n) => a.AsSpan((int)ai, n).SequenceEqual(b.AsSpan((int)bi, n));

    static void VarInt(Stream s, ulong v)
    {
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    static ulong ReadVarInt(byte[] d, ref int i)
    {
        ulong v = 0; int shift = 0;
        while (true)
        {
            var b = d[i++];
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return v;
            shift += 7;
        }
    }
}
