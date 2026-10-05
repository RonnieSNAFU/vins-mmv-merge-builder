// In-process port of `git merge-file -p [--diff3]` (git v2.50.1 libxdiff, Myers algorithm, xpparam flags 0).
// Ported from git/git v2.50.1:
//   xdiff/xmerge.c   xdl_merge, xdl_do_merge, xdl_append_merge, xdl_refine_conflicts, xdl_simplify_non_conflicts,
//                    xdl_merge_two_conflicts, xdl_fill_merge_buffer, fill_conflict_hunk, xdl_recs_copy_0,
//                    is_eol_crlf, is_cr_needed, lines_contain_alnum
//   xdiff/xdiffi.c   xdl_do_diff, xdl_recs_cmp, xdl_split, xdl_change_compact (no indent heuristic), group_*,
//                    xdl_build_script
//   xdiff/xprepare.c xdl_prepare_env, xdl_classify_record, xdl_trim_ends, xdl_cleanup_records, xdl_clean_mmatch
//   xdiff/xutils.c   xdl_bogosqrt
//   builtin/merge-file.c  level = XDL_MERGE_ZEALOUS_ALNUM, favor = 0, marker size 7 (git caps its exit code at 127; we return the true hunk count).
// Lines are compared exactly (including the line terminator). A line is everything up to and including '\n';
// the last line may lack it. Self-contained, no dependencies.
using System;
using System.Collections.Generic;
using System.Text;

namespace NRMerge;

public enum TextMergeStyle { Merge, Diff3 }

public static class TextMerge3
{
    const int MergeMinimal = 0, MergeEager = 1, MergeZealous = 2, MergeZealousAlnum = 3;
    const int MarkerSize = 7;

    /// <summary>
    /// Equivalent of <c>git merge-file -p [--diff3] -L labelA -L labelO -L labelB a o b</c>.
    /// <paramref name="conflicts"/> is the number of conflict hunks (git's exit code, except git caps it at 127).
    /// </summary>
    public static string Merge(string a, string o, string b, TextMergeStyle style, string labelA, string labelO, string labelB, out int conflicts)
    {
        string[] lo = SplitLines(o ?? ""), la = SplitLines(a ?? ""), lb = SplitLines(b ?? "");

        var xe1 = DoDiff(lo, la);
        var xe2 = DoDiff(lo, lb);
        ChangeCompact(xe1.F1, xe1.F2);
        ChangeCompact(xe1.F2, xe1.F1);
        var xscr1 = BuildScript(xe1);
        ChangeCompact(xe2.F1, xe2.F2);
        ChangeCompact(xe2.F2, xe2.F1);
        var xscr2 = BuildScript(xe2);

        if (xscr1.Count == 0) { conflicts = 0; return b ?? ""; }
        if (xscr2.Count == 0) { conflicts = 0; return a ?? ""; }

        var sb = new StringBuilder((a?.Length ?? 0) + (b?.Length ?? 0) / 8 + 256);
        int n = DoMerge(xe1, xscr1, xe2, xscr2, style, labelA, labelO, labelB, sb);
        conflicts = n;
        return sb.ToString();
    }

    // ------------------------------------------------------------------ records

    static string[] SplitLines(string s)
    {
        var list = new List<string>();
        int start = 0;
        while (start < s.Length)
        {
            int nl = s.IndexOf('\n', start);
            int end = nl < 0 ? s.Length : nl + 1;
            list.Add(s.Substring(start, end - start));
            start = end;
        }
        return list.ToArray();
    }

    sealed class XdFile
    {
        public int NRec;
        public string[] Recs;
        public int[] RHa;          // class index per record (rec->ha after classification)
        public byte[] RChgBuf;     // rchg with sentinel at -1 and NRec: RChgBuf[i + 1]
        public int[] RIndex;
        public int[] Ha;           // xdf->ha: class of each non-discarded record
        public int NReff;
        public int DStart, DEnd;

        public bool Chg(int i) => RChgBuf[i + 1] != 0;
        public void SetChg(int i, byte v) => RChgBuf[i + 1] = v;
    }

    sealed class XdEnv { public XdFile F1, F2; }

    sealed class XdChange { public int I1, I2, Chg1, Chg2; }

    // ------------------------------------------------------------------ xprepare.c

    static long BogoSqrt(long n)
    {
        long i;
        for (i = 1; n > 0; n >>= 2) i <<= 1;
        return i;
    }

    static XdEnv PrepareEnv(string[] l1, string[] l2)
    {
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        var len1 = new List<int>();
        var len2 = new List<int>();
        XdFile Ctx(string[] lines, int pass)
        {
            var f = new XdFile { NRec = lines.Length, Recs = lines, RHa = new int[lines.Length] };
            for (int i = 0; i < lines.Length; i++)
            {
                if (!classes.TryGetValue(lines[i], out var idx))
                {
                    idx = classes.Count;
                    classes.Add(lines[i], idx);
                    len1.Add(0); len2.Add(0);
                }
                if (pass == 1) len1[idx]++; else len2[idx]++;
                f.RHa[i] = idx;
            }
            f.RChgBuf = new byte[lines.Length + 2];
            f.RIndex = new int[lines.Length + 1];
            f.Ha = new int[lines.Length + 1];
            f.DStart = 0;
            f.DEnd = lines.Length - 1;
            return f;
        }
        var xe = new XdEnv { F1 = Ctx(l1, 1), F2 = Ctx(l2, 2) };
        TrimEnds(xe.F1, xe.F2);
        CleanupRecords(len1, len2, xe.F1, xe.F2);
        return xe;
    }

    static void TrimEnds(XdFile x1, XdFile x2)
    {
        int i, lim;
        for (i = 0, lim = Math.Min(x1.NRec, x2.NRec); i < lim; i++)
            if (x1.RHa[i] != x2.RHa[i]) break;
        x1.DStart = x2.DStart = i;
        int r1 = x1.NRec - 1, r2 = x2.NRec - 1;
        for (lim -= i, i = 0; i < lim; i++, r1--, r2--)
            if (x1.RHa[r1] != x2.RHa[r2]) break;
        x1.DEnd = x1.NRec - i - 1;
        x2.DEnd = x2.NRec - i - 1;
    }

    const int KpdisRun = 4, MaxEqLimit = 1024, SimscanWindow = 100;

    static bool CleanMMatch(byte[] dis, int i, int s, int e)
    {
        long r, rdis0, rpdis0, rdis1, rpdis1;
        if (i - s > SimscanWindow) s = i - SimscanWindow;
        if (e - i > SimscanWindow) e = i + SimscanWindow;
        for (r = 1, rdis0 = 0, rpdis0 = 1; (i - r) >= s; r++)
        {
            if (dis[i - r] == 0) rdis0++;
            else if (dis[i - r] == 2) rpdis0++;
            else break;
        }
        if (rdis0 == 0) return false;
        for (r = 1, rdis1 = 0, rpdis1 = 1; (i + r) <= e; r++)
        {
            if (dis[i + r] == 0) rdis1++;
            else if (dis[i + r] == 2) rpdis1++;
            else break;
        }
        if (rdis1 == 0) return false;
        rdis1 += rdis0;
        rpdis1 += rpdis0;
        return rpdis1 * KpdisRun < (rpdis1 + rdis1);
    }

    static void CleanupRecords(List<int> len1, List<int> len2, XdFile x1, XdFile x2)
    {
        var dis1 = new byte[x1.NRec + 1];
        var dis2 = new byte[x2.NRec + 1];
        long mlim = Math.Min(BogoSqrt(x1.NRec), MaxEqLimit);
        for (int i = x1.DStart; i <= x1.DEnd; i++)
        {
            int nm = len2[x1.RHa[i]];
            dis1[i] = (byte)(nm == 0 ? 0 : nm >= mlim ? 2 : 1);
        }
        mlim = Math.Min(BogoSqrt(x2.NRec), MaxEqLimit);
        for (int i = x2.DStart; i <= x2.DEnd; i++)
        {
            int nm = len1[x2.RHa[i]];
            dis2[i] = (byte)(nm == 0 ? 0 : nm >= mlim ? 2 : 1);
        }
        Keep(x1, dis1);
        Keep(x2, dis2);

        static void Keep(XdFile x, byte[] dis)
        {
            int nreff = 0;
            for (int i = x.DStart; i <= x.DEnd; i++)
            {
                if (dis[i] == 1 || (dis[i] == 2 && !CleanMMatch(dis, i, x.DStart, x.DEnd)))
                {
                    x.RIndex[nreff] = i;
                    x.Ha[nreff] = x.RHa[i];
                    nreff++;
                }
                else x.SetChg(i, 1);
            }
            x.NReff = nreff;
        }
    }

    // ------------------------------------------------------------------ xdiffi.c (Myers)

    const long MaxCostMin = 256, HeurMinCost = 256, SnakeCnt = 20, KHeur = 4;
    const long LineMax = long.MaxValue;

    sealed class DiffCtx
    {
        public int[] Ha1, Ha2;
        public XdFile F1, F2;
        public long[] Kv;
        public int FOff, BOff;   // kvdf[d] = Kv[FOff + d], kvdb[d] = Kv[BOff + d]
        public long MxCost;
    }

    static XdEnv DoDiff(string[] l1, string[] l2)
    {
        var xe = PrepareEnv(l1, l2);
        int ndiags = xe.F1.NReff + xe.F2.NReff + 3;
        var ctx = new DiffCtx
        {
            Ha1 = xe.F1.Ha, Ha2 = xe.F2.Ha, F1 = xe.F1, F2 = xe.F2,
            Kv = new long[2 * ndiags + 2],
            FOff = xe.F2.NReff + 1,
            BOff = ndiags + xe.F2.NReff + 1,
            MxCost = Math.Max(BogoSqrt(ndiags), MaxCostMin),
        };
        RecsCmp(ctx, 0, xe.F1.NReff, 0, xe.F2.NReff, false);
        return xe;
    }

    static void RecsCmp(DiffCtx c, long off1, long lim1, long off2, long lim2, bool needMin)
    {
        var ha1 = c.Ha1; var ha2 = c.Ha2;
        for (; off1 < lim1 && off2 < lim2 && ha1[off1] == ha2[off2]; off1++, off2++) ;
        for (; off1 < lim1 && off2 < lim2 && ha1[lim1 - 1] == ha2[lim2 - 1]; lim1--, lim2--) ;

        if (off1 == lim1)
        {
            for (; off2 < lim2; off2++) c.F2.SetChg(c.F2.RIndex[off2], 1);
        }
        else if (off2 == lim2)
        {
            for (; off1 < lim1; off1++) c.F1.SetChg(c.F1.RIndex[off1], 1);
        }
        else
        {
            Split(c, off1, lim1, off2, lim2, needMin, out long si1, out long si2, out bool minLo, out bool minHi);
            RecsCmp(c, off1, si1, off2, si2, minLo);
            RecsCmp(c, si1, lim1, si2, lim2, minHi);
        }
    }

    static void Split(DiffCtx c, long off1, long lim1, long off2, long lim2, bool needMin,
                      out long spl1, out long spl2, out bool minLo, out bool minHi)
    {
        var ha1 = c.Ha1; var ha2 = c.Ha2; var kv = c.Kv;
        long F = c.FOff, B = c.BOff;
        long dmin = off1 - lim2, dmax = lim1 - off2;
        long fmid = off1 - off2, bmid = lim1 - lim2;
        bool odd = ((fmid - bmid) & 1) != 0;
        long fmin = fmid, fmax = fmid;
        long bmin = bmid, bmax = bmid;
        long ec, d, i1, i2, prev1, best, dd, v, k;

        kv[F + fmid] = off1;
        kv[B + bmid] = lim1;

        for (ec = 1; ; ec++)
        {
            bool gotSnake = false;

            if (fmin > dmin) kv[F + (--fmin) - 1] = -1; else ++fmin;
            if (fmax < dmax) kv[F + (++fmax) + 1] = -1; else --fmax;

            for (d = fmax; d >= fmin; d -= 2)
            {
                if (kv[F + d - 1] >= kv[F + d + 1]) i1 = kv[F + d - 1] + 1;
                else i1 = kv[F + d + 1];
                prev1 = i1;
                i2 = i1 - d;
                for (; i1 < lim1 && i2 < lim2 && ha1[i1] == ha2[i2]; i1++, i2++) ;
                if (i1 - prev1 > SnakeCnt) gotSnake = true;
                kv[F + d] = i1;
                if (odd && bmin <= d && d <= bmax && kv[B + d] <= i1)
                {
                    spl1 = i1; spl2 = i2; minLo = minHi = true;
                    return;
                }
            }

            if (bmin > dmin) kv[B + (--bmin) - 1] = LineMax; else ++bmin;
            if (bmax < dmax) kv[B + (++bmax) + 1] = LineMax; else --bmax;

            for (d = bmax; d >= bmin; d -= 2)
            {
                if (kv[B + d - 1] < kv[B + d + 1]) i1 = kv[B + d - 1];
                else i1 = kv[B + d + 1] - 1;
                prev1 = i1;
                i2 = i1 - d;
                for (; i1 > off1 && i2 > off2 && ha1[i1 - 1] == ha2[i2 - 1]; i1--, i2--) ;
                if (prev1 - i1 > SnakeCnt) gotSnake = true;
                kv[B + d] = i1;
                if (!odd && fmin <= d && d <= fmax && i1 <= kv[F + d])
                {
                    spl1 = i1; spl2 = i2; minLo = minHi = true;
                    return;
                }
            }

            if (needMin) continue;

            if (gotSnake && ec > HeurMinCost)
            {
                spl1 = spl2 = 0;
                for (best = 0, d = fmax; d >= fmin; d -= 2)
                {
                    dd = d > fmid ? d - fmid : fmid - d;
                    i1 = kv[F + d];
                    i2 = i1 - d;
                    v = (i1 - off1) + (i2 - off2) - dd;
                    if (v > KHeur * ec && v > best &&
                        off1 + SnakeCnt <= i1 && i1 < lim1 &&
                        off2 + SnakeCnt <= i2 && i2 < lim2)
                    {
                        for (k = 1; ha1[i1 - k] == ha2[i2 - k]; k++)
                            if (k == SnakeCnt)
                            {
                                best = v; spl1 = i1; spl2 = i2;
                                break;
                            }
                    }
                }
                if (best > 0) { minLo = true; minHi = false; return; }

                for (best = 0, d = bmax; d >= bmin; d -= 2)
                {
                    dd = d > bmid ? d - bmid : bmid - d;
                    i1 = kv[B + d];
                    i2 = i1 - d;
                    v = (lim1 - i1) + (lim2 - i2) - dd;
                    if (v > KHeur * ec && v > best &&
                        off1 < i1 && i1 <= lim1 - SnakeCnt &&
                        off2 < i2 && i2 <= lim2 - SnakeCnt)
                    {
                        for (k = 0; ha1[i1 + k] == ha2[i2 + k]; k++)
                            if (k == SnakeCnt - 1)
                            {
                                best = v; spl1 = i1; spl2 = i2;
                                break;
                            }
                    }
                }
                if (best > 0) { minLo = false; minHi = true; return; }
            }

            if (ec >= c.MxCost)
            {
                long fbest = -1, fbest1 = -1;
                for (d = fmax; d >= fmin; d -= 2)
                {
                    i1 = Math.Min(kv[F + d], lim1);
                    i2 = i1 - d;
                    if (lim2 < i2) { i1 = lim2 + d; i2 = lim2; }
                    if (fbest < i1 + i2) { fbest = i1 + i2; fbest1 = i1; }
                }
                long bbest = LineMax, bbest1 = LineMax;
                for (d = bmax; d >= bmin; d -= 2)
                {
                    i1 = Math.Max(off1, kv[B + d]);
                    i2 = i1 - d;
                    if (i2 < off2) { i1 = off2 + d; i2 = off2; }
                    if (i1 + i2 < bbest) { bbest = i1 + i2; bbest1 = i1; }
                }
                if ((lim1 + lim2) - bbest < fbest - (off1 + off2))
                {
                    spl1 = fbest1; spl2 = fbest - fbest1; minLo = true; minHi = false;
                }
                else
                {
                    spl1 = bbest1; spl2 = bbest - bbest1; minLo = false; minHi = true;
                }
                return;
            }
        }
    }

    // xdl_change_compact without XDF_INDENT_HEURISTIC (merge-file passes flags 0)
    struct Group { public int Start, End; }

    static void GroupInit(XdFile x, ref Group g)
    {
        g.Start = g.End = 0;
        while (x.Chg(g.End)) g.End++;
    }

    static bool GroupNext(XdFile x, ref Group g)
    {
        if (g.End == x.NRec) return false;
        g.Start = g.End + 1;
        for (g.End = g.Start; x.Chg(g.End); g.End++) ;
        return true;
    }

    static bool GroupPrevious(XdFile x, ref Group g)
    {
        if (g.Start == 0) return false;
        g.End = g.Start - 1;
        for (g.Start = g.End; x.Chg(g.Start - 1); g.Start--) ;
        return true;
    }

    static bool GroupSlideDown(XdFile x, ref Group g)
    {
        if (g.End < x.NRec && x.RHa[g.Start] == x.RHa[g.End])
        {
            x.SetChg(g.Start++, 0);
            x.SetChg(g.End++, 1);
            while (x.Chg(g.End)) g.End++;
            return true;
        }
        return false;
    }

    static bool GroupSlideUp(XdFile x, ref Group g)
    {
        if (g.Start > 0 && x.RHa[g.Start - 1] == x.RHa[g.End - 1])
        {
            x.SetChg(--g.Start, 1);
            x.SetChg(--g.End, 0);
            while (x.Chg(g.Start - 1)) g.Start--;
            return true;
        }
        return false;
    }

    static void ChangeCompact(XdFile xdf, XdFile xdfo)
    {
        Group g = default, go = default;
        GroupInit(xdf, ref g);
        GroupInit(xdfo, ref go);
        while (true)
        {
            if (g.End != g.Start)
            {
                int groupsize, earliestEnd, endMatchingOther;
                do
                {
                    groupsize = g.End - g.Start;
                    endMatchingOther = -1;
                    while (GroupSlideUp(xdf, ref g))
                        if (!GroupPrevious(xdfo, ref go)) throw new InvalidOperationException("group sync broken sliding up");
                    earliestEnd = g.End;
                    if (go.End > go.Start) endMatchingOther = g.End;
                    while (true)
                    {
                        if (!GroupSlideDown(xdf, ref g)) break;
                        if (!GroupNext(xdfo, ref go)) throw new InvalidOperationException("group sync broken sliding down");
                        if (go.End > go.Start) endMatchingOther = g.End;
                    }
                } while (groupsize != g.End - g.Start);

                if (g.End == earliestEnd)
                {
                    /* no shifting was possible */
                }
                else if (endMatchingOther != -1)
                {
                    while (go.End == go.Start)
                    {
                        if (!GroupSlideUp(xdf, ref g)) throw new InvalidOperationException("match disappeared");
                        if (!GroupPrevious(xdfo, ref go)) throw new InvalidOperationException("group sync broken sliding to match");
                    }
                }
            }
            if (!GroupNext(xdf, ref g)) break;
            if (!GroupNext(xdfo, ref go)) throw new InvalidOperationException("group sync broken moving to next group");
        }
        if (GroupNext(xdfo, ref go)) throw new InvalidOperationException("group sync broken at end of file");
    }

    static List<XdChange> BuildScript(XdEnv xe)
    {
        var res = new List<XdChange>();
        XdFile f1 = xe.F1, f2 = xe.F2;
        bool C1(int i) => i >= -1 && i <= f1.NRec && f1.Chg(i);
        bool C2(int i) => i >= -1 && i <= f2.NRec && f2.Chg(i);
        for (int i1 = f1.NRec, i2 = f2.NRec; i1 >= 0 || i2 >= 0; i1--, i2--)
        {
            if (C1(i1 - 1) || C2(i2 - 1))
            {
                int l1, l2;
                for (l1 = i1; C1(i1 - 1); i1--) ;
                for (l2 = i2; C2(i2 - 1); i2--) ;
                res.Add(new XdChange { I1 = i1, I2 = i2, Chg1 = l1 - i1, Chg2 = l2 - i2 });
            }
        }
        res.Reverse();
        return res;
    }

    // ------------------------------------------------------------------ xmerge.c

    sealed class XdMerge
    {
        public XdMerge Next;
        public int Mode;            // 0 conflict, 1 take first, 2 take second, 3 both, 4 identical (refined away)
        public int I1, I2, Chg1, Chg2;
        public int I0, Chg0;
    }

    static void AppendMerge(ref XdMerge last, ref XdMerge head, int mode, int i0, int chg0, int i1, int chg1, int i2, int chg2)
    {
        var m = last;
        if (m != null && (i1 <= m.I1 + m.Chg1 || i2 <= m.I2 + m.Chg2))
        {
            if (mode != m.Mode) m.Mode = 0;
            m.Chg0 = i0 + chg0 - m.I0;
            m.Chg1 = i1 + chg1 - m.I1;
            m.Chg2 = i2 + chg2 - m.I2;
        }
        else
        {
            m = new XdMerge { Mode = mode, I0 = i0, Chg0 = chg0, I1 = i1, Chg1 = chg1, I2 = i2, Chg2 = chg2 };
            if (last != null) last.Next = m;
            last = m;
            head ??= m;
        }
    }

    static int DoMerge(XdEnv xe1, List<XdChange> s1, XdEnv xe2, List<XdChange> s2, TextMergeStyle style,
                       string name1, string ancestorName, string name2, StringBuilder result)
    {
        int level = MergeZealousAlnum;
        bool diff3 = style == TextMergeStyle.Diff3;
        if (diff3 && MergeEager < level) level = MergeEager;

        XdMerge head = null, c = null;
        int p1 = 0, p2 = 0;
        int i0, i1, i2, chg0, chg1, chg2;
        while (p1 < s1.Count && p2 < s2.Count)
        {
            var x1 = s1[p1]; var x2 = s2[p2];
            if (x1.I1 + x1.Chg1 < x2.I1)
            {
                i0 = x1.I1; i1 = x1.I2; i2 = x2.I2 - x2.I1 + x1.I1;
                chg0 = x1.Chg1; chg1 = x1.Chg2; chg2 = x1.Chg1;
                AppendMerge(ref c, ref head, 1, i0, chg0, i1, chg1, i2, chg2);
                p1++;
                continue;
            }
            if (x2.I1 + x2.Chg1 < x1.I1)
            {
                i0 = x2.I1; i1 = x1.I2 - x1.I1 + x2.I1; i2 = x2.I2;
                chg0 = x2.Chg1; chg1 = x2.Chg1; chg2 = x2.Chg2;
                AppendMerge(ref c, ref head, 2, i0, chg0, i1, chg1, i2, chg2);
                p2++;
                continue;
            }
            if (level == MergeMinimal || x1.I1 != x2.I1 || x1.Chg1 != x2.Chg1 || x1.Chg2 != x2.Chg2 ||
                !CmpLines(xe1.F2, x1.I2, xe2.F2, x2.I2, x1.Chg2))
            {
                int off = x1.I1 - x2.I1;
                int ffo = off + x1.Chg1 - x2.Chg1;
                i0 = x1.I1; i1 = x1.I2; i2 = x2.I2;
                if (off > 0) { i0 -= off; i1 -= off; }
                else i2 += off;
                chg0 = x1.I1 + x1.Chg1 - i0;
                chg1 = x1.I2 + x1.Chg2 - i1;
                chg2 = x2.I2 + x2.Chg2 - i2;
                if (ffo < 0) { chg0 -= ffo; chg1 -= ffo; }
                else chg2 += ffo;
                AppendMerge(ref c, ref head, 0, i0, chg0, i1, chg1, i2, chg2);
            }
            i1 = x1.I1 + x1.Chg1;
            i2 = x2.I1 + x2.Chg1;
            if (i1 >= i2) p2++;
            if (i2 >= i1) p1++;
        }
        for (; p1 < s1.Count; p1++)
        {
            var x1 = s1[p1];
            i0 = x1.I1; i1 = x1.I2; i2 = x1.I1 + xe2.F2.NRec - xe2.F1.NRec;
            chg0 = x1.Chg1; chg1 = x1.Chg2; chg2 = x1.Chg1;
            AppendMerge(ref c, ref head, 1, i0, chg0, i1, chg1, i2, chg2);
        }
        for (; p2 < s2.Count; p2++)
        {
            var x2 = s2[p2];
            i0 = x2.I1; i1 = x2.I1 + xe1.F2.NRec - xe1.F1.NRec; i2 = x2.I2;
            chg0 = x2.Chg1; chg1 = x2.Chg1; chg2 = x2.Chg2;
            AppendMerge(ref c, ref head, 2, i0, chg0, i1, chg1, i2, chg2);
        }

        if (MergeZealous <= level)
        {
            RefineConflicts(xe1, xe2, head);
            SimplifyNonConflicts(xe1, head, MergeZealous < level);
        }

        FillMergeBuffer(xe1, name1, xe2, name2, ancestorName, head, result, diff3);

        int count = 0;
        for (var m = head; m != null; m = m.Next) if (m.Mode == 0) count++;
        return count;
    }

    static bool CmpLines(XdFile f1, int i1, XdFile f2, int i2, int n)
    {
        for (int i = 0; i < n; i++)
            if (!string.Equals(f1.Recs[i1 + i], f2.Recs[i2 + i], StringComparison.Ordinal)) return false;
        return true;
    }

    static void RefineConflicts(XdEnv xe1, XdEnv xe2, XdMerge m)
    {
        for (; m != null; m = m.Next)
        {
            int i1 = m.I1, i2 = m.I2;
            if (m.Mode != 0) continue;
            if (m.Chg1 == 0 || m.Chg2 == 0) continue;

            var t1 = new string[m.Chg1];
            Array.Copy(xe1.F2.Recs, m.I1, t1, 0, m.Chg1);
            var t2 = new string[m.Chg2];
            Array.Copy(xe2.F2.Recs, m.I2, t2, 0, m.Chg2);
            var xe = DoDiff(t1, t2);
            ChangeCompact(xe.F1, xe.F2);
            ChangeCompact(xe.F2, xe.F1);
            var xscr = BuildScript(xe);
            if (xscr.Count == 0)
            {
                m.Mode = 4;
                continue;
            }
            m.I1 = xscr[0].I1 + i1; m.Chg1 = xscr[0].Chg1;
            m.I2 = xscr[0].I2 + i2; m.Chg2 = xscr[0].Chg2;
            for (int k = 1; k < xscr.Count; k++)
            {
                var m2 = new XdMerge
                {
                    Next = m.Next, Mode = 0,
                    I1 = xscr[k].I1 + i1, Chg1 = xscr[k].Chg1,
                    I2 = xscr[k].I2 + i2, Chg2 = xscr[k].Chg2,
                };
                m.Next = m2;
                m = m2;
            }
        }
    }

    static bool LineContainsAlnum(string s)
    {
        foreach (char ch in s)
            if ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) return true;
        return false;
    }

    static bool LinesContainAlnum(XdEnv xe, int i, int chg)
    {
        for (; chg > 0; chg--, i++)   // C: for (; chg; ...) — chg is > 3 here, never negative
            if (LineContainsAlnum(xe.F2.Recs[i])) return true;
        return false;
    }

    static void SimplifyNonConflicts(XdEnv xe1, XdMerge m, bool simplifyIfNoAlnum)
    {
        if (m == null) return;
        for (; ; )
        {
            var next = m.Next;
            if (next == null) return;
            int begin = m.I1 + m.Chg1, end = next.I1;
            if (m.Mode != 0 || next.Mode != 0 ||
                (end - begin > 3 && (!simplifyIfNoAlnum || LinesContainAlnum(xe1, begin, end - begin))))
            {
                m = next;
            }
            else
            {
                // xdl_merge_two_conflicts
                m.Chg1 = next.I1 + next.Chg1 - m.I1;
                m.Chg2 = next.I2 + next.Chg2 - m.I2;
                m.Next = next.Next;
            }
        }
    }

    static void RecsCopy(StringBuilder sb, string[] recs, int i, int count, bool needsCr, bool addNl)
    {
        if (count < 1) return;
        for (int k = 0; k < count; k++) sb.Append(recs[i + k]);
        if (addNl)
        {
            var last = recs[i + count - 1];
            if (last.Length == 0 || last[last.Length - 1] != '\n')
            {
                if (needsCr) sb.Append('\r');
                sb.Append('\n');
            }
        }
    }

    static int IsEolCrlf(XdFile f, int i)
    {
        int size;
        if (i < f.NRec - 1)
            return (size = f.Recs[i].Length) > 1 && f.Recs[i][size - 2] == '\r' ? 1 : 0;
        if (f.NRec == 0) return -1;
        if ((size = f.Recs[i].Length) > 0 && f.Recs[i][size - 1] == '\n')
            return size > 1 && f.Recs[i][size - 2] == '\r' ? 1 : 0;
        if (i == 0) return -1;
        return (size = f.Recs[i - 1].Length) > 1 && f.Recs[i - 1][size - 2] == '\r' ? 1 : 0;
    }

    static bool IsCrNeeded(XdEnv xe1, XdEnv xe2, XdMerge m)
    {
        int needsCr = IsEolCrlf(xe1.F2, m.I1 != 0 ? m.I1 - 1 : 0);
        if (needsCr != 0) needsCr = IsEolCrlf(xe2.F2, m.I2 != 0 ? m.I2 - 1 : 0);
        if (needsCr != 0) needsCr = IsEolCrlf(xe1.F1, 0);
        return needsCr > 0;
    }

    static void Marker(StringBuilder sb, char ch, string name, bool needsCr)
    {
        sb.Append(ch, MarkerSize);
        if (name != null) { sb.Append(' '); sb.Append(name); }
        if (needsCr) sb.Append('\r');
        sb.Append('\n');
    }

    static void FillMergeBuffer(XdEnv xe1, string name1, XdEnv xe2, string name2, string ancestorName,
                                XdMerge m, StringBuilder sb, bool diff3)
    {
        int i = 0;
        string[] r1 = xe1.F2.Recs, r2 = xe2.F2.Recs, r0 = xe1.F1.Recs;
        for (; m != null; m = m.Next)
        {
            if (m.Mode == 0)
            {
                bool needsCr = IsCrNeeded(xe1, xe2, m);
                RecsCopy(sb, r1, i, m.I1 - i, false, false);
                Marker(sb, '<', name1, needsCr);
                RecsCopy(sb, r1, m.I1, m.Chg1, needsCr, true);
                if (diff3)
                {
                    Marker(sb, '|', ancestorName, needsCr);
                    RecsCopy(sb, r0, m.I0, m.Chg0, needsCr, true);
                }
                Marker(sb, '=', null, needsCr);
                RecsCopy(sb, r2, m.I2, m.Chg2, needsCr, true);
                Marker(sb, '>', name2, needsCr);
            }
            else if ((m.Mode & 3) != 0)
            {
                RecsCopy(sb, r1, i, m.I1 - i, false, false);
                if ((m.Mode & 1) != 0)
                {
                    bool needsCr = IsCrNeeded(xe1, xe2, m);
                    RecsCopy(sb, r1, m.I1, m.Chg1, needsCr, (m.Mode & 2) != 0);
                }
                if ((m.Mode & 2) != 0)
                    RecsCopy(sb, r2, m.I2, m.Chg2, false, false);
            }
            else continue;
            i = m.I1 + m.Chg1;
        }
        RecsCopy(sb, r1, i, xe1.F2.NRec - i, false, false);
    }
}
