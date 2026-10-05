using SoulsFormats;

namespace NRMerge;

/// <summary>Analysis helper: three-way node-level summary of the behavior HKX entries of a behbnd, plus a round-trip check.</summary>
public static class BehProbe
{
    public static Dictionary<string, byte[]> Entries(string path)
    {
        if (path == null || !File.Exists(path)) return new();
        var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(path)));
        return bnd.Files.GroupBy(f => EntryKey(f.Name)).ToDictionary(g => g.Key, g => g.First().Bytes.ToArray());
    }

    /// <summary>Binder entry name without the build-machine root (ER "n:\gr\data\…" vs NR "w:\cl\data	arget\…").</summary>
    public static string EntryKey(string name)
    {
        const string marker = @"\interroot_win64\";
        var n = name.ToLowerInvariant().Replace('/', '\\');
        int i = n.LastIndexOf(marker, StringComparison.Ordinal);
        if (i >= 0) n = n[(i + marker.Length)..];
        const string model = @"model\";
        int j = n.StartsWith(model) ? 0 : n.LastIndexOf(@"\" + model, StringComparison.Ordinal) + 1;
        return j > 0 || n.StartsWith(model) ? n[(j + model.Length)..] : n;
    }

    public static int Run(string rel)
    {
        var b = Entries(Paths.BaseOf(rel)); var e = Entries(Paths.In(Paths.EV, rel)); var m = Entries(MmvRewrite.MmvInput(rel));
        foreach (var k in b.Keys.Union(e.Keys).Union(m.Keys).OrderBy(x => x))
        {
            b.TryGetValue(k, out var xb); e.TryGetValue(k, out var xe); m.TryGetValue(k, out var xm);
            bool same(byte[] x, byte[] y) => x != null && y != null && x.AsSpan().SequenceEqual(y);
            if (same(xe, xm) || (same(xb, xe) && xm == null)) continue;
            Console.WriteLine($"{k}: base={xb?.Length} ev={xe?.Length} mmv={xm?.Length} evIsBase={same(xb, xe)} mmvIsBase={same(xb, xm)}");
            if (!k.EndsWith(".hkx") || xe == null || xm == null || xb == null) continue;
            try
            {
                var hb = BehGraph.Load(xb); var he = BehGraph.Load(xe); var hm = BehGraph.Load(xm);
                var gb = hb.Root; var ge = he.Root; var gm = hm.Root;
                Console.WriteLine($"  nightreign types: base={hb.Nightreign} ev={he.Nightreign} mmv={hm.Nightreign}");
                var nb = BehGraph.NamedNodes(gb); var ne = BehGraph.NamedNodes(ge); var nm = BehGraph.NamedNodes(gm);
                var sb = nb.ToDictionary(x => x.Key, x => BehGraph.Sig(x.Value)); var se = ne.ToDictionary(x => x.Key, x => BehGraph.Sig(x.Value)); var sm = nm.ToDictionary(x => x.Key, x => BehGraph.Sig(x.Value));
                string Cls(Dictionary<string, string> s) => $"added={s.Keys.Count(x => !sb.ContainsKey(x))} removed={sb.Keys.Count(x => !s.ContainsKey(x))} changed={s.Count(x => sb.TryGetValue(x.Key, out var v) && v != x.Value)}";
                Console.WriteLine($"  nodes base={nb.Count} EV {Cls(se)} | MMV {Cls(sm)}");
                var both = sb.Keys.Where(x => se.TryGetValue(x, out var a) && sm.TryGetValue(x, out var c) && a != sb[x] && c != sb[x] && a != c).ToList();
                if (Environment.GetEnvironmentVariable("BEHPROBE_LIST") == "1")
                {
                    Console.WriteLine("  MMV added: " + string.Join(", ", sm.Keys.Where(x => !sb.ContainsKey(x))));
                    Console.WriteLine("  MMV changed: " + string.Join(", ", sm.Keys.Where(x => sb.TryGetValue(x, out var v) && v != sm[x])));
                    Console.WriteLine("  EV removed: " + string.Join(", ", sb.Keys.Where(x => !se.ContainsKey(x))));
                    Console.WriteLine("  EV added: " + string.Join(", ", se.Keys.Where(x => !sb.ContainsKey(x))));
                    var dbn = BehGraph.Graph(gb).m_data.m_stringData; var den = BehGraph.Graph(ge).m_data.m_stringData;
                    Console.WriteLine("  events base-not-EV: " + string.Join(", ", dbn.m_eventNames.Except(den.m_eventNames)));
                    Console.WriteLine("  vars base-not-EV: " + string.Join(", ", dbn.m_variableNames.Except(den.m_variableNames)));
                }
                Console.WriteLine($"  both changed differently: {both.Count} {string.Join(", ", both.Take(15))}");
                var addBoth = se.Keys.Where(x => !sb.ContainsKey(x) && sm.ContainsKey(x)).ToList();
                Console.WriteLine($"  added by both: {addBoth.Count} (same {addBoth.Count(x => se[x] == sm[x])}) {string.Join(", ", addBoth.Where(x => se[x] != sm[x]).Take(10))}");
                var db = BehGraph.Graph(gb).m_data.m_stringData; var de = BehGraph.Graph(ge).m_data.m_stringData; var dm = BehGraph.Graph(gm).m_data.m_stringData;
                Console.WriteLine($"  events base={db.m_eventNames.Count} ev={de.m_eventNames.Count} mmv={dm.m_eventNames.Count} vars base={db.m_variableNames.Count} ev={de.m_variableNames.Count} mmv={dm.m_variableNames.Count}");
                Console.WriteLine($"  EV new events [{string.Join(",", de.m_eventNames.Skip(db.m_eventNames.Count))}] MMV new events [{string.Join(",", dm.m_eventNames.Skip(db.m_eventNames.Count))}] EV new vars [{string.Join(",", de.m_variableNames.Skip(db.m_variableNames.Count))}] MMV new vars [{string.Join(",", dm.m_variableNames.Skip(db.m_variableNames.Count))}]");
                foreach (var (nm2, g) in new[] { ("base", gb), ("EV", ge), ("MMV", gm) })
                {
                    var dups = BehGraph.AllObjects(g).Where(BehGraph.IsNamedNode).GroupBy(BehGraph.KeyOf).Where(x => x.Count() > 1).ToList();
                    Console.WriteLine($"  {nm2} duplicate node keys: {dups.Count} {string.Join(", ", dups.Take(5).Select(x => x.Key + "x" + x.Count()))}");
                }
                Console.WriteLine($"  EV events are base-prefix: {db.m_eventNames.SequenceEqual(de.m_eventNames.Take(db.m_eventNames.Count))}; MMV: {db.m_eventNames.SequenceEqual(dm.m_eventNames.Take(db.m_eventNames.Count))}");
                var saved = BehGraph.Save(he);
                Console.WriteLine($"  EV re-save byte-identical: {saved.AsSpan().SequenceEqual(xe)} ({saved.Length} vs {xe.Length})");
                var rt = BehGraph.Read(saved);
                var nr = BehGraph.NamedNodes(rt);
                Console.WriteLine($"  EV round-trip: nodes {nr.Count}/{ne.Count}, sig-equal {ne.All(x => nr.TryGetValue(x.Key, out var y) && BehGraph.Sig(y) == se[x.Key])}");
            }
            catch (Exception ex) { Console.WriteLine("  ERR " + ex.GetType().Name + ": " + ex.Message); }
        }
        return 0;
    }
}

/// <summary>Analysis helper: runs the behavior merge on one behbnd's behavior entry and checks node coverage of the result.</summary>
public static class BehTest
{
    public static int Run(string rel)
    {
        var b = BehProbe.Entries(Paths.BaseOf(rel)); var e = BehProbe.Entries(Paths.In(Paths.EV, rel)); var m = BehProbe.Entries(MmvRewrite.MmvInput(rel));
        foreach (var k in e.Keys.Where(k => k.Contains(@"\behaviors\") && m.ContainsKey(k) && b.ContainsKey(k) && !e[k].AsSpan().SequenceEqual(m[k])))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var outBytes = BehaviorMerge.Merge(b[k], e[k], m[k], out var notes);
            var r = BehGraph.Read(outBytes);
            var kr = BehGraph.AllObjects(r).Where(BehGraph.IsNamedNode).Select(BehGraph.KeyOf).ToHashSet();
            var kb = BehGraph.NamedNodes(BehGraph.Read(b[k])).Keys.ToHashSet();
            var ke = BehGraph.NamedNodes(BehGraph.Read(e[k])).Keys.ToHashSet();
            var km = BehGraph.AllObjects(BehGraph.Read(m[k])).Where(BehGraph.IsNamedNode).Select(BehGraph.KeyOf).ToHashSet();
            var lostE = ke.Where(x => !kr.Contains(x)).ToList();
            var lostM = km.Where(x => !kb.Contains(x) && !kr.Contains(x)).ToList();
            Console.WriteLine($"{k}: {outBytes.Length} bytes in {sw.Elapsed.TotalSeconds:F1}s; result nodes {kr.Count}; EV nodes lost {lostE.Count} {string.Join(",", lostE.Take(8))}; MMV-added lost {lostM.Count} {string.Join(",", lostM.Take(8))}");
            foreach (var (nm, g) in new[] { ("base", BehGraph.Read(b[k])), ("EV", BehGraph.Read(e[k])), ("MMV", BehGraph.Read(m[k])), ("merged", r) })
            {
                var ci = BehIdCheck.Run(g);
                Console.WriteLine($"  animation id check {nm}: ids shared by different animations {ci.sharedIds}, animations with several ids {ci.multiIdNames}");
            }
            foreach (var n in notes.Take(40)) Console.WriteLine("  " + n);
            if (notes.Count > 40) Console.WriteLine($"  … {notes.Count - 40} more");
        }
        return 0;
    }
}

/// <summary>Analysis helper: animation-name tables and clip internal-id ranges of a behbnd's behavior and character files.</summary>
public static class BehTables
{
    public static int Run(string path)
    {
        var bnd = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(path)));
        foreach (var f in bnd.Files)
        {
            Console.WriteLine($"{f.Name} {f.Bytes.Length}");
            if (!f.Name.EndsWith(".hkx")) continue;
            try
            {
                var root = BehGraph.Read(f.Bytes.ToArray());
                foreach (var v in root.m_namedVariants) Console.WriteLine($"   variant {v.m_className} {v.m_name}");
                var g = BehGraph.Graph(root);
                if (g != null)
                {
                    var clips = BehGraph.AllObjects(root).OfType<HKLib.hk2018.hkbClipGenerator>().ToList();
                    Console.WriteLine($"   behavior anim names {g.m_data.m_stringData.m_animationNames.Count}; clips {clips.Count}; internalId max {clips.Max(c => c.m_animationInternalId)}; distinct anim names {clips.Select(c => c.m_animationName).Distinct().Count()}");
                }
                foreach (var cd in BehGraph.AllObjects(root).OfType<HKLib.hk2018.hkbCharacterStringData>())
                    Console.WriteLine($"   character bundles {cd.m_animationBundleNameData?.Count} files {cd.m_animationBundleFilenameData?.Count}");
            }
            catch (Exception ex) { Console.WriteLine("   ERR " + ex.Message); }
        }
        return 0;
    }
}

/// <summary>Analysis helper: writes a binder's entries (base/EV/MMV) to a folder, one subfolder per side.</summary>
public static class BndDump
{
    public static int Run(string rel, string outDir)
    {
        foreach (var (side, path) in new[] { ("base", Paths.BaseOf(rel)), ("ev", Paths.In(Paths.EV, rel)), ("mmv", MmvRewrite.MmvInput(rel)) })
        {
            if (path == null || !File.Exists(path)) continue;
            var bnd = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(path)));
            var dir = Path.Combine(outDir, side);
            Directory.CreateDirectory(dir);
            foreach (var f in bnd.Files) File.WriteAllBytes(Path.Combine(dir, BndDiff3.Norm(f.Name)), f.Bytes.ToArray());
        }
        return 0;
    }
}

/// <summary>Analysis helper: structural summary of FLVER2 files (dummies, bones, materials, meshes).</summary>
public static class FlverInfo
{
    public static int Run(string[] files)
    {
        foreach (var p in files)
        {
            var f = SoulsFormats.FLVER2.Read(File.ReadAllBytes(p));
            Console.WriteLine($"{p}: dummies={f.Dummies.Count} bones={f.Nodes.Count} materials={f.Materials.Count} meshes={f.Meshes.Count} verts={f.Meshes.Sum(m => m.Vertices.Count)} bufferLayouts={f.BufferLayouts.Count} gx={f.GXLists.Count}");
            Console.WriteLine("   mats: " + string.Join(" | ", f.Materials.Select(m => $"{m.Name}:{Path.GetFileName(m.MTD)}")));
            Console.WriteLine("   dummyRefs: " + string.Join(",", f.Dummies.GroupBy(d => d.ReferenceID).Select(g => $"{g.Key}x{g.Count()}")));
            Console.WriteLine("   meshes: " + string.Join(" | ", f.Meshes.Select(m => $"mat{m.MaterialIndex}/v{m.Vertices.Count}/fs{m.FaceSets.Count}")));
            string H(object o) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(ObjMerge.Sig(o))))[..10];
            Console.WriteLine($"   sig header={H(f.Header)} dummies={H(f.Dummies)} nodes={H(f.Nodes)} materials={H(f.Materials)} gx={H(f.GXLists)} layouts={H(f.BufferLayouts)} meshVerts={H(f.Meshes.Select(m => m.Vertices.Select(v => (v.Position, v.Normal)).ToList()).ToList())} faces={H(f.Meshes.Select(m => m.FaceSets.Select(fs => fs.Indices).ToList()).ToList())} meshMeta={H(f.Meshes.Select(m => (m.MaterialIndex, m.NodeIndex, m.Dynamic, m.BoneIndices, m.BoundingBox)).ToList())}");
        }
        return 0;
    }
}

/// <summary>Analysis helper: where animation names are used (behavior clip generators, TAE animations) in EV and MMV.</summary>
public static class AnimUsage
{
    public static int Run(string[] names)
    {
        foreach (var (side, root) in new[] { ("EV", Paths.EV), ("MMV", Paths.MMV), ("base", Paths.VanillaNR) })
        {
            var beh = BehProbe.Entries(Paths.In(root, "chr/c0000.behbnd.dcx")).First(k => k.Key.Contains(@"\behaviors\")).Value;
            var clips = BehGraph.AllObjects(BehGraph.Read(beh)).OfType<HKLib.hk2018.hkbClipGenerator>().ToList();
            var ani = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(Paths.In(root, "chr/c0000.anibnd.dcx"))));
            var taes = ani.Files.Where(f => f.Name.EndsWith(".tae")).Select(f => (BndDiff3.Norm(f.Name), SoulsFormats.TAE.Read(f.Bytes.ToArray()))).ToList();
            foreach (var n in names)
            {
                int clipsN = clips.Count(c => string.Equals(c.m_animationName, n, StringComparison.OrdinalIgnoreCase));
                var cat = int.Parse(n.Substring(1, 3)); var id = long.Parse(n.Substring(5));
                var own = taes.FirstOrDefault(t => t.Item1 == $"a{cat:000}.tae").Item2?.Animations.FirstOrDefault(a => a.ID == id);
                var inTae = own == null ? new List<string>() : new List<string> { $"a{cat:000}.tae {TaeSig.MiniHeader(own)} file={own.AnimFileName} events={own.Events.Count}" };
                long full = cat * 1000000L + id;
                var importers = taes.SelectMany(t => t.Item2.Animations.Where(a =>
                    (a.MiniHeader is SoulsFormats.TAE.Animation.AnimMiniHeader.ImportOtherAnim io && io.ImportFromAnimID == full) ||
                    (a.MiniHeader is SoulsFormats.TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX && st.ImportHKXSourceAnimID == full && !(t.Item1 == $"a{cat:000}.tae" && a.ID == id)))
                    .Select(a => $"{t.Item1}:{a.ID}")).ToList();
                Console.WriteLine($"{side}\t{n}\tclips={clipsN}\ttae={string.Join(",", inTae)}\timportedBy={string.Join(",", importers.Take(5))}");
            }
        }
        return 0;
    }
}

/// <summary>Analysis helper: the selectors (CMSG) that contain clips with the given animation names, per side.</summary>
public static class ClipParents
{
    public static int Run(string[] names)
    {
        foreach (var (side, root) in new[] { ("EV", Paths.EV), ("MMV", Paths.MMV) })
        {
            var beh = BehProbe.Entries(Paths.In(root, "chr/c0000.behbnd.dcx")).First(k => k.Key.Contains(@"\behaviors\")).Value;
            var all = BehGraph.AllObjects(BehGraph.Read(beh));
            foreach (var c in all.OfType<HKLib.hk2018.CustomManualSelectorGenerator>())
            {
                var hit = c.m_generators.OfType<HKLib.hk2018.hkbClipGenerator>().Where(g => names.Contains(g.m_animationName)).ToList();
                if (hit.Count == 0) continue;
                Console.WriteLine($"{side} {c.m_name} offsetType={c.m_offsetType} animId={c.m_animId} hits={string.Join(",", hit.Select(h => h.m_animationName))} siblings={string.Join(",", c.m_generators.OfType<HKLib.hk2018.hkbClipGenerator>().Select(g => g.m_animationName).Take(12))}{(c.m_generators.Count > 12 ? $" …({c.m_generators.Count})" : "")}");
            }
        }
        return 0;
    }
}

/// <summary>Analysis helper: "Invoke PC Behavior" (TAE event 307) targets per mod, as PC-type*1e8 + judge.</summary>
public static class TaePcBehavior
{
    public static Dictionary<long, List<string>> Collect(string anibnd)
    {
        var d = new Dictionary<long, List<string>>();
        var bnd = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(anibnd)));
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae")))
            foreach (var a in SoulsFormats.TAE.Read(f.Bytes.ToArray()).Animations)
                foreach (var e in a.Events.Where(e => e.Type == 307))
                {
                    var p = e.GetParameterBytes(false);
                    long id = BitConverter.ToInt32(p, 4) * 100000000L + BitConverter.ToInt32(p, 8);
                    if (!d.TryGetValue(id, out var l)) d[id] = l = new();
                    l.Add($"{BndDiff3.Norm(f.Name)}:{a.ID}");
                }
        return d;
    }

    public static int Run(long min, long max)
    {
        foreach (var (side, root) in new[] { ("EV", Paths.EV), ("MMV", Paths.MMV), ("base", Paths.VanillaNR) })
        {
            var d = Collect(Paths.In(root, "chr/c0000.anibnd.dcx"));
            var hits = d.Where(x => x.Key >= min && x.Key <= max).OrderBy(x => x.Key).ToList();
            Console.WriteLine($"{side}: {hits.Count} ids: " + string.Join(" ", hits.Select(x => $"{x.Key}(x{x.Value.Count})")));
        }
        return 0;
    }
}

/// <summary>Analysis helper: behavior-judge values of event types 1/2/5/304/307 in one TAE of c0000.anibnd.</summary>
public static class TaeJudges
{
    public static int Run(string anibnd, string taeName)
    {
        var bnd = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(anibnd)));
        var f = bnd.Files.First(x => BndDiff3.Norm(x.Name) == taeName);
        var c = new SortedDictionary<string, int>();
        foreach (var a in SoulsFormats.TAE.Read(f.Bytes.ToArray()).Animations)
            foreach (var e in a.Events.Where(e => e.Type is 1 or 2 or 5 or 304 or 307))
            {
                var p = e.GetParameterBytes(false);
                int judge = e.Type switch { 1 => BitConverter.ToInt32(p, 8), 2 => BitConverter.ToInt32(p, 8), 5 => BitConverter.ToInt32(p, 4), 304 => BitConverter.ToInt32(p, 4), _ => BitConverter.ToInt32(p, 8) };
                var k = $"type{e.Type} judge{judge}";
                c[k] = c.GetValueOrDefault(k) + 1;
            }
        Console.WriteLine(string.Join(" ", c.Select(x => $"{x.Key}(x{x.Value})")));
        return 0;
    }
}

/// <summary>Analysis helper: TAE files/event types that use given behavior-judge values (types 1/2/5/304/307).</summary>
public static class JudgeUsers
{
    public static int Run(string anibnd, int[] judges)
    {
        var bnd = SoulsFormats.BND4.Read(Dcx.Decompress(File.ReadAllBytes(anibnd)));
        var c = new SortedDictionary<string, int>();
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tae")))
            foreach (var a in SoulsFormats.TAE.Read(f.Bytes.ToArray()).Animations)
                foreach (var e in a.Events.Where(e => e.Type is 1 or 2 or 5 or 304 or 307))
                {
                    var p = e.GetParameterBytes(false);
                    int judge = e.Type is 5 or 304 ? BitConverter.ToInt32(p, 4) : BitConverter.ToInt32(p, 8);
                    if (!judges.Contains(judge)) continue;
                    var k = $"{BndDiff3.Norm(f.Name)} t{e.Type} j{judge}" + (e.Type == 307 ? $" pc{BitConverter.ToInt32(p, 4)}" : "");
                    c[k] = c.GetValueOrDefault(k) + 1;
                }
        foreach (var x in c) Console.WriteLine($"{x.Key} x{x.Value}");
        return 0;
    }
}
