using SoulsFormats;

namespace NRMerge;

/// <summary>
/// Behavior-judge collisions (Task 13): EV and MMV both put unrelated rows at the variation-0 fallback slots of the same
/// behavior judges (BehaviorParam_PC 9000003xx-8xx, 6000001xx). MMV's added TAE events that use those judges are moved to
/// free judges, and every BehaviorParam_PC row with an old judge is copied to the new judge (variation-0 fallback rows from
/// MMV), so MMV's events resolve exactly as in MMV while EV's events keep EV's rows.
/// </summary>
public static class JudgeRemap
{
    /// <summary>Byte offset of the behavior judge in the parameters of event types that invoke behaviors by judge, or -1.</summary>
    public static int JudgeOffset(int type) => type switch { 1 or 2 => 8, 5 or 304 => 4, _ => -1 };

    /// <summary>Maps each needed judge (ascending) to the lowest judge in 0..999 not in <paramref name="used"/>.</summary>
    public static Dictionary<int, int> Assign(IEnumerable<int> needed, ISet<int> used)
    {
        var free = new Queue<int>(Enumerable.Range(0, 1000).Where(j => !used.Contains(j)));
        var map = new Dictionary<int, int>();
        foreach (var j in needed.Distinct().OrderBy(x => x))
        {
            if (free.Count == 0) throw new InvalidOperationException($"no free behavior judge left for {j}");
            map[j] = free.Dequeue();
        }
        return map;
    }

    static int Judge(TAE.Event e, bool be)
    {
        int off = JudgeOffset(e.Type);
        var p = e.GetParameterBytes(be);
        return off < 0 || p.Length < off + 4 ? int.MinValue : BitConverter.ToInt32(p, off);
    }

    /// <summary>Judges used by events MMV added (not in the base or EV animation of the same ID).</summary>
    public static IEnumerable<int> MmvAddedJudges(TAE b, TAE e, TAE m)
    {
        foreach (var (a, ev) in MmvAdded(b, e, m)) yield return Judge(ev, m.BigEndian);
    }

    static IEnumerable<(TAE.Animation a, TAE.Event ev)> MmvAdded(TAE b, TAE e, TAE m)
    {
        var db = b?.Animations.GroupBy(x => x.ID).ToDictionary(g => g.Key, g => g.First()) ?? new();
        var de = e?.Animations.GroupBy(x => x.ID).ToDictionary(g => g.Key, g => g.First()) ?? new();
        foreach (var a in m.Animations)
        {
            var known = new Dictionary<string, int>();
            foreach (var src in new[] { db.GetValueOrDefault(a.ID), de.GetValueOrDefault(a.ID) })
                if (src != null)
                    foreach (var g in src.Events.GroupBy(x => TaeSig.Event(x, m.BigEndian)))
                        known[g.Key] = Math.Max(known.GetValueOrDefault(g.Key), g.Count());
            foreach (var ev in a.Events.ToList())
            {
                if (JudgeOffset(ev.Type) < 0) continue;
                var k = TaeSig.Event(ev, m.BigEndian);
                if (known.TryGetValue(k, out var n) && n > 0) { known[k] = n - 1; continue; }
                yield return (a, ev);
            }
        }
    }

    /// <summary>Rewrites the judges of MMV-added events in <paramref name="m"/> (modified in place and returned).</summary>
    public static TAE RewriteMmv(TAE b, TAE e, TAE m, Dictionary<int, int> map, out int changed)
    {
        changed = 0;
        foreach (var (a, ev) in MmvAdded(b, e, m).ToList())
        {
            int j = Judge(ev, m.BigEndian);
            if (!map.TryGetValue(j, out var nj)) continue;
            var p = ev.GetParameterBytes(m.BigEndian);
            BitConverter.GetBytes(nj).CopyTo(p, JudgeOffset(ev.Type));
            var repl = new TAE.Event(ev.StartTime, ev.EndTime, ev.Type, ev.Unk04, p, m.BigEndian) { Group = ev.Group };
            a.Events[a.Events.IndexOf(ev)] = repl;
            changed++;
        }
        return m;
    }
}
