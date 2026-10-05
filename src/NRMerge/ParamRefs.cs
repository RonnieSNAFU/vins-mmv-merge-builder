using System.Xml;

namespace NRMerge;

/// <summary>Reference annotations from Smithbox's NR Param Meta (field → target param, optionally conditional).</summary>
public static class ParamRefs
{
    public sealed record RefSpec(string Field, string Target, string CondField, string CondValue);

    static Dictionary<string, List<RefSpec>> _byType;

    public static List<RefSpec> ParseRefs(string field, string refs)
    {
        var list = new List<RefSpec>();
        foreach (var raw in refs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = raw.IndexOf('(');
            if (i < 0) { list.Add(new RefSpec(field, raw, null, null)); continue; }
            var target = raw[..i];
            var cond = raw[(i + 1)..].TrimEnd(')');
            var eq = cond.IndexOf('=');
            list.Add(eq < 0 ? new RefSpec(field, target, null, null)
                            : new RefSpec(field, target, cond[..eq].Trim(), cond[(eq + 1)..].Trim()));
        }
        return list;
    }

    /// <summary>All reference specs of a param type, read through the def/meta file pairing.</summary>
    public static List<RefSpec> For(string paramType)
    {
        if (_byType == null) LoadAll();
        return _byType.TryGetValue(paramType ?? "", out var l) ? l : new List<RefSpec>();
    }

    static void LoadAll()
    {
        _byType = new Dictionary<string, List<RefSpec>>(StringComparer.Ordinal);
        foreach (var defPath in Directory.GetFiles(Paths.ParamDefs("NR"), "*.xml"))
        {
            var metaPath = Path.Combine(Paths.ParamMeta("NR"), Path.GetFileName(defPath));
            if (!File.Exists(metaPath)) continue;
            var type = SoulsFormats.PARAMDEF.XmlDeserialize(defPath).ParamType;
            var doc = new XmlDocument();
            doc.Load(metaPath);
            var specs = new List<RefSpec>();
            var fieldNode = doc.DocumentElement?["Field"];
            if (fieldNode != null)
                foreach (XmlNode n in fieldNode.ChildNodes)
                {
                    var refs = n.Attributes?["Refs"]?.Value;
                    if (!string.IsNullOrEmpty(refs)) specs.AddRange(ParseRefs(n.Name, refs));
                }
            _byType[type] = specs;
        }
    }
}
