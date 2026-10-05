// Hand resolutions of AI-script function conflicts, stored as rules instead of whole function texts:
// per chunk key, which side's text the resolution starts from ("ev" = EV's chunk, "mmv" = MMV's chunk after its fN_
// prefixes were aligned to EV's — exactly the two texts available where the former Python merge consulted its override files),
// the SHA-256 of that text, and a minimal line edit script (Myers) turning it into the resolved function.
//
// JSON (one file per script, e.g. merge/ai/473000_battle.lua.rules.json):
// { "format": "nrmerge-airules/1",
//   "rules": { "<key>": { "take": "ev"|"mmv", "sideSha256": "<hex>",
//                          "edits": [ { "at": <0-based line in the side text>, "delete": <n>, "insert": ["...", ...] } ] } } }
// Edits are sorted by "at", refer to the ORIGINAL side lines and never overlap. Lines are the text split on '\n'.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NRMerge;

public sealed class AiEdit
{
    public int At { get; set; }
    public int Delete { get; set; }
    public List<string> Insert { get; set; } = new();
}

public sealed class AiRule
{
    public string Take { get; set; }
    public string SideSha256 { get; set; }
    public List<AiEdit> Edits { get; set; } = new();
    public int InsertedLines => Edits.Sum(e => e.Insert.Count);
    public int DeletedLines => Edits.Sum(e => e.Delete);
}

public sealed class AiRules
{
    public const string Format = "nrmerge-airules/1";

    /// <summary>Rules by chunk key, in file order.</summary>
    public OrderedDictionary<string, AiRule> Rules { get; } = new(StringComparer.Ordinal);

    public static AiRules Empty => new();

    public static string NoLongerApplies(string key) => $"hand resolution for {key} no longer applies (input changed)";

    /// <summary>
    /// Resolution of a both-changed chunk. False when there is no rule for <paramref name="key"/>. True otherwise, with
    /// either the resolved <paramref name="text"/>, or (side hash mismatch) <paramref name="error"/> set and text null.
    /// </summary>
    public bool TryResolve(string key, string evSide, string mmvAlignedSide, out string text, out string error)
    {
        text = null; error = null;
        if (!Rules.TryGetValue(key, out var rule)) return false;
        var side = rule.Take switch
        {
            "ev" => evSide,
            "mmv" => mmvAlignedSide,
            _ => throw new InvalidDataException($"AI rule {key}: take must be \"ev\" or \"mmv\", not \"{rule.Take}\""),
        };
        if (!string.Equals(Sha256(side), rule.SideSha256, StringComparison.OrdinalIgnoreCase))
        {
            error = NoLongerApplies(key);
            return true;
        }
        text = ApplyEdits(side, rule.Edits);
        return true;
    }

    // ------------------------------------------------------------------ generation

    /// <summary>Rule turning one of the two side texts into <paramref name="resolvedText"/>; the side needing fewer inserted lines wins (tie: EV).</summary>
    public static AiRule Make(string evSide, string mmvAlignedSide, string resolvedText)
    {
        var fromEv = Diff(evSide, resolvedText);
        var fromMmv = Diff(mmvAlignedSide, resolvedText);
        bool mmv = fromMmv.Sum(e => e.Insert.Count) < fromEv.Sum(e => e.Insert.Count);
        return new AiRule { Take = mmv ? "mmv" : "ev", SideSha256 = Sha256(mmv ? mmvAlignedSide : evSide), Edits = mmv ? fromMmv : fromEv };
    }

    /// <summary>
    /// Rules for every both-changed chunk of the three inputs that has a resolved text. <paramref name="log"/> gets one
    /// line per rule (and one per resolution whose key never reaches the both-changed branch, which gets no rule).
    /// </summary>
    public static AiRules Generate(string baseText, string evText, string mmvText, IReadOnlyDictionary<string, string> resolvedByKey, out List<string> log)
    {
        var rules = new AiRules();
        log = new List<string>();
        var sides = LuaFuncMerge.BothChangedSides(baseText, evText, mmvText);
        foreach (var (key, ev, mmv) in sides)
        {
            if (!resolvedByKey.TryGetValue(key, out var resolved)) continue;
            var rule = Make(ev, mmv, resolved);
            if (ApplyEdits(rule.Take == "ev" ? ev : mmv, rule.Edits) != resolved) throw new InvalidOperationException("edit script does not round-trip for " + key);
            rules.Rules[key] = rule;
            log.Add($"{key}: take {rule.Take}, {rule.Edits.Count} edits, {rule.InsertedLines} inserted / {rule.DeletedLines} deleted lines (side {(rule.Take == "ev" ? ev : mmv).Split('\n').Length} lines, resolved {resolved.Split('\n').Length})");
        }
        foreach (var k in resolvedByKey.Keys)
            if (!sides.Any(s => s.Key == k)) log.Add($"{k}: unused (never reaches the both-changed branch)");
        return rules;
    }

    /// <summary>
    /// Generate() from a luafuncmerge override directory: for each both-changed key, &lt;dir&gt;/&lt;OverrideFileName(key)&gt; read as
    /// the script reads it (universal newlines, trailing '\n' stripped).
    /// </summary>
    public static AiRules GenerateFromResolvedDir(string baseText, string evText, string mmvText, string resolvedDir, out List<string> log)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, _, _) in LuaFuncMerge.BothChangedSides(baseText, evText, mmvText))
        {
            var path = Path.Combine(resolvedDir, OverrideFileName(key));
            if (File.Exists(path)) resolved[key] = PyText.Read(path).TrimEnd('\n');
        }
        return Generate(baseText, evText, mmvText, resolved, out log);
    }

    /// <summary>The former Python merge's override file name: <c>re.sub(r'[^\w.#-]', '_', key) + '.lua'</c>.</summary>
    public static string OverrideFileName(string key) => Regex.Replace(key, @"[^\w.#-]", "_", RegexOptions.CultureInvariant) + ".lua";

    // ------------------------------------------------------------------ text, hash, diff

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string ApplyEdits(string side, IEnumerable<AiEdit> edits)
    {
        var lines = side.Split('\n');
        var output = new List<string>(lines.Length);
        int pos = 0;
        foreach (var e in edits)
        {
            if (e.At < pos || e.Delete < 0 || e.At + e.Delete > lines.Length) throw new InvalidDataException($"AI rule edit out of order or range: at {e.At}, delete {e.Delete}");
            for (; pos < e.At; pos++) output.Add(lines[pos]);
            output.AddRange(e.Insert);
            pos += e.Delete;
        }
        for (; pos < lines.Length; pos++) output.Add(lines[pos]);
        return string.Join("\n", output);
    }

    /// <summary>Minimal line edit script (Myers O(ND)) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static List<AiEdit> Diff(string from, string to)
    {
        string[] a = from.Split('\n'), b = to.Split('\n');
        int n = a.Length, m = b.Length, max = n + m, off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int dFinal = -1;
        for (int d = 0; d <= max && dFinal < 0; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = (k == -d || (k != d && v[off + k - 1] < v[off + k + 1])) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                v[off + k] = x;
                if (x >= n && y >= m) { dFinal = d; break; }
            }
        }
        // Backtrack into per-line ops (reverse order): 'd' a[x], 'i' b[y] at a-position x.
        var ops = new List<(char op, int ax, int by)>();
        {
            int x = n, y = m;
            for (int d = dFinal; d > 0; d--)
            {
                var vd = trace[d];
                int k = x - y;
                int prevK = (k == -d || (k != d && vd[off + k - 1] < vd[off + k + 1])) ? k + 1 : k - 1;
                int prevX = vd[off + prevK], prevY = prevX - prevK;
                while (x > prevX && y > prevY) { x--; y--; }
                if (x == prevX) ops.Add(('i', x, prevY));
                else ops.Add(('d', prevX, -1));
                x = prevX; y = prevY;
            }
        }
        ops.Reverse();
        var edits = new List<AiEdit>();
        AiEdit cur = null;
        foreach (var (op, ax, by) in ops)
        {
            // An op continues the current hunk when it touches the hunk's end position in a.
            if (cur == null || ax != cur.At + cur.Delete) { cur = new AiEdit { At = ax }; edits.Add(cur); }
            if (op == 'd') cur.Delete++;
            else cur.Insert.Add(b[by]);
        }
        return edits;
    }

    // ------------------------------------------------------------------ JSON

    public static AiRules Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path, Encoding.UTF8)) : Empty;

    public void Save(string path) => File.WriteAllText(path, ToJson(), new UTF8Encoding(false));

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteStartObject("rules");
            foreach (var (key, r) in Rules)
            {
                w.WriteStartObject(key);
                w.WriteString("take", r.Take);
                w.WriteString("sideSha256", r.SideSha256);
                w.WriteStartArray("edits");
                foreach (var e in r.Edits)
                {
                    w.WriteStartObject();
                    w.WriteNumber("at", e.At);
                    w.WriteNumber("delete", e.Delete);
                    w.WriteStartArray("insert");
                    foreach (var s in e.Insert) w.WriteStringValue(s);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    public static AiRules FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("format", out var f) && f.GetString() != Format) throw new InvalidDataException("unknown AI rules format " + f.GetString());
        var rules = new AiRules();
        foreach (var p in root.GetProperty("rules").EnumerateObject())
        {
            var r = new AiRule { Take = p.Value.GetProperty("take").GetString(), SideSha256 = p.Value.GetProperty("sideSha256").GetString() };
            foreach (var e in p.Value.GetProperty("edits").EnumerateArray())
                r.Edits.Add(new AiEdit
                {
                    At = e.GetProperty("at").GetInt32(),
                    Delete = e.GetProperty("delete").GetInt32(),
                    Insert = e.GetProperty("insert").EnumerateArray().Select(x => x.GetString()).ToList(),
                });
            rules.Rules[p.Name] = r;
        }
        return rules;
    }
}
