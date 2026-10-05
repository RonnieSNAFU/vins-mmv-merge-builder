using HKLib.hk2018;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace NRMerge;

/// <summary>Havok behavior graph access: load/save hk2018 binary HKX, enumerate objects, identify named nodes, signatures.</summary>
public static class BehGraph
{
    /// <summary>Nightreign's CustomManualSelectorGenerator has an extra serialized bool (isBasePoseAnim @198) and another type hash.</summary>
    public const uint NrCmsgHash = 624531992;

    static HavokTypeRegistry _nr;

    /// <summary>HKLib's (Elden Ring) registry with Nightreign's CustomManualSelectorGenerator layout.</summary>
    public static HavokTypeRegistry NrRegistry
    {
        get
        {
            if (_nr != null) return _nr;
            var xml = XElement.Load(Path.Combine(AppContext.BaseDirectory, "Res", "HavokTypeRegistry20180100.xml"));
            var cmsg = xml.Descendants("HavokType").Single(x => (string)x.Attribute("Name") == "CustomManualSelectorGenerator");
            cmsg.SetAttributeValue("Hash", NrCmsgHash.ToString());
            var ride = cmsg.Descendants("Member").Single(m => (string)m.Attribute("Name") == "rideSync");
            var enableTae = cmsg.Descendants("Member").Single(m => (string)m.Attribute("Name") == "enableTae");
            ride.AddAfterSelf(new XElement("Member", new XAttribute("Name", "isBasePoseAnim"), new XAttribute("Type", (string)enableTae.Attribute("Type")),
                new XAttribute("Offset", "198"), new XAttribute("Flags", (string)ride.Attribute("Flags"))));
            var load = typeof(HavokTypeRegistry).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(XElement) });
            return _nr = new HavokTypeRegistry((HavokType[])load.Invoke(null, new object[] { xml }));
        }
    }

    /// <summary>A loaded behavior file and the type registry (ER or NR) it must be written back with.</summary>
    public sealed record Hkx(hkRootLevelContainer Root, bool Nightreign);

    public static Hkx Load(byte[] hkx)
    {
        try { return new Hkx((hkRootLevelContainer)new HavokBinarySerializer().Read(new MemoryStream(hkx)), false); }
        catch (InvalidDataException ex) when (ex.Message.Contains("Incorrect hash"))
        {
            return new Hkx((hkRootLevelContainer)new HavokBinarySerializer(NrRegistry).Read(new MemoryStream(hkx)), true);
        }
    }

    public static byte[] Save(Hkx h)
    {
        var s = h.Nightreign ? new HavokBinarySerializer(NrRegistry) : new HavokBinarySerializer();
        var ms = new MemoryStream();
        s.Write(h.Root, ms);
        return ms.ToArray();
    }

    public static hkRootLevelContainer Read(byte[] hkx) => Load(hkx).Root;

    static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

    public static FieldInfo[] Fields(Type t)
    {
        lock (FieldCache)
        {
            if (!FieldCache.TryGetValue(t, out var f))
                FieldCache[t] = f = t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.MetadataToken).ToArray();
            return f;
        }
    }

    /// <summary>Name of a named graph object (hkbNode, state info, …), or null.</summary>
    public static string NameOf(object o)
    {
        if (o == null) return null;
        var f = o.GetType().GetField("m_name", BindingFlags.Public | BindingFlags.Instance);
        return f?.FieldType == typeof(string) ? (string)f.GetValue(o) : null;
    }

    public static bool IsNamedNode(object o) => o is hkbNode && !string.IsNullOrEmpty(NameOf(o));

    public static string KeyOf(object o) => $"{o.GetType().Name}:{NameOf(o)}";

    /// <summary>Every reachable Havok object (reference identity), in discovery order.</summary>
    public static List<object> AllObjects(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var order = new List<object>();
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            if (o == null || !seen.Add(o)) continue;
            order.Add(o);
            foreach (var child in Children(o).Reverse()) stack.Push(child);
        }
        return order;
    }

    /// <summary>Direct object references held by <paramref name="o"/> (fields, lists, arrays, nested value structs flattened).</summary>
    public static IEnumerable<object> Children(object o)
    {
        foreach (var f in Fields(o.GetType()))
        {
            var v = f.GetValue(o);
            foreach (var c in Refs(v)) yield return c;
        }
    }

    static IEnumerable<object> Refs(object v)
    {
        if (v == null || v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) yield break;
        if (v is IHavokObject) { yield return v; yield break; }
        if (v is IEnumerable en && v is not string)
        {
            foreach (var x in en) foreach (var r in Refs(x)) yield return r;
            yield break;
        }
        if (v.GetType().IsValueType)
            foreach (var f in Fields(v.GetType())) foreach (var r in Refs(f.GetValue(v))) yield return r;
    }

    /// <summary>
    /// Signature of an object's own content: references to named nodes appear as their key, unnamed objects are inlined.
    /// </summary>
    public static string Sig(object o)
    {
        var sb = new StringBuilder();
        Append(sb, o, true, 0);
        return sb.ToString();
    }

    static void Append(StringBuilder sb, object v, bool top, int depth)
    {
        if (v == null) { sb.Append("null"); return; }
        if (depth > 40) { sb.Append("…"); return; }
        var t = v.GetType();
        if (v is string s) { sb.Append('"').Append(s).Append('"'); return; }
        if (v is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (v is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (t.IsPrimitive || t.IsEnum) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
        if (!top && IsNamedNode(v)) { sb.Append('@').Append(KeyOf(v)); return; }
        if (v is IEnumerable en)
        {
            sb.Append('[');
            foreach (var x in en) { Append(sb, x, false, depth + 1); sb.Append(','); }
            sb.Append(']');
            return;
        }
        sb.Append(t.Name).Append('{');
        foreach (var fi in Fields(t)) { sb.Append(fi.Name).Append('='); Append(sb, fi.GetValue(v), false, depth + 1); sb.Append(';'); }
        sb.Append('}');
    }

    public static Dictionary<string, object> NamedNodes(object root)
    {
        var d = new Dictionary<string, object>();
        foreach (var o in AllObjects(root))
            if (IsNamedNode(o)) d.TryAdd(KeyOf(o), o);
        return d;
    }

    public static hkbBehaviorGraph Graph(hkRootLevelContainer root) =>
        root.m_namedVariants.Select(v => v.m_variant).OfType<hkbBehaviorGraph>().FirstOrDefault();
}

public static class BehChecks
{
    /// <summary>Clip generators whose m_animationInternalId does not index their own animation name in the graph's animation-name table.</summary>
    public static (int clips, int bad, List<string> examples) ClipIndex(HKLib.hk2018.hkRootLevelContainer root)
    {
        var names = BehGraph.Graph(root).m_data.m_stringData.m_animationNames;
        var clips = BehGraph.AllObjects(root).OfType<HKLib.hk2018.hkbClipGenerator>().ToList();
        var bad = clips.Where(c => c.m_animationInternalId < 0 || c.m_animationInternalId >= names.Count || !string.Equals(Path.GetFileNameWithoutExtension(names[c.m_animationInternalId]?.Replace('\\', '/') ?? ""), c.m_animationName, StringComparison.OrdinalIgnoreCase)).ToList();
        return (clips.Count, bad.Count, bad.Take(5).Select(c => $"{c.m_name}:{c.m_animationName}@{c.m_animationInternalId}={(c.m_animationInternalId >= 0 && c.m_animationInternalId < names.Count ? names[c.m_animationInternalId] : "OUT")}").ToList());
    }
}

public static class BehIdCheck
{
    /// <summary>Animation-id consistency: ids shared by different animations, and animations with several ids.</summary>
    public static (int sharedIds, int multiIdNames) Run(HKLib.hk2018.hkRootLevelContainer root)
    {
        var clips = BehGraph.AllObjects(root).OfType<HKLib.hk2018.hkbClipGenerator>().ToList();
        int shared = clips.GroupBy(c => c.m_animationInternalId).Count(g => g.Select(c => (c.m_animationName ?? "").ToLowerInvariant()).Distinct().Count() > 1);
        int multi = clips.GroupBy(c => (c.m_animationName ?? "").ToLowerInvariant()).Count(g => g.Select(c => c.m_animationInternalId).Distinct().Count() > 1);
        return (shared, multi);
    }
}
