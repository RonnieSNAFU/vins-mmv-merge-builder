// Exact port of the former Python resolver merge/hks/resolve_c0000 (removed with the port): resolves the 14 conflict hunks of the three-way merge of c0000.hks
// (EV / El-Fonz0 base 197b182 / MMV), given `git merge-file --diff3 -L EV -L BASE -L MMV` output (TextMerge3 Diff3 style).
//
// Python behaviours reproduced on purpose:
//  - input read in text mode (universal newlines), split on '\n'; parts re-joined with '\n' (so two adjacent hunks,
//    or a hunk resolved to no lines, leave an empty line, exactly as the script does); no newline is added at the end.
//  - list indexing with negative indices wraps (rule 2's backwards scan for the last number line).
//  - ids are compared as integers (arts_id == 007 equals 7) and printed in canonical form; duplicates are kept.
//  - str.strip() / \s use Python's whitespace set.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace NRMerge;

public static class HksResolve
{
    public const int ExpectedHunks = 14;

    sealed class Hunk
    {
        public List<string> Ev = new(), Base = new(), Mmv = new();
    }

    public static string Resolve(string diff3Text)
    {
        var parts = Parse(PyText.UniversalNewlines(diff3Text));
        int hunks = parts.Count(p => p is Hunk);
        if (hunks != ExpectedHunks)
            throw new BuildException($"c0000.hks: expected {ExpectedHunks} conflict hunks, found {hunks}; Elden Vins or MMV changed, update the HKS rules in HksResolve.cs");
        var output = new List<string>();
        int n = 0;
        foreach (var p in parts)
        {
            if (p is Hunk h) { n++; output.Add(string.Join("\n", ResolveHunk(n, h))); }
            else output.Add((string)p);
        }
        var text = string.Join("\n", output);
        if (text.Contains("<<<<<<<") || text.Contains(">>>>>>>") || text.Contains("|||||||"))
            throw new BuildException("c0000.hks: conflict markers left after resolving");
        return text;
    }

    /// <summary>parse(): plain-text strings and hunks, alternating (text first and last).</summary>
    static List<object> Parse(string text)
    {
        var lines = text.Split('\n');
        var output = new List<object>();
        var buf = new List<string>();
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i].StartsWith("<<<<<<< EV", StringComparison.Ordinal))
            {
                output.Add(string.Join("\n", buf)); buf = new List<string>();
                var h = new Hunk();
                var part = h.Ev;
                i++;
                while (!At(lines, i).StartsWith(">>>>>>> MMV", StringComparison.Ordinal))
                {
                    if (lines[i].StartsWith("||||||| BASE", StringComparison.Ordinal)) part = h.Base;
                    else if (lines[i] == "=======") part = h.Mmv;
                    else part.Add(lines[i]);
                    i++;
                }
                output.Add(h);
            }
            else buf.Add(lines[i]);
            i++;
        }
        output.Add(string.Join("\n", buf));
        return output;
    }

    static string At(string[] lines, int i) => i < lines.Length ? lines[i] : throw new BuildException("c0000.hks: unterminated conflict hunk");

    static readonly Regex ArtsId = new(@"arts_id == (\d+)", RegexOptions.CultureInvariant);
    static readonly Regex NumberLineStart = new(@"^\s*(\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    static readonly Regex CondLine = new(@"^\s*(else)?if\b", RegexOptions.CultureInvariant);
    static readonly Regex NumberLine = new(@"^\s*\d+", RegexOptions.CultureInvariant);
    static readonly Regex NumberComma = new(@"^(\s*\d+)\s*(,)?", RegexOptions.CultureInvariant);
    static readonly Regex TrailingThen = new(@"\s*then\s*$", RegexOptions.CultureInvariant);

    static BigInteger PyInt(string digits)
    {
        BigInteger v = 0;
        foreach (var c in digits) v = v * 10 + (int)char.GetNumericValue(c);
        return v;
    }

    static List<BigInteger> Ids(string line) => ArtsId.Matches(line).Select(m => PyInt(m.Groups[1].Value)).ToList();

    static int PyIndex(int count, int i) => i < 0 ? (i + count >= 0 ? i + count : throw new IndexOutOfRangeException("list index out of range")) : (i < count ? i : throw new IndexOutOfRangeException("list index out of range"));

    static string First(List<string> l) => l[PyIndex(l.Count, 0)];
    static string Last(List<string> l) => l[PyIndex(l.Count, -1)];

    /// <summary>cond_line(): index of the first `if`/`elseif` line.</summary>
    static int CondLineIndex(List<string> block)
    {
        for (int i = 0; i < block.Count; i++) if (CondLine.IsMatch(block[i])) return i;
        throw new BuildException("c0000.hks: no if/elseif line in a hunk");
    }

    /// <summary>EV's comment lines + EV's condition line, then MMV's lines after its own condition line.</summary>
    static List<string> EvConditionMmvBody(Hunk h)
    {
        int ce = CondLineIndex(h.Ev), cm = CondLineIndex(h.Mmv);
        return h.Ev.Take(ce + 1).Concat(h.Mmv.Skip(cm + 1)).ToList();
    }

    static List<string> ResolveHunk(int n, Hunk h)
    {
        List<string> ev = h.Ev, bs = h.Base, mmv = h.Mmv;
        switch (n)
        {
            case 1 or 8:   // both added different new functions / elseif branches at the same spot: keep both
                return ev.Concat(mmv).ToList();
            case 2:        // EV turned the skill condition into aow_list; add the skill IDs MMV appended to the condition
            {
                var baseIds = Ids(First(bs));
                var extra = Ids(First(mmv)).Where(x => !baseIds.Contains(x)).ToList();
                var have = NumberLineStart.Matches(string.Join("\n", ev)).Select(m => PyInt(m.Groups[1].Value)).ToHashSet();
                extra = extra.Where(x => !have.Contains(x)).ToList();
                if (extra.Count == 0) return ev;
                int closing = -1;
                for (int i = 0; i < ev.Count; i++) if (PyText.Strip(ev[i]) == "}") closing = i;
                if (closing < 0) throw new BuildException("c0000.hks hunk 2: no closing '}'");
                int last = closing - 1;
                while (!NumberLine.IsMatch(ev[PyIndex(ev.Count, last)])) last--;
                var output = new List<string>(ev);
                int li = PyIndex(output.Count, last);
                output[li] = NumberComma.Replace(output[li], m => m.Groups[1].Value + ",", 1);
                return output.Take(closing).Concat(extra.Select(x => $"        {x}, -- MMV")).Concat(output.Skip(closing)).ToList();
            }
            case 3:        // skill list continued: EV's lines + skill IDs only MMV added
            {
                var baseIds = Ids(string.Join(" ", bs));
                var evIds = Ids(string.Join(" ", ev));
                var extra = Ids(string.Join(" ", mmv)).Where(x => !baseIds.Contains(x) && !evIds.Contains(x)).ToList();
                var output = new List<string>(ev);
                int li = PyIndex(output.Count, -1);
                output[li] = TrailingThen.Replace(output[li], "") + string.Concat(extra.Select(x => $" or arts_id == {x}")) + " then";
                return output;
            }
            case 4:        // MMV's guard checks for its weapon kinds, then EV's widened deflect condition
                return mmv.Take(Math.Max(0, mmv.Count - 1)).Concat(ev).ToList();
            case 5:        // MMV only reordered the terms
                return ev;
            case 6:        // EV's list (also avoids MMV's WEAPON_CATEGORY_WHI typo) + MMV's kind 61
                return new List<string> { TrailingThen.Replace(First(ev), " or kind == 61 then") };
            case 7:        // EV's long category list + MMV's kind 53
                return new List<string> { First(ev).Replace(") == TRUE then", ", 53) == TRUE then", StringComparison.Ordinal) };
            case 9:        // EV's condition (no deflect) with MMV's inner check
            {
                var output = new List<string>(ev);
                output[PyIndex(output.Count, -1)] = Last(mmv);
                return output;
            }
            case 10 or 11 or 12 or 13 or 14:
                return EvConditionMmvBody(h);
        }
        throw new BuildException($"c0000.hks: no rule for hunk {n}");
    }
}
