using SoulsFormats;
using System.Security.Cryptography;

namespace NRMerge;

/// <summary>Entry-level three-way merge of BND4 binders and TPFs (spec §9), starting from EV's file.</summary>
public static class BinderMerge
{
    static string Key(string name) => BndDiff3.Norm(name ?? "");
    static string Hash(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA1.HashData(b));

    public sealed class Result
    {
        public int TakenMmv, AddedMmv, DeletedMmv, Conflicts, Merged;
        public List<string> ConflictKeys = new();
        public override string ToString() => $"mmv-changes={TakenMmv} mmv-added={AddedMmv} mmv-deleted={DeletedMmv} merged-inside={Merged} conflicts={Conflicts}";
    }

    static Dictionary<string, byte[]> Entries(object binder, Func<string, string> Key)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (binder is BND4 b) foreach (var f in b.Files) d.TryAdd(Key(f.Name), f.Bytes.ToArray());
        else if (binder is TPF t) foreach (var x in t.Textures) d.TryAdd(Key(x.Name), x.Bytes.ToArray());
        return d;
    }

    static object Read(string path, out DCX.Type type)
    {
        type = DCX.Type.None;
        if (path == null || !File.Exists(path)) return null;
        var data = Dcx.Decompress(File.ReadAllBytes(path), out type);
        if (BND4.Is(data)) return BND4.Read(data);
        if (TPF.Is(data)) return TPF.Read(data);
        throw new InvalidDataException("unsupported binder " + path);
    }

    /// <param name="ownerOf">Who wins an entry both mods changed differently (default EV).</param>
    /// <param name="mergeEntry">Optional sub-file merge for an entry both mods changed: (key, base, ev, mmv) → merged bytes, or null to use <paramref name="ownerOf"/>.</param>
    /// <param name="fullPathKeys">Match entries by their path below interroot (not just the file name).</param>
    public static Result Merge(string basePath, string evPath, string mmvPath, string outPath, Func<string, Side> ownerOf, string label,
        Func<string, byte[], byte[], byte[], byte[]> mergeEntry = null, bool fullPathKeys = false, string area = "binders")
    {
        Func<string, string> Key = fullPathKeys ? BehProbe.EntryKey : BinderMerge.Key;
        var res = new Result();
        var ev = Read(evPath, out var type);
        var mmv = Read(mmvPath, out _);
        var bas = Read(basePath, out _);
        var be = Entries(bas, Key); var me = Entries(mmv, Key); var ee = Entries(ev, Key);

        // MMV source objects by key (to copy names/flags/texture formats)
        var mmvBnd = new Dictionary<string, BinderFile>(StringComparer.Ordinal);
        var mmvTex = new Dictionary<string, TPF.Texture>(StringComparer.Ordinal);
        if (mmv is BND4 mb) foreach (var f in mb.Files) mmvBnd.TryAdd(Key(f.Name), f);
        if (mmv is TPF mt) foreach (var x in mt.Textures) mmvTex.TryAdd(Key(x.Name), x);

        var evIndex = new Dictionary<string, BinderFile>(StringComparer.Ordinal);
        int nextId = 0;
        if (ev is BND4 eb0)
        {
            foreach (var f in eb0.Files) evIndex.TryAdd(Key(f.Name), f);
            nextId = eb0.Files.Count == 0 ? 0 : eb0.Files.Max(f => f.ID) + 1;
        }

        void SetEntry(string key, bool add)
        {
            if (ev is BND4 eb)
            {
                var src = mmvBnd[key];
                if (!add && evIndex.TryGetValue(key, out var existing)) { existing.Bytes = src.Bytes.ToArray(); return; }
                var nf = new BinderFile(src.Flags, nextId++, src.Name, src.Bytes.ToArray()) { CompressionType = src.CompressionType };
                eb.Files.Add(nf);
                evIndex[key] = nf;
            }
            else if (ev is TPF et)
            {
                var src = mmvTex[key];
                int i = et.Textures.FindIndex(x => Key(x.Name) == key);
                if (i >= 0 && !add) et.Textures[i] = src; else et.Textures.Add(src);
            }
        }
        void Remove(string key)
        {
            if (ev is BND4 eb && evIndex.Remove(key, out var gone)) eb.Files.Remove(gone);
            else if (ev is TPF et) et.Textures.RemoveAll(x => Key(x.Name) == key);
        }

        foreach (var key in be.Keys.Union(ee.Keys).Union(me.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            be.TryGetValue(key, out var b); ee.TryGetValue(key, out var e); me.TryGetValue(key, out var m);
            string hb = b == null ? null : Hash(b), he = e == null ? null : Hash(e), hm = m == null ? null : Hash(m);
            if (hm == he) continue;                       // same on both sides (or both absent)
            bool mChanged = hm != hb, eChanged = he != hb;
            if (!mChanged) continue;                      // only EV changed: EV's file already holds it
            if (m == null)                                // MMV deleted
            {
                if (!eChanged) { Remove(key); res.DeletedMmv++; }
                continue;
            }
            if (!eChanged)                                // only MMV changed / added
            {
                SetEntry(key, add: e == null);
                if (e == null) res.AddedMmv++; else res.TakenMmv++;
                continue;
            }
            if ((e == null || e.Length == 0) && m.Length > 0 && key.EndsWith(".tae", StringComparison.OrdinalIgnoreCase))
            {
                // EV emptied or removed an animation set it no longer uses; MMV's weapons/characters still play it
                // (c0000 a281: MMV's Frozen Cold Needle Invader, hero moveset 281)
                SetEntry(key, add: e == null);
                res.TakenMmv++;
                Journal.Add(area, $"{label} :: {key}", $"EV {(e == null ? "removed" : "emptied")} the animation set, MMV filled it ({m.Length} B) -> MMV");
                continue;
            }
            if (mergeEntry != null && e != null)
            {
                var merged = mergeEntry(key, b, e, m);
                if (merged != null)
                {
                    evIndex[key].Bytes = merged;
                    res.Merged++;
                    Journal.Add(area, $"{label} :: {key}", "both changed: merged inside the entry");
                    continue;
                }
            }
            res.Conflicts++;                              // both changed differently
            res.ConflictKeys.Add(key);
            var owner = ownerOf(key);
            Journal.Add(area, $"{label} :: {key}", $"both changed (EV {e?.Length ?? 0} B, MMV {m.Length} B) -> {owner}");
            if (owner == Side.MMV) SetEntry(key, add: e == null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        byte[] bytes = ev switch
        {
            BND4 eb => eb.Write(type).ToArray(),
            TPF et => et.Write(type).ToArray(),
            _ => throw new InvalidOperationException(),
        };
        File.WriteAllBytes(outPath, bytes);
        return res;
    }
}
