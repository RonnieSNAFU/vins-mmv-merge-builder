using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace NRMerge;

/// <summary>Three-way merge of plain object graphs by public read/write properties (deep for nested classes and equal-length arrays).</summary>
public static class ObjMerge
{
    static bool IsLeaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t.IsValueType;

    static IEnumerable<PropertyInfo> Props(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
            && p.GetGetMethod() != null && p.GetSetMethod() != null);

    /// <summary>Stable text signature of a value: leaves by invariant text, structs by their public fields, classes by properties.</summary>
    public static string Sig(object o)
    {
        var sb = new StringBuilder();
        Write(sb, o, 0);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object o, int depth)
    {
        if (o == null) { sb.Append("null"); return; }
        if (depth > 12) { sb.Append("..."); return; }
        var t = o.GetType();
        if (o is string s) { sb.Append('"').Append(s).Append('"'); return; }
        if (o is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (o is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (t.IsPrimitive || t.IsEnum || o is decimal) { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
        if (o is byte[] bytes) { sb.Append(Convert.ToHexString(bytes)); return; }
        if (o is IEnumerable en)
        {
            sb.Append('[');
            foreach (var x in en) { Write(sb, x, depth + 1); sb.Append(','); }
            sb.Append(']');
            return;
        }
        if (t.IsValueType)
        {
            sb.Append('(');
            foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) { Write(sb, fi.GetValue(o), depth + 1); sb.Append(','); }
            sb.Append(')');
            return;
        }
        sb.Append('{');
        foreach (var p in Props(t).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.Append(p.Name).Append(':');
            Write(sb, p.GetValue(o), depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }

    /// <summary>
    /// Merges <paramref name="m"/>'s changes (vs <paramref name="b"/>) into <paramref name="e"/> and returns the result (e is modified
    /// in place where possible). A value changed differently by both goes to <paramref name="winner"/>(property path); each such
    /// case is added to <paramref name="conflicts"/> as "path: EV=… MMV=… → side".
    /// </summary>
    public static object Merge(object b, object e, object m, Func<string, Side> winner, List<string> conflicts, string path = "")
    {
        string sb = Sig(b), se = Sig(e), sm = Sig(m);
        if (se == sm || sm == sb) return e;
        if (se == sb) return m;
        var t = e?.GetType();
        if (e != null && m != null && t == m.GetType() && (b == null || b.GetType() == t))
        {
            if (e is Array ae && m is Array am && ae.Length == am.Length && (b == null || ((Array)b).Length == ae.Length))
            {
                var ab = (Array)b;
                for (int i = 0; i < ae.Length; i++)
                    ae.SetValue(Merge(ab?.GetValue(i), ae.GetValue(i), am.GetValue(i), winner, conflicts, $"{path}[{i}]"), i);
                return e;
            }
            if (!IsLeaf(t) && e is not IEnumerable)
            {
                foreach (var p in Props(t))
                {
                    var pPath = path == "" ? p.Name : path + "." + p.Name;
                    var v = Merge(b == null ? null : p.GetValue(b), p.GetValue(e), p.GetValue(m), winner, conflicts, pPath);
                    p.SetValue(e, v);
                }
                return e;
            }
        }
        var w = winner(path);
        conflicts.Add($"{path}: EV={Short(se)} MMV={Short(sm)} -> {w}");
        return w == Side.EV ? e : m;
    }

    static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
