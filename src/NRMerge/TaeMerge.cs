using SoulsFormats;

namespace NRMerge;

/// <summary>
/// Three-way TAE merge (Task 9): per animation ID; animations both mods changed get an event-level merge. A side's edit of a
/// base event is recognised as "removed base event + added event of the same type" (nearest start time); edits both sides
/// made to the same base event keep EV's version.
/// </summary>
public static class TaeMerge
{
    static string Key(TAE.Event e, bool be) => TaeSig.Event(e, be);
    static string Header(TAE.Animation a) => $"{TaeSig.MiniHeader(a)}|{a.AnimFileName}";

    sealed class SideView
    {
        public TAE.Event[] Kept;       // per base event: the side's identical event, or null
        public TAE.Event[] Modified;   // per base event: the side's edited version, or null
        public List<TAE.Event> Added = new();   // unpaired additions
    }

    static SideView View(List<TAE.Event> bas, List<TAE.Event> side, bool be)
    {
        var v = new SideView { Kept = new TAE.Event[bas.Count], Modified = new TAE.Event[bas.Count] };
        var free = side.ToList();
        for (int i = 0; i < bas.Count; i++)
        {
            var k = Key(bas[i], be);
            var hit = free.FirstOrDefault(x => Key(x, be) == k);
            if (hit != null) { v.Kept[i] = hit; free.Remove(hit); }
        }
        for (int i = 0; i < bas.Count; i++)
        {
            if (v.Kept[i] != null) continue;
            var cand = free.Where(x => x.Type == bas[i].Type && x.Group?.GroupType == bas[i].Group?.GroupType)
                .OrderBy(x => Math.Abs(x.StartTime - bas[i].StartTime)).FirstOrDefault();
            if (cand != null) { v.Modified[i] = cand; free.Remove(cand); }
        }
        v.Added = free;
        return v;
    }

    static TAE.Event Clone(TAE.Event src, bool srcBe, TAE.Animation into)
    {
        var e = new TAE.Event(src.StartTime, src.EndTime, src.Type, src.Unk04, src.GetParameterBytes(srcBe), srcBe);
        if (src.Group != null)
        {
            string G(TAE.EventGroup g) => $"{g.GroupType}/{g.GroupData.DataType}/{g.GroupData.CutsceneEntityType}/{g.GroupData.CutsceneEntityIDPart1}/{g.GroupData.CutsceneEntityIDPart2}";
            var g = into.EventGroups.FirstOrDefault(x => G(x) == G(src.Group));
            if (g == null) { g = src.Group.GetClone(); into.EventGroups.Add(g); }
            e.Group = g;
        }
        return e;
    }

    /// <summary>Event-level merge of an animation both sides changed; <paramref name="e"/> is modified and returned.</summary>
    public static TAE.Animation MergeAnimation(TAE.Animation b, TAE.Animation e, TAE.Animation m, bool be, List<string> conflicts)
    {
        string hb = Header(b), he = Header(e), hm = Header(m);
        if (he != hm && hm != hb)
        {
            if (he == hb) { e.MiniHeader = m.MiniHeader.GetClone(); e.AnimFileName = m.AnimFileName; }
            else conflicts.Add($"anim {e.ID} header: EV={he} MMV={hm} -> EV");
        }
        var vb = b.Events;
        var ve = View(vb, e.Events, be);
        var vm = View(vb, m.Events, be);
        var result = e.Events;
        var claimable = ve.Added.Concat(ve.Modified.Where(x => x != null)).ToList();
        for (int i = 0; i < vb.Count; i++)
        {
            if (vm.Kept[i] != null) continue;
            var mm = vm.Modified[i];
            string what = $"anim {e.ID} event type {vb[i].Type} @{vb[i].StartTime:R}";
            if (mm == null)   // MMV removed it
            {
                if (ve.Kept[i] != null) result.Remove(ve.Kept[i]);
                else if (ve.Modified[i] != null) conflicts.Add($"{what}: EV edited, MMV removed -> EV's edit");
                continue;
            }
            if (ve.Kept[i] != null) { result[result.IndexOf(ve.Kept[i])] = Clone(mm, be, e); continue; }
            if (ve.Modified[i] == null) { conflicts.Add($"{what}: EV removed, MMV edited -> removed (EV)"); continue; }
            if (Key(ve.Modified[i], be) == Key(mm, be)) continue;
            conflicts.Add($"{what}: both edited -> EV ({Key(ve.Modified[i], be)} vs MMV {Key(mm, be)})");
        }
        foreach (var add in vm.Added)
        {
            var k = Key(add, be);
            var same = claimable.FirstOrDefault(x => Key(x, be) == k);
            if (same != null) { claimable.Remove(same); continue; }
            result.Add(Clone(add, be, e));
        }
        return e;
    }

    /// <summary>
    /// Merges <paramref name="m"/>'s changes (vs <paramref name="b"/>) into <paramref name="e"/>. Animations missing from the base
    /// use <paramref name="fallbackBase"/> (e.g. the Elden Ring original) when it has them.
    /// </summary>
    public static TAE Merge(TAE b, TAE e, TAE m, out List<string> conflicts, TAE fallbackBase = null)
    {
        conflicts = new List<string>();
        bool be = e.BigEndian;
        var db = b.Animations.GroupBy(a => a.ID).ToDictionary(g => g.Key, g => g.First());
        var de = e.Animations.GroupBy(a => a.ID).ToDictionary(g => g.Key, g => g.First());
        var dm = m.Animations.GroupBy(a => a.ID).ToDictionary(g => g.Key, g => g.First());
        var df = fallbackBase?.Animations.GroupBy(a => a.ID).ToDictionary(g => g.Key, g => g.First()) ?? new();
        var result = new List<TAE.Animation>();
        foreach (var id in db.Keys.Union(de.Keys).Union(dm.Keys).OrderBy(x => x))
        {
            db.TryGetValue(id, out var ab); de.TryGetValue(id, out var ae); dm.TryGetValue(id, out var am);
            string S(TAE.Animation a) => a == null ? null : TaeSig.Animation(a, be);
            string sb = S(ab), se = S(ae), sm = S(am);
            if (ae == null && am == null) continue;
            if (ae == null) { if (ab == null || sm != sb) { result.Add(am); if (ab != null) conflicts.Add($"anim {id}: EV removed, MMV changed -> MMV's kept"); } continue; }
            if (am == null) { if (ab == null || se != sb) result.Add(ae); continue; }
            if (se == sm || sm == sb) { result.Add(ae); continue; }
            if (se == sb) { result.Add(am); continue; }
            var baseA = ab ?? (df.TryGetValue(id, out var af) ? af : null);
            if (baseA == null) { conflicts.Add($"anim {id}: added by both differently, no base -> EV"); result.Add(ae); continue; }
            result.Add(MergeAnimation(baseA, ae, am, be, conflicts));
        }
        e.Animations = result;
        return e;
    }
}
