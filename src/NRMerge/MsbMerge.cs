using SoulsFormats;
using System.Collections;
using System.Reflection;

namespace NRMerge;

/// <summary>Stage (Task 8): per-entry three-way merge of MSB_NR maps, property-level merge for entries both mods changed.</summary>
public static class MsbMerge
{
    /// <summary>
    /// Precedence for a part property both mods changed: EV owns what spawns (model, NPC/think/chara-init params, SpEffect sets);
    /// MMV owns placement and everything else (spec §5 "MSB placement fields → MMV", §9 "EV's model swap").
    /// </summary>
    public static Side PartWinner(string path)
    {
        var leaf = path.Split('.')[0].Split('[')[0];
        return leaf is "ModelName" or "NpcParamId" or "NpcThinkParamId" or "CharaInitParamId" or "SpEffectSetParamIds"
            or "CondemnedSpEffectSetParamIds" or "BackupEventAnimID" ? Side.EV : Side.MMV;
    }

    /// <summary>Part fields that together decide what spawns; they are never mixed between the two mods.</summary>
    public static readonly string[] SpawnIdentity = { "ModelName", "NpcParamId", "NpcThinkParamId", "CharaInitParamId", "SpEffectSetParamIds", "CondemnedSpEffectSetParamIds" };

    static object GetPath(object o, string path)
    {
        foreach (var n in path.Split('.')) { if (o == null) return null; var p = o.GetType().GetProperty(n); if (p == null) return null; o = p.GetValue(o); }
        return o;
    }

    static void SetPath(object o, string path, object v)
    {
        var parts = path.Split('.');
        foreach (var n in parts[..^1]) o = o?.GetType().GetProperty(n)?.GetValue(o);
        o?.GetType().GetProperty(parts[^1])?.SetValue(o, v);
    }

    /// <summary>Top-level properties whose value differs between two versions of an entry.</summary>
    static List<string> ChangedProps(object b, object x) =>
        x.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => b == null || ObjMerge.Sig(p.GetValue(b)) != ObjMerge.Sig(p.GetValue(x))).Select(p => p.Name).ToList();

    static IEnumerable<PropertyInfo> ListProps(object param) =>
        param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>));

    static string NameOf(object o) => ((MSB_NR.Entry)o).Name;

    static uint EntityId(MSB_NR.Part p) =>
        p.GetType().GetProperty("EntityData")?.GetValue(p) is MSB_NR.Part.EntityStruct es ? es.EntityID : 0;

    /// <summary>Replaces every string property (deep, incl. string arrays) equal to <paramref name="from"/> with <paramref name="to"/>.</summary>
    static void ReplaceNames(object o, string from, string to, int depth = 0)
    {
        if (o == null || depth > 6) return;
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
            var t = p.PropertyType;
            if (t == typeof(string)) { if (p.CanWrite && (string)p.GetValue(o) == from) p.SetValue(o, to); }
            else if (t == typeof(string[])) { if (p.GetValue(o) is string[] a) for (int i = 0; i < a.Length; i++) if (a[i] == from) a[i] = to; }
            else if (t.IsClass && !t.IsArray && t.Namespace?.StartsWith("SoulsFormats") == true) ReplaceNames(p.GetValue(o), from, to, depth + 1);
        }
    }

    /// <summary>
    /// A part that one side renamed but that keeps its base entity ID is the same part: it is renamed back to the base name
    /// (and every name reference in that side's map follows) so the entries are matched instead of duplicated.
    /// </summary>
    static void UndoRenames(MSB_NR b, MSB_NR side, string sideName, string rel)
    {
        var sideNames = side.Parts.GetEntries().Select(p => p.Name).ToHashSet();
        var baseNames = b.Parts.GetEntries().Select(p => p.Name).ToHashSet();
        var byEntity = b.Parts.GetEntries().Where(p => EntityId(p) != 0 && !sideNames.Contains(p.Name))
            .GroupBy(EntityId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var all = side.Models.GetEntries().Cast<object>().Concat(side.Events.GetEntries()).Concat(side.Regions.GetEntries())
            .Concat(side.Routes.GetEntries()).Concat(side.Parts.GetEntries()).ToList();
        foreach (var p in side.Parts.GetEntries().ToList())
        {
            if (baseNames.Contains(p.Name) || !byEntity.TryGetValue(EntityId(p), out var bp) || bp.GetType() != p.GetType()) continue;
            if (side.Parts.GetEntries().Count(x => EntityId(x) == EntityId(p)) != 1) continue;
            var old = p.Name;
            foreach (var x in all) ReplaceNames(x, old, bp.Name);
            byEntity.Remove(EntityId(p));
            Journal.Add("maps", $"{rel} part {bp.Name}", $"{sideName} renamed it to {old} (same entity {EntityId(p)}): matched as the same part, base name kept");
        }
    }

    public static MSB_NR MergeMsb(MSB_NR b, MSB_NR e, MSB_NR m, string rel)
    {
        UndoRenames(b, e, "EV", rel);
        UndoRenames(b, m, "MMV", rel);
        foreach (var pp in new[] { "Models", "Events", "Regions", "Routes", "Parts" })
        {
            var prop = typeof(MSB_NR).GetProperty(pp);
            object pb = prop.GetValue(b), pe = prop.GetValue(e), pm = prop.GetValue(m);
            foreach (var lp in ListProps(pe))
            {
                List<object> L(object param) => ((IList)lp.GetValue(param))?.Cast<object>().ToList() ?? new List<object>();
                var lb = L(pb); var le = L(pe); var lm = L(pm);
                var db = lb.ToDictionary(NameOf); var de = le.ToDictionary(NameOf); var dm = lm.ToDictionary(NameOf);
                var keys = Seq3.Merge(lb.Select(NameOf).ToList(), le.Select(NameOf).ToList(), lm.Select(NameOf).ToList(), k => k, out var cs);
                foreach (var c in cs) Journal.Add("maps", $"{rel} {pp}.{lp.Name} order", $"both inserted entries at the same spot: EV's [{string.Join(",", c.A)}] then MMV's [{string.Join(",", c.B)}]");

                // A deletion by one side does not remove an entry the other side changed.
                foreach (var k in db.Keys)
                {
                    if (keys.Contains(k)) continue;
                    bool inE = de.TryGetValue(k, out var xe), inM = dm.TryGetValue(k, out var xm);
                    var keeper = inE && !inM && ObjMerge.Sig(xe) != ObjMerge.Sig(db[k]) ? (le, "EV changed, MMV deleted: kept EV's")
                        : inM && !inE && ObjMerge.Sig(xm) != ObjMerge.Sig(db[k]) ? (lm, "MMV changed, EV deleted: kept MMV's") : default;
                    if (keeper.Item1 == null)
                    {
                        Journal.Add("maps", $"{rel} {pp}.{lp.Name} {k}", inE ? "deleted by MMV (EV unchanged)" : inM ? "deleted by EV (MMV unchanged)" : "deleted by both");
                        continue;
                    }
                    var src = keeper.Item1.Select(NameOf).ToList();
                    int at = 0;
                    for (int i = src.IndexOf(k) - 1; i >= 0; i--) { int q = keys.IndexOf(src[i]); if (q >= 0) { at = q + 1; break; } }
                    keys.Insert(at, k);
                    Journal.Add("maps", $"{rel} {pp}.{lp.Name} {k}", keeper.Item2);
                }

                var outList = (IList)lp.GetValue(pe);
                if (outList == null) { outList = (IList)Activator.CreateInstance(lp.PropertyType); lp.SetValue(pe, outList); }
                var merged = new List<object>();
                foreach (var k in keys)
                {
                    db.TryGetValue(k, out var xb); de.TryGetValue(k, out var xe); dm.TryGetValue(k, out var xm);
                    if (xe == null) { merged.Add(xm); if (xb == null) Journal.Add("maps", $"{rel} {pp}.{lp.Name} {k}", "added by MMV"); else if (ObjMerge.Sig(xm) != ObjMerge.Sig(xb)) Journal.Add("maps", $"{rel} {pp}.{lp.Name} {k}", "MMV changed, EV deleted: kept MMV's"); continue; }
                    if (xm == null) { merged.Add(xe); continue; }
                    string sb = ObjMerge.Sig(xb), se = ObjMerge.Sig(xe), sm = ObjMerge.Sig(xm);
                    if (se == sm || sm == sb) { merged.Add(xe); continue; }
                    if (se == sb) { merged.Add(xm); continue; }
                    var conf = new List<string>();
                    Func<string, Side> win = pp == "Parts" ? PartWinner : _ => Side.MMV;
                    var changedE = ChangedProps(xb, xe); var changedM = ChangedProps(xb, xm);
                    var idE = SpawnIdentity.Select(f => GetPath(xe, f)).ToList();
                    bool idBoth = pp == "Parts" && SpawnIdentity.Any(f => ObjMerge.Sig(GetPath(xb, f)) != ObjMerge.Sig(GetPath(xe, f)))
                                               && SpawnIdentity.Any(f => ObjMerge.Sig(GetPath(xb, f)) != ObjMerge.Sig(GetPath(xm, f)));
                    var r = ObjMerge.Merge(xb, xe, xm, win, conf);
                    if (idBoth)
                    {
                        for (int i = 0; i < SpawnIdentity.Length; i++) SetPath(r, SpawnIdentity[i], idE[i]);
                        conf.Add($"spawn identity ({string.Join(",", SpawnIdentity)}) changed by both: EV's set kept whole");
                    }
                    merged.Add(r);
                    Journal.Add("maps", $"{rel} {pp}.{lp.Name} {k}", $"both changed (EV: {string.Join(",", changedE)}; MMV: {string.Join(",", changedM)}): property merge"
                        + (conf.Count == 0 ? ", no overlapping property" : "; " + string.Join(" | ", conf)));
                }
                outList.Clear();
                foreach (var x in merged) outList.Add(x);
            }
        }

        // Every model a part uses must be listed.
        var have = e.Models.GetEntries().Select(x => x.Name).ToHashSet();
        foreach (var part in e.Parts.GetEntries())
        {
            if (string.IsNullOrEmpty(part.ModelName) || have.Contains(part.ModelName)) continue;
            var src = m.Models.GetEntries().FirstOrDefault(x => x.Name == part.ModelName) ?? b.Models.GetEntries().FirstOrDefault(x => x.Name == part.ModelName);
            if (src == null) throw new InvalidDataException($"{rel}: part {part.Name} uses model {part.ModelName} that no input lists");
            e.Models.Add(src);
            have.Add(src.Name);
            Journal.Add("maps", $"{rel} model {src.Name}", $"added to model list (used by {part.Name})");
        }
        return e;
    }

    static void MergeFile(string rel)
    {
        var basePath = Paths.BaseOf(rel);
        var evRaw = File.ReadAllBytes(Paths.In(Paths.EV, rel));
        var e = MSB_NR.Read(Dcx.Decompress(evRaw, out var type));
        var b = MSB_NR.Read(Dcx.Decompress(File.ReadAllBytes(basePath)));
        var m = MSB_NR.Read(Dcx.Decompress(File.ReadAllBytes(MmvRewrite.MmvInput(rel))));
        var r = MergeMsb(b, e, m, rel);
        var outPath = Paths.In(Paths.OutMod, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, r.Write(type).ToArray());
        var check = MSB_NR.Read(Dcx.Decompress(File.ReadAllBytes(outPath)));
        foreach (var g in check.Parts.GetEntries().Where(p => EntityId(p) != 0).GroupBy(EntityId).Where(g => g.Count() > 1))
            Console.WriteLine($"  NOTE duplicate entity {g.Key}: {string.Join(", ", g.Select(p => p.Name))}");
        foreach (var g in check.Models.GetEntries().GroupBy(x => x.Name).Where(g => g.Count() > 1))
            Console.WriteLine($"  NOTE duplicate model {g.Key}");
        Console.WriteLine($"{rel}: parts={check.Parts.GetEntries().Count} (EV {e.Parts.GetEntries().Count}) models={check.Models.GetEntries().Count} events={check.Events.GetEntries().Count} regions={check.Regions.GetEntries().Count}");
    }

    public static int Run()
    {
        Journal.Clear();
        foreach (var line in File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1))
        {
            var p = line.Split('\t');
            if (p[0] == "merge" && p[1].EndsWith(".msb.dcx")) MergeFile(p[1]);
        }
        Journal.Save();
        return 0;
    }
}
