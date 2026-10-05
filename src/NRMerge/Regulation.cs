using Andre.Formats;
using SoulsFormats;
using System.Text.Json;

namespace NRMerge;

/// <summary>Loads regulation.bin files (Nightreign by default, Elden Ring for port comparisons) into named Param objects.</summary>
public static class Regulation
{
    public static string SmithboxData => Paths.SmithboxData;
    static readonly Dictionary<string, Dictionary<string, PARAMDEF>> _defs = new();
    static readonly Dictionary<string, Dictionary<string, string>> _typeMapping = new();

    public static Dictionary<string, PARAMDEF> Defs(string game = "NR")
    {
        if (_defs.TryGetValue(game, out var d)) return d;
        d = new Dictionary<string, PARAMDEF>();
        foreach (var f in Directory.GetFiles(Paths.ParamDefs(game), "*.xml"))
        {
            var def = PARAMDEF.XmlDeserialize(f, true);
            d[def.ParamType] = def;
        }
        return _defs[game] = d;
    }

    public static Dictionary<string, string> TypeMapping(string game = "NR")
    {
        if (_typeMapping.TryGetValue(game, out var m)) return m;
        m = new Dictionary<string, string>();
        var path = Paths.ParamTypeInfo(game);
        if (File.Exists(path))
        {
            var json = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in json.RootElement.GetProperty("Mapping").EnumerateObject())
                m[p.Name] = p.Value.GetString();
        }
        return _typeMapping[game] = m;
    }

    /// <summary>Vanilla regulations a release fetches (pinned + hashed, see data\fetch.json) instead of reading a Smithbox checkout.</summary>
    static readonly Dictionary<string, string> Fetched = new() { ["NR/10340000"] = "nr-regulation-1.03.4" };

    /// <summary>The vanilla regulation.bin of a game version: in a release only the fetched ones (else null); in dev mode the
    /// Smithbox checkout's Regulations folder (same file, same hash as the pinned fetch).</summary>
    public static string VanillaRegulationForVersion(string version, string game = "NR")
    {
        if (!Paths.DevData)
            return Fetched.TryGetValue($"{game}/{version}", out var id) ? Fetch.Get(id, Paths.Config) : null;
        var root = Path.Combine(SmithboxData, "PARAM", game, "Regulations");
        if (!Directory.Exists(root)) return null;
        foreach (var d in Directory.GetDirectories(root))
            if (Path.GetFileName(d).Contains($"({version})"))
                return Path.Combine(d, "regulation.bin");
        return null;
    }

    public sealed class Loaded
    {
        public string Path;
        public string Game;
        public BND4 Bnd;
        public ulong Version;
        public Dictionary<string, Param> Params = new(StringComparer.Ordinal);
        public Dictionary<string, BinderFile> Files = new(StringComparer.Ordinal);
        public Dictionary<string, string> OriginalParamTypes = new(StringComparer.Ordinal);
        public List<string> Errors = new();
    }

    public static Loaded Load(string path, string game = "NR")
    {
        var bytes = File.ReadAllBytes(path);
        var bnd = game == "ER" ? SFUtil.DecryptERRegulation(bytes) : SFUtil.DecryptNightreignRegulation(bytes);
        var l = new Loaded { Path = path, Bnd = bnd, Game = game };
        ulong.TryParse(bnd.Version, out l.Version);
        var defs = Defs(game);
        var map = TypeMapping(game);
        foreach (var f in bnd.Files)
        {
            if (!f.Name.EndsWith(".param", StringComparison.OrdinalIgnoreCase)) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(f.Name.Replace('\\', '/'));
            l.Files[name] = f;
            try
            {
                var p = Param.ReadIgnoreCompression(f.Bytes);
                l.OriginalParamTypes[name] = p.ParamType;
                if (string.IsNullOrEmpty(p.ParamType) || !defs.ContainsKey(p.ParamType))
                {
                    if (map.TryGetValue(name, out var mapped) && defs.ContainsKey(mapped))
                        p.ParamType = mapped;
                    else
                    {
                        l.Errors.Add($"{name}: no paramdef for type '{p.ParamType}'");
                        continue;
                    }
                }
                p.ApplyParamdef(defs[p.ParamType], l.Version, name);
                l.Params[name] = p;
            }
            catch (Exception e)
            {
                l.Errors.Add($"{name}: {e.Message}");
            }
        }
        return l;
    }

    /// <summary>Writes every loaded param back into its binder (original ParamType strings) and encrypts with a zero IV.</summary>
    public static void Save(Loaded l, string path)
    {
        foreach (var (name, p) in l.Params)
        {
            var mapped = p.ParamType;
            p.ParamType = l.OriginalParamTypes[name];
            l.Files[name].Bytes = p.Write();
            p.ParamType = mapped;
        }
        var bytes = SFUtil.EncryptNightreignRegulation(l.Bnd, new byte[16]);
        File.WriteAllBytes(path, bytes);
    }

    public static string Fmt(object v)
    {
        return v switch
        {
            null => "null",
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            byte[] b => Convert.ToHexString(b),
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
