// Port of the parts of CPython 3.13 Lib/difflib.py that the AI merge (LuaFuncMerge) depends on:
// SequenceMatcher (junk + autojunk "popular" heuristic, find_longest_match, get_matching_blocks, get_opcodes, ratio,
// quick_ratio, real_quick_ratio) and Differ.compare / _fancy_replace / _fancy_helper / _plain_replace, reduced to the
// number of '+' / '-' lines ndiff(a, b) yields (linejunk=None, charjunk=IS_CHARACTER_JUNK). '?' lines are never counted.
// Elements are compared by value: lines by ordinal string equality, characters by Unicode code point (like Python str).
using System;
using System.Collections.Generic;
using System.Linq;

namespace NRMerge;

public static class PyDifflib
{
    /// <summary><c>sum(1 for op in difflib.ndiff(a, b) if op[:1] in '+-')</c>.</summary>
    public static int NdiffChangeCount(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Lines -> ids (equal ids <=> equal strings).
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string s) { if (!ids.TryGetValue(s, out var v)) ids[s] = v = ids.Count; return v; }
        var ia = a.Select(Id).ToArray();
        var ib = b.Select(Id).ToArray();
        var cpCache = new Dictionary<int, int[]>();
        var byId = new string[ids.Count];
        foreach (var kv in ids) byId[kv.Value] = kv.Key;
        int[] Cps(int id)
        {
            if (!cpCache.TryGetValue(id, out var cps))
                cpCache[id] = cps = byId[id].EnumerateRunes().Select(r => r.Value).ToArray();
            return cps;
        }

        var cruncher = new SequenceMatcher(null, ia, ib);
        int count = 0;
        foreach (var (tag, alo, ahi, blo, bhi) in cruncher.GetOpcodes())
        {
            switch (tag)
            {
                case 'r': count += FancyReplace(ia, alo, ahi, ib, blo, bhi, Cps); break;
                case 'd': count += ahi - alo; break;
                case 'i': count += bhi - blo; break;
            }
        }
        return count;
    }

    static readonly Func<int, bool> IsCharacterJunk = ch => ch == ' ' || ch == '\t';

    static int FancyReplace(int[] a, int alo, int ahi, int[] b, int blo, int bhi, Func<int, int[]> cps)
    {
        double bestRatio = 0.74, cutoff = 0.75;
        int bestI = -1, bestJ = -1;
        int? eqi = null, eqj = null;
        SequenceMatcher cruncher = null;
        for (int j = blo; j < bhi; j++)
        {
            int bj = b[j];
            int[] bjc = null;
            for (int i = alo; i < ahi; i++)
            {
                int ai = a[i];
                if (ai == bj)
                {
                    if (eqi == null) { eqi = i; eqj = j; }
                    continue;
                }
                bjc ??= cps(bj);
                if (cruncher == null || !ReferenceEquals(cruncher.B, bjc)) cruncher = new SequenceMatcher(IsCharacterJunk, null, bjc);
                cruncher.SetSeq1(cps(ai));
                if (cruncher.RealQuickRatio() > bestRatio && cruncher.QuickRatio() > bestRatio && cruncher.Ratio() > bestRatio)
                {
                    bestRatio = cruncher.Ratio(); bestI = i; bestJ = j;
                }
            }
        }
        bool identical = false;
        if (bestRatio < cutoff)
        {
            if (eqi == null) return (ahi - alo) + (bhi - blo); // _plain_replace
            bestI = eqi.Value; bestJ = eqj.Value; identical = true;
        }
        return FancyHelper(a, alo, bestI, b, blo, bestJ, cps)
             + (identical ? 0 : 2)                                // '  x' vs '- a' (? ..) '+ b' (? ..)
             + FancyHelper(a, bestI + 1, ahi, b, bestJ + 1, bhi, cps);
    }

    static int FancyHelper(int[] a, int alo, int ahi, int[] b, int blo, int bhi, Func<int, int[]> cps)
    {
        if (alo < ahi) return blo < bhi ? FancyReplace(a, alo, ahi, b, blo, bhi, cps) : ahi - alo;
        return blo < bhi ? bhi - blo : 0;
    }

    static double CalculateRatio(int matches, int length) => length != 0 ? 2.0 * matches / length : 1.0;

    /// <summary>difflib.SequenceMatcher over int elements (autojunk=True).</summary>
    public sealed class SequenceMatcher
    {
        readonly Func<int, bool> isjunk;
        int[] a, b;
        Dictionary<int, List<int>> b2j;
        HashSet<int> bjunk;
        Dictionary<int, int> fullbcount;
        List<(int i, int j, int k)> matchingBlocks;
        public int[] B => b;

        public SequenceMatcher(Func<int, bool> isjunk, int[] a, int[] b)
        {
            this.isjunk = isjunk;
            SetSeq1(a ?? Array.Empty<int>());
            SetSeq2(b ?? Array.Empty<int>());
        }

        public void SetSeq1(int[] na)
        {
            if (ReferenceEquals(na, a)) return;
            a = na; matchingBlocks = null;
        }

        public void SetSeq2(int[] nb)
        {
            if (ReferenceEquals(nb, b)) return;
            b = nb; matchingBlocks = null; fullbcount = null;
            ChainB();
        }

        void ChainB()
        {
            b2j = new Dictionary<int, List<int>>();
            for (int i = 0; i < b.Length; i++)
            {
                if (!b2j.TryGetValue(b[i], out var l)) b2j[b[i]] = l = new List<int>();
                l.Add(i);
            }
            bjunk = new HashSet<int>();
            if (isjunk != null)
            {
                foreach (var elt in b2j.Keys) if (isjunk(elt)) bjunk.Add(elt);
                foreach (var elt in bjunk) b2j.Remove(elt);
            }
            int n = b.Length;
            if (n >= 200)
            {
                int ntest = n / 100 + 1;
                var popular = b2j.Where(kv => kv.Value.Count > ntest).Select(kv => kv.Key).ToList();
                foreach (var elt in popular) b2j.Remove(elt);
            }
        }

        (int i, int j, int k) FindLongestMatch(int alo, int ahi, int blo, int bhi)
        {
            int besti = alo, bestj = blo, bestsize = 0;
            var j2len = new Dictionary<int, int>();
            for (int i = alo; i < ahi; i++)
            {
                var newj2len = new Dictionary<int, int>();
                if (b2j.TryGetValue(a[i], out var js))
                {
                    foreach (var j in js)
                    {
                        if (j < blo) continue;
                        if (j >= bhi) break;
                        int k = (j2len.TryGetValue(j - 1, out var prev) ? prev : 0) + 1;
                        newj2len[j] = k;
                        if (k > bestsize) { besti = i - k + 1; bestj = j - k + 1; bestsize = k; }
                    }
                }
                j2len = newj2len;
            }
            while (besti > alo && bestj > blo && !bjunk.Contains(b[bestj - 1]) && a[besti - 1] == b[bestj - 1])
            { besti--; bestj--; bestsize++; }
            while (besti + bestsize < ahi && bestj + bestsize < bhi && !bjunk.Contains(b[bestj + bestsize]) && a[besti + bestsize] == b[bestj + bestsize])
                bestsize++;
            while (besti > alo && bestj > blo && bjunk.Contains(b[bestj - 1]) && a[besti - 1] == b[bestj - 1])
            { besti--; bestj--; bestsize++; }
            while (besti + bestsize < ahi && bestj + bestsize < bhi && bjunk.Contains(b[bestj + bestsize]) && a[besti + bestsize] == b[bestj + bestsize])
                bestsize++;
            return (besti, bestj, bestsize);
        }

        public List<(int i, int j, int k)> GetMatchingBlocks()
        {
            if (matchingBlocks != null) return matchingBlocks;
            int la = a.Length, lb = b.Length;
            var queue = new Stack<(int, int, int, int)>();
            queue.Push((0, la, 0, lb));
            var blocks = new List<(int i, int j, int k)>();
            while (queue.Count > 0)
            {
                var (alo, ahi, blo, bhi) = queue.Pop();
                var x = FindLongestMatch(alo, ahi, blo, bhi);
                var (i, j, k) = x;
                if (k != 0)
                {
                    blocks.Add(x);
                    if (alo < i && blo < j) queue.Push((alo, i, blo, j));
                    if (i + k < ahi && j + k < bhi) queue.Push((i + k, ahi, j + k, bhi));
                }
            }
            blocks.Sort();
            int i1 = 0, j1 = 0, k1 = 0;
            var nonAdjacent = new List<(int, int, int)>();
            foreach (var (i2, j2, k2) in blocks)
            {
                if (i1 + k1 == i2 && j1 + k1 == j2) k1 += k2;
                else
                {
                    if (k1 != 0) nonAdjacent.Add((i1, j1, k1));
                    i1 = i2; j1 = j2; k1 = k2;
                }
            }
            if (k1 != 0) nonAdjacent.Add((i1, j1, k1));
            nonAdjacent.Add((la, lb, 0));
            return matchingBlocks = nonAdjacent;
        }

        /// <summary>Opcodes with tag 'r'eplace, 'd'elete, 'i'nsert, 'e'qual.</summary>
        public List<(char tag, int i1, int i2, int j1, int j2)> GetOpcodes()
        {
            int i = 0, j = 0;
            var answer = new List<(char, int, int, int, int)>();
            foreach (var (ai, bj, size) in GetMatchingBlocks())
            {
                char tag = '\0';
                if (i < ai && j < bj) tag = 'r';
                else if (i < ai) tag = 'd';
                else if (j < bj) tag = 'i';
                if (tag != '\0') answer.Add((tag, i, ai, j, bj));
                i = ai + size; j = bj + size;
                if (size != 0) answer.Add(('e', ai, i, bj, j));
            }
            return answer;
        }

        public double Ratio() => CalculateRatio(GetMatchingBlocks().Sum(t => t.k), a.Length + b.Length);

        public double QuickRatio()
        {
            if (fullbcount == null)
            {
                fullbcount = new Dictionary<int, int>();
                foreach (var elt in b) fullbcount[elt] = (fullbcount.TryGetValue(elt, out var c) ? c : 0) + 1;
            }
            var avail = new Dictionary<int, int>();
            int matches = 0;
            foreach (var elt in a)
            {
                int numb = avail.TryGetValue(elt, out var v) ? v : (fullbcount.TryGetValue(elt, out var f) ? f : 0);
                avail[elt] = numb - 1;
                if (numb > 0) matches++;
            }
            return CalculateRatio(matches, a.Length + b.Length);
        }

        public double RealQuickRatio()
        {
            int la = a.Length, lb = b.Length;
            return CalculateRatio(Math.Min(la, lb), la + lb);
        }
    }
}
