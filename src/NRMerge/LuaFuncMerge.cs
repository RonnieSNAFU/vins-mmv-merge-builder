// Exact port of the former Python script luafuncmerge (removed with the port) (function-level three-way merge of normalised Lua 5.0 AI scripts), with the
// per-function override files (<out>.resolved/<key>.lua) replaced by AiRules (side + hash + minimal line edits).
//
// Python behaviours reproduced on purpose:
//  - input text: open(encoding='utf-8').read() uses universal newlines, so "\r\n" AND lone "\r" become "\n"
//    (the script's own .replace('\r\n', '\n') is then a no-op); a UTF-8 BOM is kept as U+FEFF (see PyText.Read).
//  - text.split('\n') (not splitlines), blank top-level lines dropped via str.strip() (Python whitespace set).
//  - chunk keys: definition name, else 'stmt:' + masked line; repeated keys get '#n'.
//  - overrides (rules) are consulted only when both sides changed a chunk; they win over a clean line merge,
//    and the line merge is still computed first (aligned() + git merge-file), as in the script.
//  - a key present in EV and MMV but absent from base, with both sides different, makes the script raise TypeError
//    (align_prefix(None, ...)); the port throws BuildException.
//  - unresolved chunk: EV's text kept, key listed; the script's exit code = unresolved.Count.
//  - MMV chunks whose key exists in base but not in EV are appended at the end when MMV changed them (masked).
//  - output: '\n\n'.join(chunks) + '\n'; report file: '\n'.join(report) + '\n' (ReportText).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NRMerge;

public static class LuaFuncMerge
{
    static readonly Regex Def = new(@"^(?:function\s+([\w.:]+)\s*\(|([\w.:\[\]""']+)\s*=\s*function\s*\()", RegexOptions.CultureInvariant);
    static readonly Regex Mask = new(@"f\d+_(?:local|arg)\d+", RegexOptions.CultureInvariant);
    static readonly Regex Prefix = new(@"f(\d+)_(local|arg)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Merged script text (what the Python script wrote to OUT). <paramref name="report"/> = lines of OUT.report.txt,
    /// <paramref name="unresolved"/> = keys printed as "UNRESOLVED key" (the script's exit code is their count).
    /// </summary>
    public static string Merge(string baseText, string evText, string mmvText, AiRules rules, out List<string> report, out List<string> unresolved)
        => Core(baseText, evText, mmvText, rules ?? AiRules.Empty, out report, out unresolved, null);

    /// <summary>Content of OUT.report.txt.</summary>
    public static string ReportText(IEnumerable<string> report) => string.Join("\n", report) + "\n";

    /// <summary>Lines the Python script printed to stdout.</summary>
    public static IEnumerable<string> StdoutLines(IEnumerable<string> unresolved) => unresolved.Select(k => "UNRESOLVED " + k);

    /// <summary>
    /// For every chunk that reaches the both-changed branch: EV's chunk text and MMV's chunk text with its function
    /// prefixes aligned to EV's — the two texts a rule's "take" can refer to.
    /// </summary>
    public static List<(string Key, string Ev, string MmvAligned)> BothChangedSides(string baseText, string evText, string mmvText)
    {
        var sink = new List<(string, string, string)>();
        Core(baseText, evText, mmvText, AiRules.Empty, out _, out _, sink);
        return sink;
    }

    static string Core(string baseText, string evText, string mmvText, AiRules rules, out List<string> report, out List<string> unresolved,
        List<(string, string, string)> sidesSink)
    {
        var b = Chunks(baseText); var e = Chunks(evText); var m = Chunks(mmvText);
        var db = ToDict(b); var de = ToDict(e); var dm = ToDict(m);
        var order = e.Select(c => c.Key).ToList();
        var inOrder = new HashSet<string>(order, StringComparer.Ordinal);
        for (int i = 0; i < m.Count; i++)
        {
            var k = m[i].Key;
            if (de.ContainsKey(k) || inOrder.Contains(k) || db.ContainsKey(k)) continue;
            string prev = null;
            for (int j = i - 1; j >= 0; j--) if (inOrder.Contains(m[j].Key)) { prev = m[j].Key; break; }
            order.Insert(prev != null ? order.IndexOf(prev) + 1 : 0, k);
            inOrder.Add(k);
        }

        var result = new List<string>();
        unresolved = new List<string>();
        report = new List<string>();
        foreach (var k in order)
        {
            db.TryGetValue(k, out var sb); de.TryGetValue(k, out var se); dm.TryGetValue(k, out var sm);
            string mb = Masked(sb), me = Masked(se), mm = Masked(sm);
            string text;
            if (se == null)
            {
                text = sm;
                report.Add($"MMV-added {k}");
            }
            else if (sm == null)
            {
                text = (sb != null && me == mb) ? null : se;
                if (text == null) report.Add($"drop (MMV deleted) {k}");
            }
            else if (me == mm || mm == mb)
                text = se;
            else if (me == mb)
            {
                text = sm;
                report.Add($"MMV {k}");
            }
            else
            {
                if (sb == null)
                    throw new BuildException($"AI script merge: '{k}' was added by both EV and MMV with different text (no base chunk); add a hand resolution");
                string ab = AlignPrefix(sb, se), am = AlignPrefix(sm, se);
                string merged = Aligned(ab, se) && Aligned(ab, am) ? LineMerge(ab, se, am) : null;
                sidesSink?.Add((k, se, am));
                if (rules.TryResolve(k, se, am, out var resolved, out var error))
                {
                    if (error == null)
                    {
                        text = resolved;
                        report.Add($"RESOLVED {k}");
                    }
                    else
                    {
                        text = se;
                        unresolved.Add(k);
                        report.Add($"CONFLICT {k} ({error}; EV kept until resolved)");
                    }
                }
                else if (merged != null)
                {
                    text = merged;
                    report.Add($"LINE-MERGED {k}");
                }
                else
                {
                    text = se;
                    unresolved.Add(k);
                    report.Add($"CONFLICT {k} (EV kept until resolved)");
                }
            }
            if (text != null) result.Add(text);
        }
        foreach (var (k, _) in m)
        {
            if (db.ContainsKey(k) && !de.ContainsKey(k) && Masked(dm[k]) != Masked(db[k]))
            {
                result.Add(dm[k]);
                report.Add($"MMV (EV deleted) {k}");
            }
        }
        return string.Join("\n\n", result) + "\n";
    }

    static Dictionary<string, string> ToDict(List<(string Key, string Text)> chunks)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, t) in chunks) d[k] = t;
        return d;
    }

    /// <summary>chunks(): top-level chunks as (key, text), keys made unique with '#n'.</summary>
    public static List<(string Key, string Text)> Chunks(string text)
    {
        text = PyText.UniversalNewlines(text ?? "");
        var output = new List<(string key, List<string> lines)>();
        List<string> cur = new();
        string key = null;
        bool inFunc = false;
        foreach (var line in text.Split('\n'))
        {
            var mt = Def.Match(line);
            if (mt.Success && !(line.StartsWith(' ') || line.StartsWith('\t')))
            {
                if (cur.Count > 0) output.Add((key, cur));
                key = mt.Groups[1].Success && mt.Groups[1].Length > 0 ? mt.Groups[1].Value : mt.Groups[2].Value;
                cur = new List<string> { line };
                inFunc = true;
                continue;
            }
            if (inFunc)
            {
                cur.Add(line);
                if (line == "end")
                {
                    output.Add((key, cur));
                    cur = new List<string>(); key = null; inFunc = false;
                }
                continue;
            }
            if (PyText.Strip(line).Length == 0) continue;
            output.Add(("stmt:" + Mask.Replace(line, "L"), new List<string> { line }));
        }
        if (cur.Count > 0) output.Add((key, cur));
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var res = new List<(string, string)>();
        foreach (var (k, c) in output)
        {
            seen.TryGetValue(k, out var n);
            seen[k] = n + 1;
            res.Add((n == 0 ? k : $"{k}#{n}", string.Join("\n", c)));
        }
        return res;
    }

    public static string Masked(string s) => s == null ? null : Mask.Replace(s, "L");

    /// <summary>align_prefix(): renumbers the fN_ prefixes of a chunk to the reference chunk's, by order of appearance.</summary>
    public static string AlignPrefix(string text, string reference)
    {
        static List<string> Order(string t)
        {
            var seen = new List<string>();
            foreach (Match mm in Prefix.Matches(t))
                if (!seen.Contains(mm.Groups[1].Value)) seen.Add(mm.Groups[1].Value);
            return seen;
        }
        List<string> src = Order(text), dst = Order(reference);
        if (src.Count != dst.Count) return text;
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < src.Count; i++) mapping[src[i]] = dst[i];
        return Prefix.Replace(text, mm => $"f{mapping[mm.Groups[1].Value]}_{mm.Groups[2].Value}");
    }

    /// <summary>aligned(): true when ndiff's +/- line count is the same with and without local-name masking.</summary>
    public static bool Aligned(string baseText, string side)
    {
        string[] a = baseText.Split('\n'), b = side.Split('\n');
        int raw = PyDifflib.NdiffChangeCount(a, b);
        int msk = PyDifflib.NdiffChangeCount(a.Select(Masked).ToArray(), b.Select(Masked).ToArray());
        return raw == msk;
    }

    /// <summary>line_merge(): `git merge-file -p ev base mmv` on the chunk texts (+ '\n'); merged text without trailing '\n', or null on conflict.</summary>
    public static string LineMerge(string baseText, string ev, string mmv)
    {
        var merged = TextMerge3.Merge(ev + "\n", baseText + "\n", mmv + "\n", TextMergeStyle.Merge, "ev", "base", "mmv", out var conflicts);
        return conflicts == 0 ? merged.TrimEnd('\n') : null;
    }
}
