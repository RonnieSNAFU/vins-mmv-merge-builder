using SoulsFormats;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NRMerge;

/// <summary>Three-way comparison of EMEVD (per event) and MSB_NR (per named entry) files.</summary>
public static class MapDiff3
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new ByteArrayHex() },
    };

    sealed class ByteArrayHex : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => Convert.FromHexString(r.GetString());
        public override void Write(Utf8JsonWriter w, byte[] v, JsonSerializerOptions o) => w.WriteStringValue(Convert.ToHexString(v));
    }

    public static string EventSig(EMEVD.Event e)
    {
        var sb = new StringBuilder();
        sb.Append((int)e.RestBehavior).Append('|');
        foreach (var i in e.Instructions)
            sb.Append(i.Bank).Append(':').Append(i.ID).Append(':').Append(i.Layer?.ToString() ?? "-").Append(':').Append(Convert.ToHexString(i.ArgData)).Append(';');
        sb.Append('|');
        foreach (var p in e.Parameters)
            sb.Append(p.InstructionIndex).Append(',').Append(p.TargetStartByte).Append(',').Append(p.SourceStartByte).Append(',').Append(p.ByteCount).Append(';');
        return sb.ToString();
    }

    public static Dictionary<string, string> EmevdEntries(string path)
    {
        var d = new Dictionary<string, string>();
        if (path == null || !File.Exists(path)) return d;
        var e = EMEVD.Read(Dcx.Decompress(File.ReadAllBytes(path)));
        foreach (var ev in e.Events) d[$"event {ev.ID}"] = EventSig(ev);
        return d;
    }

    public static Dictionary<string, string> MsbEntries(string path)
    {
        var d = new Dictionary<string, string>();
        if (path == null || !File.Exists(path)) return d;
        var m = MSB_NR.Read(Dcx.Decompress(File.ReadAllBytes(path)));
        void AddAll<T>(string kind, IEnumerable<T> list) where T : class
        {
            foreach (var e in list)
            {
                var name = (string)e.GetType().GetProperty("Name")?.GetValue(e) ?? "?";
                string sig;
                try { sig = e.GetType().Name + JsonSerializer.Serialize(e, e.GetType(), JsonOpts); }
                catch (Exception ex) { sig = "ERR:" + ex.Message; }
                d[$"{kind} {name}"] = sig;
            }
        }
        AddAll("model", m.Models.GetEntries());
        AddAll("event", m.Events.GetEntries());
        AddAll("region", m.Regions.GetEntries());
        AddAll("route", m.Routes.GetEntries());
        AddAll("part", m.Parts.GetEntries());
        return d;
    }

    public static string Compare(Dictionary<string, string> b, Dictionary<string, string> x, Dictionary<string, string> y, StringBuilder detail)
    {
        var counts = new SortedDictionary<string, int>();
        foreach (var k in b.Keys.Union(x.Keys).Union(y.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            b.TryGetValue(k, out var s0); x.TryGetValue(k, out var s1); y.TryGetValue(k, out var s2);
            string cls;
            if (s0 == null)
                cls = s1 != null && s2 != null ? (s1 == s2 ? "add-both-same" : "add-both-DIFF") : (s1 != null ? "add-A" : "add-B");
            else
            {
                bool ca = s1 != s0, cb = s2 != s0;
                if (!ca && !cb) cls = "same";
                else if (ca && !cb) cls = s1 == null ? "del-A" : "mod-A";
                else if (!ca && cb) cls = s2 == null ? "del-B" : "mod-B";
                else if (s1 == s2) cls = "mod-both-same";
                else cls = "mod-both-DIFF";
            }
            counts[cls] = counts.GetValueOrDefault(cls) + 1;
            if (cls is not ("same" or "add-both-same" or "mod-both-same")) detail.AppendLine($"{cls}\t{k}");
        }
        return string.Join(" ", counts.Select(c => $"{c.Key}={c.Value}"));
    }

    public static int Run(string listFile, string outFile)
    {
        var sb = new StringBuilder();
        foreach (var line in File.ReadAllLines(listFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = line.Split('\t');
            var isMsb = p[3].EndsWith(".msb.dcx");
            Func<string, Dictionary<string, string>> rd = isMsb ? MsbEntries : EmevdEntries;
            var detail = new StringBuilder();
            string s;
            try { s = Compare(rd(p[0] == "" ? null : p[0]), rd(p[1]), rd(p[2]), detail); }
            catch (Exception e) { s = "ERROR " + e.Message; }
            Console.WriteLine($"{p[3]}\t{s}");
            sb.AppendLine($"#### {p[3]} :: {s}");
            sb.Append(detail);
        }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }
}
