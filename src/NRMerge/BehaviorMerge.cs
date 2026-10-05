using HKLib.hk2018;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NRMerge;

/// <summary>
/// Three-way Havok behavior graph merge (Task 10): MMV's changes against the base are applied onto EV's graph.
/// Named nodes (hkbNode with a name) are matched across graphs by type and name (duplicates by their named parents);
/// one-sided field changes are taken, list fields are merged by element identity (diff3), fields both mods changed
/// to different values keep EV's. MMV-only objects are imported with their references re-pointed at EV's instances.
/// </summary>
public static class BehaviorMerge
{
    sealed class Index
    {
        public readonly Dictionary<string, List<object>> ByKey = new();
        public readonly Dictionary<object, HashSet<string>> Parents = new(ReferenceEqualityComparer.Instance);
    }

    static Index Build(object root)
    {
        var ix = new Index();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object o, string owner)>();
        stack.Push((root, "<root>"));
        while (stack.Count > 0)
        {
            var (o, owner) = stack.Pop();
            foreach (var c in BehGraph.Children(o))
            {
                bool named = BehGraph.IsNamedNode(c);
                if (named)
                {
                    if (!ix.Parents.TryGetValue(c, out var ps)) ix.Parents[c] = ps = new HashSet<string>();
                    ps.Add(owner);
                }
                if (!seen.Add(c)) continue;
                if (named)
                {
                    var k = BehGraph.KeyOf(c);
                    if (!ix.ByKey.TryGetValue(k, out var l)) ix.ByKey[k] = l = new List<object>();
                    l.Add(c);
                }
                stack.Push((c, named ? BehGraph.KeyOf(c) : owner));
            }
        }
        return ix;
    }

    /// <summary>Pairs each named instance of <paramref name="x"/> with its counterpart in <paramref name="y"/>.</summary>
    static Dictionary<object, object> Match(Index x, Index y, Func<object, string> sig)
    {
        var map = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var (k, xs) in x.ByKey)
        {
            if (!y.ByKey.TryGetValue(k, out var ys)) continue;
            if (xs.Count == 1 && ys.Count == 1) { map[xs[0]] = ys[0]; continue; }
            var pairs = from a in xs from b in ys
                        let score = (sig(a) == sig(b) ? 1000 : 0) + x.Parents.GetValueOrDefault(a, new()).Intersect(y.Parents.GetValueOrDefault(b, new())).Count() * 10
                        orderby score descending select (a, b, score);
            var usedA = new HashSet<object>(ReferenceEqualityComparer.Instance); var usedB = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var (a, b, score) in pairs)
            {
                if (score == 0 || usedA.Contains(a) || usedB.Contains(b)) continue;
                map[a] = b; usedA.Add(a); usedB.Add(b);
            }
        }
        return map;
    }

    sealed class Ctx
    {
        public Dictionary<object, object> MtoE, MtoB, EtoB;
        public readonly Dictionary<object, object> Memo = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<object, string> SigCache = new(ReferenceEqualityComparer.Instance);
        public List<string> Notes;
        public int Imported, Merged, Conflicts;
        public IndexMaps Maps;

        public string Sig(object o)
        {
            if (o == null) return null;
            if (!SigCache.TryGetValue(o, out var s)) SigCache[o] = s = BehGraph.Sig(o);
            return s;
        }
    }

    static bool IsLeaf(object v) => v == null || v is string || v.GetType().IsPrimitive || v.GetType().IsEnum || (v.GetType().IsValueType && v is not IHavokObject);

    /// <summary>Brings an MMV-side value into EV's graph: named nodes EV has become EV's instances, everything else is imported.</summary>
    static object Tr(Ctx c, object v)
    {
        if (v == null || v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return v;
        if (v is IHavokObject)
        {
            if (BehGraph.IsNamedNode(v) && c.MtoE.TryGetValue(v, out var e)) return e;
            if (c.Memo.TryGetValue(v, out var done)) return done;
            c.Memo[v] = v;
            if (BehGraph.IsNamedNode(v)) c.Imported++;
            foreach (var f in BehGraph.Fields(v.GetType())) f.SetValue(v, c.Maps.Remap(v, f, Tr(c, f.GetValue(v))));
            return v;
        }
        if (v is IList list)
        {
            var copy = (IList)Activator.CreateInstance(v.GetType(), list.Count);
            if (v is Array arr) { for (int i = 0; i < arr.Length; i++) ((Array)copy).SetValue(Tr(c, arr.GetValue(i)), i); return copy; }
            foreach (var x in list) copy.Add(Tr(c, x));
            return copy;
        }
        if (v.GetType().IsValueType)
        {
            object boxed = v;
            foreach (var f in BehGraph.Fields(v.GetType())) f.SetValue(boxed, Tr(c, f.GetValue(boxed)));
            return boxed;
        }
        return v;
    }

    static string ValueSig(object v) => v == null ? "null" : BehGraph.IsNamedNode(v) ? "@" + BehGraph.KeyOf(v) : BehGraph.Sig(v);

    /// <summary>Identity of a list element for diff3: named nodes by key (unmatched MMV duplicates get a unique key), states by name.</summary>
    static string ElemKey(Ctx c, object x, bool mmvSide)
    {
        if (x == null) return "null";
        if (BehGraph.IsNamedNode(x))
            return mmvSide && !c.MtoE.ContainsKey(x) && !c.MtoB.ContainsKey(x) ? $"{BehGraph.KeyOf(x)}#new{RuntimeHelpers.GetHashCode(x)}" : BehGraph.KeyOf(x);
        if (x is hkbStateMachine.StateInfo s) return $"state:{s.m_name}:{s.m_stateId}";
        return ValueSig(x);
    }

    static object MergeList(Ctx c, IList b, IList e, IList m, string path)
    {
        var lb = (b ?? Array.Empty<object>()).Cast<object>().ToList(); var le = e.Cast<object>().ToList(); var lm = m.Cast<object>().ToList();
        var kb = lb.Select(x => ElemKey(c, x, false)).ToList(); var ke = le.Select(x => ElemKey(c, x, false)).ToList(); var km = lm.Select(x => ElemKey(c, x, true)).ToList();
        var keys = Seq3.Merge(kb, ke, km, k => k, out var cs);
        if (cs.Count > 0) c.Notes.Add($"{path}: both inserted at the same spot, EV's then MMV's ({cs.Count} hunk(s))");
        // A named node one side removed from this list while the other side edited it stays (with the editor's version).
        foreach (var k in kb.Distinct().Where(k => !keys.Contains(k)).ToList())
        {
            var xe = ke.Contains(k) ? le[ke.IndexOf(k)] : null; var xm = km.Contains(k) ? lm[km.IndexOf(k)] : null;
            bool eEdited = xe != null && BehGraph.IsNamedNode(xe) && c.EtoB.TryGetValue(xe, out var be) && c.Sig(xe) != c.Sig(be);
            bool mEdited = xm != null && BehGraph.IsNamedNode(xm) && c.MtoB.TryGetValue(xm, out var bm) && c.Sig(xm) != c.Sig(bm);
            if (!eEdited && !mEdited) continue;
            var order = eEdited ? ke : km;
            int at = 0;
            for (int i = order.IndexOf(k) - 1; i >= 0; i--) { int q = keys.IndexOf(order[i]); if (q >= 0) { at = q + 1; break; } }
            keys.Insert(at, k);
            c.Notes.Add($"{path}: {k} deleted by {(eEdited ? "MMV" : "EV")}, changed by {(eEdited ? "EV" : "MMV")} -> kept");
        }
        Queue<object> Q(List<string> ks, List<object> xs, string k) => new(xs.Where((_, i) => ks[i] == k));
        var qe = new Dictionary<string, Queue<object>>(); var qm = new Dictionary<string, Queue<object>>(); var qb = new Dictionary<string, Queue<object>>();
        var result = (IList)Activator.CreateInstance(e.GetType(), keys.Count);
        foreach (var k in keys)
        {
            if (!qe.ContainsKey(k)) { qe[k] = Q(ke, le, k); qm[k] = Q(km, lm, k); qb[k] = Q(kb, lb, k); }
            object xe = qe[k].Count > 0 ? qe[k].Dequeue() : null, xm = qm[k].Count > 0 ? qm[k].Dequeue() : null, xb = qb[k].Count > 0 ? qb[k].Dequeue() : null;
            if (xe != null)
            {
                // the same unnamed element (e.g. a state) changed inside by MMV: merge it
                if (xm != null && !BehGraph.IsNamedNode(xe) && xe is IHavokObject) result.Add(MergeValue(c, xb, xe, xm, $"{path}[{k}]"));
                else result.Add(xe);
            }
            else result.Add(Tr(c, xm));
        }
        return result;
    }

    static object MergeValue(Ctx c, object b, object e, object m, string path)
    {
        if (e is hkbBehaviorGraphData) return e;   // event/variable tables: extended by IndexMaps, never list-merged
        string sb = ValueSig(b), se = ValueSig(e), sm = ValueSig(m);
        if (se == sm || sm == sb) return e;
        // lists go through MergeList even when only MMV changed them: EV may have edited a named child MMV removed
        if (e is IList le0 && m is IList lm0 && e is not Array && e.GetType() == m.GetType()) return MergeList(c, b as IList, le0, lm0, path);
        if (se == sb) return Tr(c, m);
        if (e is IList le && m is IList lm && e is not Array && e.GetType() == m.GetType()) return MergeList(c, b as IList, le, lm, path);
        if (e is IHavokObject && m is IHavokObject && !BehGraph.IsNamedNode(e) && !BehGraph.IsNamedNode(m) && e.GetType() == m.GetType()
            && (b == null || b.GetType() == e.GetType()))
        {
            MergeFields(c, b, e, m, path);
            return e;
        }
        c.Conflicts++;
        c.Notes.Add($"{path}: EV={Short(se)} MMV={Short(sm)} -> EV");
        return e;
    }

    static void MergeFields(Ctx c, object b, object e, object m, string path)
    {
        foreach (var f in BehGraph.Fields(e.GetType()))
        {
            var ev = f.GetValue(e);
            var v = MergeValue(c, b == null ? null : f.GetValue(b), ev, f.GetValue(m), $"{path}.{f.Name}");
            if (v is int && !Equals(v, ev)) v = c.Maps.Remap(e, f, v);
            f.SetValue(e, v);
        }
    }

    static string Short(string s) => s.Length > 100 ? s[..100] + "…" : s;

    public static hkRootLevelContainer MergeGraphs(hkRootLevelContainer b, hkRootLevelContainer e, hkRootLevelContainer m, List<string> notes)
    {
        var ib = Build(b); var ie = Build(e); var im = Build(m);
        var anim = AnimIds.Snapshot(e, m);
        var c = new Ctx { Notes = notes, Maps = IndexMaps.Build(BehGraph.Graph(b)?.m_data, BehGraph.Graph(e)?.m_data, BehGraph.Graph(m)?.m_data, notes) };
        c.MtoE = Match(im, ie, c.Sig);
        c.MtoB = Match(im, ib, c.Sig);
        c.EtoB = Match(ie, ib, c.Sig);
        // snapshot signatures before anything is modified
        foreach (var x in im.ByKey.Values.SelectMany(v => v).Concat(ie.ByKey.Values.SelectMany(v => v)).Concat(ib.ByKey.Values.SelectMany(v => v))) c.Sig(x);

        foreach (var x in im.ByKey.Values.SelectMany(v => v))
        {
            if (!c.MtoE.TryGetValue(x, out var y)) continue;          // MMV-only: imported when referenced
            c.MtoB.TryGetValue(x, out var z);
            if (z == null && c.EtoB.TryGetValue(y, out var zz)) z = zz;
            string sx = c.Sig(x), sy = c.Sig(y), sz = c.Sig(z);
            if (sx == sy || sx == sz) continue;
            c.Merged++;
            var key = BehGraph.KeyOf(y);
            if (z == null) notes.Add($"{key}: added by both differently: field merge without base");
            MergeFields(c, z, y, x, key);
        }

        // the root container itself (named variants) — normally unchanged
        MergeFields(c, b, e, m, "root");
        anim.Apply(e, notes);
        c.Maps.Report();
        notes.Insert(0, $"merged {c.Merged} node(s) MMV changed, imported {c.Imported} MMV node(s), {c.Conflicts} field conflict(s) kept EV");
        return e;
    }

    static string Stem(string path) => Path.GetFileNameWithoutExtension((path ?? "").Replace('\\', '/')).ToLowerInvariant();

    /// <summary>
    /// MMV event/variable indices -> EV's, by name. Names only MMV has are appended to EV's tables (with their info, bounds and
    /// initial value), so EV's own indices never move.
    /// </summary>
    sealed class IndexMaps
    {
        const int Pending = -2;
        int[] ev = Array.Empty<int>(), var = Array.Empty<int>();
        bool identity = true;
        hkbBehaviorGraphData e, m;
        readonly List<string> appendedE = new(), appendedV = new();
        List<string> notes;

        static readonly HashSet<string> EventFields = new()
        {
            "m_eventId", "m_enterEventId", "m_exitEventId", "m_randomTransitionEventId", "m_returnToPreviousStateEventId",
            "m_transitionToNextHigherStateEventId", "m_transitionToNextLowerStateEventId", "m_offEventId", "m_onEventId",
            "m_activateEventId", "m_deactivateEventId", "m_assignmentEventIndex", "m_endOfClipEventId", "m_startMatchingEventId",
            "m_startPlayingEventId", "m_disableEventId", "m_enableEventId",
        };
        static readonly HashSet<string> VariableFields = new() { "m_variableIndex", "m_syncVariableIndex", "m_assignmentVariableIndex" };

        /// <summary>
        /// Maps by name. Names MMV added (absent from the base) are appended to EV's tables now (HKS may fire/read them by name);
        /// base names EV removed are appended only if MMV content that is imported refers to them.
        /// </summary>
        public static IndexMaps Build(hkbBehaviorGraphData b, hkbBehaviorGraphData e, hkbBehaviorGraphData m, List<string> notes)
        {
            var x = new IndexMaps { e = e, m = m, notes = notes };
            if (e?.m_stringData == null || m?.m_stringData == null) return x;
            var baseE = (b?.m_stringData?.m_eventNames ?? new()).ToHashSet();
            var baseV = (b?.m_stringData?.m_variableNames ?? new()).ToHashSet();
            var at = new Dictionary<string, int>();
            for (int i = 0; i < e.m_stringData.m_eventNames.Count; i++) at.TryAdd(e.m_stringData.m_eventNames[i] ?? "", i);
            var mn = m.m_stringData.m_eventNames;
            x.ev = new int[mn.Count];
            for (int i = 0; i < mn.Count; i++)
                x.ev[i] = at.TryGetValue(mn[i] ?? "", out var j) ? j : baseE.Contains(mn[i]) ? Pending : x.AppendEvent(i);
            var vat = new Dictionary<string, int>();
            for (int i = 0; i < e.m_stringData.m_variableNames.Count; i++) vat.TryAdd(e.m_stringData.m_variableNames[i] ?? "", i);
            var vm = m.m_stringData.m_variableNames;
            x.var = new int[vm.Count];
            for (int i = 0; i < vm.Count; i++)
                x.var[i] = vat.TryGetValue(vm[i] ?? "", out var j) ? j : baseV.Contains(vm[i]) ? Pending : x.AppendVariable(i);
            x.identity = x.ev.Select((v, i) => v == i).All(t => t) && x.var.Select((v, i) => v == i).All(t => t);
            if (!e.m_stringData.m_characterPropertyNames.SequenceEqual(m.m_stringData.m_characterPropertyNames))
                notes.Add("WARNING: character property names differ between EV and MMV; character-property bindings are not remapped");
            return x;
        }

        int AppendEvent(int i)
        {
            e.m_stringData.m_eventNames.Add(m.m_stringData.m_eventNames[i]);
            if (i < m.m_eventInfos.Count) e.m_eventInfos.Add(m.m_eventInfos[i]);
            appendedE.Add(m.m_stringData.m_eventNames[i]);
            return e.m_stringData.m_eventNames.Count - 1;
        }

        int AppendVariable(int i)
        {
            e.m_stringData.m_variableNames.Add(m.m_stringData.m_variableNames[i]);
            if (i < m.m_variableInfos.Count) e.m_variableInfos.Add(m.m_variableInfos[i]);
            if (i < m.m_variableBounds.Count) e.m_variableBounds.Add(m.m_variableBounds[i]);
            var mv = m.m_variableInitialValues; var evs = e.m_variableInitialValues ??= new hkbVariableValueSet();
            if (mv != null && i < mv.m_wordVariableValues.Count)
            {
                int word = mv.m_wordVariableValues[i].m_value;
                var type = i < m.m_variableInfos.Count ? m.m_variableInfos[i].m_type : hkbVariableInfo.VariableType.VARIABLE_TYPE_INT32;
                if (type is hkbVariableInfo.VariableType.VARIABLE_TYPE_VECTOR3 or hkbVariableInfo.VariableType.VARIABLE_TYPE_VECTOR4 or hkbVariableInfo.VariableType.VARIABLE_TYPE_QUATERNION
                    && word >= 0 && word < mv.m_quadVariableValues.Count)
                { evs.m_quadVariableValues.Add(mv.m_quadVariableValues[word]); word = evs.m_quadVariableValues.Count - 1; }
                else if (type == hkbVariableInfo.VariableType.VARIABLE_TYPE_POINTER && word >= 0 && word < mv.m_variantVariableValues.Count)
                { evs.m_variantVariableValues.Add(mv.m_variantVariableValues[word]); word = evs.m_variantVariableValues.Count - 1; }
                evs.m_wordVariableValues.Add(new hkbVariableValue { m_value = word });
            }
            appendedV.Add(m.m_stringData.m_variableNames[i]);
            return e.m_stringData.m_variableNames.Count - 1;
        }

        public void Report()
        {
            if (appendedE.Count + appendedV.Count > 0 || !identity)
                notes.Add($"event/variable indices remapped by name: events appended [{string.Join(",", appendedE)}], variables appended [{string.Join(",", appendedV)}]");
        }

        /// <summary>Maps an MMV-side value of <paramref name="f"/> (owned by <paramref name="owner"/>) into EV's index space.</summary>
        public object Remap(object owner, FieldInfo f, object value)
        {
            if (identity || value is not int i || i < 0) return value;
            if (EventFields.Contains(f.Name) || (owner is hkbEventBase && f.Name == "m_id"))
            {
                if (i >= ev.Length) return value;
                if (ev[i] == Pending) ev[i] = AppendEvent(i);
                return ev[i];
            }
            if (VariableFields.Contains(f.Name))
            {
                if (owner is hkbVariableBindingSet.Binding bnd && bnd.m_bindingType != hkbVariableBindingSet.Binding.BindingType.BINDING_TYPE_VARIABLE) return value;
                if (i >= var.Length) return value;
                if (var[i] == Pending) var[i] = AppendVariable(i);
                return var[i];
            }
            return value;
        }
    }

    /// <summary>
    /// Clip m_animationInternalId values: one id per distinct animation. Vanilla ids are indices into the behavior's animation-name
    /// table; mods number new animations after it (EV's c0000 leaves the table alone, ER-port enemies append to it). EV's ids are
    /// kept; animations only MMV uses get fresh ids after the highest id in use, appended to the table when the table is dense.
    /// </summary>
    sealed class AnimIds
    {
        List<string> evTable;
        readonly Dictionary<string, short> evIds = new();
        readonly Dictionary<string, string> mmvPaths = new();

        public static AnimIds Snapshot(hkRootLevelContainer e, hkRootLevelContainer m)
        {
            var a = new AnimIds();
            a.evTable = BehGraph.Graph(e)?.m_data?.m_stringData?.m_animationNames?.ToList();
            foreach (var clip in BehGraph.AllObjects(e).OfType<hkbClipGenerator>())
                a.evIds.TryAdd((clip.m_animationName ?? "").ToLowerInvariant(), clip.m_animationInternalId);
            var mt = BehGraph.Graph(m)?.m_data?.m_stringData?.m_animationNames;
            if (mt != null)
                foreach (var clip in BehGraph.AllObjects(m).OfType<hkbClipGenerator>())
                    if (clip.m_animationInternalId >= 0 && clip.m_animationInternalId < mt.Count && Stem(mt[clip.m_animationInternalId]) == (clip.m_animationName ?? "").ToLowerInvariant())
                        a.mmvPaths.TryAdd(Stem(mt[clip.m_animationInternalId]), mt[clip.m_animationInternalId]);
            return a;
        }

        public void Apply(hkRootLevelContainer root, List<string> notes)
        {
            var sd = BehGraph.Graph(root)?.m_data?.m_stringData;
            if (sd == null || evTable == null) return;
            sd.m_animationNames = evTable.ToList();
            var table = sd.m_animationNames;
            var byStem = new Dictionary<string, short>();
            for (int i = 0; i < table.Count; i++) byStem.TryAdd(Stem(table[i]), (short)i);
            int next = Math.Max(table.Count, evIds.Count == 0 ? 0 : evIds.Values.Max() + 1);
            var fresh = new Dictionary<string, short>();
            int changed = 0, appended = 0;
            foreach (var clip in BehGraph.AllObjects(root).OfType<hkbClipGenerator>())
            {
                var name = (clip.m_animationName ?? "").ToLowerInvariant();
                if (!evIds.TryGetValue(name, out var id) && !byStem.TryGetValue(name, out id) && !fresh.TryGetValue(name, out id))
                {
                    id = (short)next++;
                    fresh[name] = id;
                    if (id == table.Count) { table.Add(mmvPaths.GetValueOrDefault(name, name)); appended++; }
                }
                if (clip.m_animationInternalId != id) { clip.m_animationInternalId = id; changed++; }
            }
            notes.Add($"animation ids: {fresh.Count} MMV-only animation(s) numbered {(fresh.Count == 0 ? "-" : $"{fresh.Values.Min()}..{fresh.Values.Max()}")}, {changed} clip id(s) changed, {appended} name(s) appended to the table");
        }
    }

    public static byte[] Merge(byte[] baseHkx, byte[] evHkx, byte[] mmvHkx, out List<string> notes)
    {
        notes = new List<string>();
        var b = BehGraph.Load(baseHkx); var e = BehGraph.Load(evHkx); var m = BehGraph.Load(mmvHkx);
        if (e.Nightreign != m.Nightreign) notes.Add($"type flavour differs (EV nightreign={e.Nightreign}, MMV nightreign={m.Nightreign}); written as EV's");
        var r = MergeGraphs(b.Root, e.Root, m.Root, notes);
        var bytes = BehGraph.Save(e with { Root = r });
        BehGraph.Load(bytes);   // must re-read
        return bytes;
    }
}
