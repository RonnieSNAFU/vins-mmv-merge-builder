namespace NRMerge;

/// <summary>Three-way merge of ordered sequences (diff3), matching items by a string key.</summary>
public static class Seq3
{
    public sealed class Conflict<T>
    {
        public int BaseIndex;
        public List<T> Base = new();
        public List<T> A = new();
        public List<T> B = new();
    }

    /// <summary>
    /// Merges two edited versions of <paramref name="baseL"/>. One-sided changes are applied; identical changes are taken
    /// once; for conflicting hunks the result holds A's hunk followed by B's items that appear in neither A's hunk nor
    /// the base hunk.
    /// </summary>
    public static List<T> Merge<T>(IList<T> baseL, IList<T> a, IList<T> b, Func<T, string> key, out List<Conflict<T>> conflicts,
        Func<Conflict<T>, List<T>> resolve = null)
    {
        var kb = baseL.Select(key).ToList();
        var ka = a.Select(key).ToList();
        var kc = b.Select(key).ToList();
        var ma = Match(kb, ka).ToDictionary(p => p.x, p => p.y);
        var mb = Match(kb, kc).ToDictionary(p => p.x, p => p.y);

        var result = new List<T>();
        conflicts = new List<Conflict<T>>();
        int i = 0, j = 0, k = 0;
        while (true)
        {
            int s = i;
            while (s < kb.Count && !(ma.ContainsKey(s) && mb.ContainsKey(s))) s++;
            int aEnd = s < kb.Count ? ma[s] : a.Count;
            int bEnd = s < kb.Count ? mb[s] : b.Count;

            var cb = kb.GetRange(i, s - i);
            var ca = ka.GetRange(j, aEnd - j);
            var cc = kc.GetRange(k, bEnd - k);
            if (ca.SequenceEqual(cb))
                for (int t = k; t < bEnd; t++) result.Add(b[t]);
            else if (cc.SequenceEqual(cb) || ca.SequenceEqual(cc))
                for (int t = j; t < aEnd; t++) result.Add(a[t]);
            else
            {
                var conflict = new Conflict<T> { BaseIndex = i };
                for (int t = i; t < s; t++) conflict.Base.Add(baseL[t]);
                for (int t = j; t < aEnd; t++) conflict.A.Add(a[t]);
                for (int t = k; t < bEnd; t++) conflict.B.Add(b[t]);
                var custom = resolve?.Invoke(conflict);
                if (custom != null) result.AddRange(custom);
                else
                {
                    result.AddRange(conflict.A);
                    var seen = new HashSet<string>(ca.Concat(cb));
                    for (int t = k; t < bEnd; t++)
                        if (!seen.Contains(kc[t])) result.Add(b[t]);
                    conflicts.Add(conflict);
                }
            }

            if (s >= kb.Count) break;
            result.Add(a[aEnd]);
            i = s + 1; j = aEnd + 1; k = bEnd + 1;
        }
        return result;
    }

    /// <summary>Longest-common-subsequence matching (Myers O(ND)) returning matched index pairs in order.</summary>
    public static List<(int x, int y)> Match(IList<string> a, IList<string> b)
    {
        int n = a.Count, m = b.Count;
        int pre = 0;
        while (pre < n && pre < m && a[pre] == b[pre]) pre++;
        int suf = 0;
        while (suf < n - pre && suf < m - pre && a[n - 1 - suf] == b[m - 1 - suf]) suf++;

        var res = new List<(int, int)>(Math.Min(n, m));
        for (int t = 0; t < pre; t++) res.Add((t, t));
        res.AddRange(Myers(a, pre, n - suf, b, pre, m - suf));
        for (int t = 0; t < suf; t++) res.Add((n - suf + t, m - suf + t));
        return res;
    }

    static List<(int, int)> Myers(IList<string> A, int a0, int a1, IList<string> B, int b0, int b1)
    {
        int N = a1 - a0, M = b1 - b0;
        var matches = new List<(int, int)>();
        if (N == 0 || M == 0) return matches;
        int max = N + M, off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = (k == -d || (k != d && v[off + k - 1] < v[off + k + 1])) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < N && y < M && A[a0 + x] == B[b0 + y]) { x++; y++; }
                v[off + k] = x;
                if (x >= N && y >= M)
                {
                    Backtrack(trace, off, N, M, matches);
                    matches.Reverse();
                    for (int t = 0; t < matches.Count; t++) matches[t] = (matches[t].Item1 + a0, matches[t].Item2 + b0);
                    return matches;
                }
            }
        }
        return matches;
    }

    static void Backtrack(List<int[]> trace, int off, int N, int M, List<(int, int)> matches)
    {
        int x = N, y = M;
        for (int d = trace.Count - 1; d >= 0; d--)
        {
            var v = trace[d];
            int k = x - y;
            int prevK = (k == -d || (k != d && v[off + k - 1] < v[off + k + 1])) ? k + 1 : k - 1;
            int prevX = v[off + prevK];
            int prevY = prevX - prevK;
            while (x > prevX && y > prevY) { x--; y--; matches.Add((x, y)); }
            if (d > 0) { x = prevX; y = prevY; }
        }
    }
}
