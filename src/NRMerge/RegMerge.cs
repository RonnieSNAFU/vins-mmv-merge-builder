using Andre.Formats;
using SoulsFormats;

namespace NRMerge;

/// <summary>Stage 2: three-way merge of regulation.bin (spec §6).</summary>
public static class RegMerge
{
    public static bool Same(object a, object b)
    {
        if (ParamDiff.ValEq(a, b)) return true;
        return a != null && b != null && Regulation.Fmt(a) == Regulation.Fmt(b);
    }

    /// <summary>Reference-like fields (IDs, lots) whose -1/0 value means "none".</summary>
    public static bool IsRefField(string field) =>
        field.Contains("Id", StringComparison.Ordinal) || field.Contains("ID", StringComparison.Ordinal) || field.Contains("Lot", StringComparison.Ordinal);

    static bool IsUnset(object v) => v != null && Regulation.Fmt(v) is "-1" or "0";

    /// <summary>
    /// Three-way merge of one field value. Returns EV's or MMV's value, never the base's. For reference fields a
    /// "none" value never beats a real reference, whichever side the policy prefers.
    /// </summary>
    public static object MergeField(object baseV, object evV, object mmvV, Side winner, out bool conflict, bool isRef = false)
    {
        conflict = false;
        if (Same(evV, mmvV)) return evV;
        if (baseV != null && Same(evV, baseV)) return mmvV;
        if (baseV != null && Same(mmvV, baseV)) return evV;
        conflict = true;
        var (win, lose) = winner == Side.EV ? (evV, mmvV) : (mmvV, evV);
        if (isRef && IsUnset(win) && !IsUnset(lose)) return lose;
        return win;
    }

    /// <summary>Applies both mods' changed fields onto the base values of a row.</summary>
    public static Dictionary<string, object> MergeRowFields(IReadOnlyDictionary<string, object> baseVals,
        IReadOnlyDictionary<string, object> evChanges, IReadOnlyDictionary<string, object> mmvChanges,
        Func<string, Side> winner, out List<string> conflicts)
    {
        var result = new Dictionary<string, object>(baseVals);
        conflicts = new List<string>();
        foreach (var f in evChanges.Keys.Union(mmvChanges.Keys))
        {
            baseVals.TryGetValue(f, out var b);
            var e = evChanges.TryGetValue(f, out var ev) ? ev : b;
            var m = mmvChanges.TryGetValue(f, out var mv) ? mv : b;
            result[f] = MergeField(b, e, m, winner(f), out var c);
            if (c) conflicts.Add(f);
        }
        return result;
    }

    public sealed class Context
    {
        public Regulation.Loaded Cur, EvBase, Ev, Mmv, Er;
        public ParamDeltaSet DEv, DMmv;
        public SortedDictionary<string, int> Stats = new(StringComparer.Ordinal);
        public List<(string Param, RowKey Key, Param.Row MmvRow)> Collisions = new();
        /// <summary>Keys of rows in the merged output that exist only because MMV added them.</summary>
        public Dictionary<string, HashSet<RowKey>> MmvOnlyAdded = new(StringComparer.Ordinal);
        public Dictionary<string, Param> Merged = new(StringComparer.Ordinal);
        public void Count(string k, int n = 1) => Stats[k] = Stats.GetValueOrDefault(k) + n;
    }

    public static Context Load()
    {
        var c = new Context
        {
            Cur = Regulation.Load(Path.Combine(Paths.GameNR, "regulation.bin")),
            EvBase = Regulation.Load(Regulation.VanillaRegulationForVersion("10340000")),
            Ev = Regulation.Load(Path.Combine(Paths.EV, "regulation.bin")),
            Mmv = Regulation.Load(Path.Combine(Paths.MMV, "regulation.bin")),
            Er = Paths.HasEldenRing ? Regulation.Load(Path.Combine(Paths.GameER, "regulation.bin"), "ER") : new Regulation.Loaded { Game = "ER", Path = "(no Elden Ring)" },
        };
        if (!Paths.HasEldenRing)
            ErFallback.Note("regulation.bin (Elden Ring)", "no Elden Ring regulation: ER-ported rows recognised by the Andre dictionaries only; EV's ER-port HP scaling not derived from ER values; ER row IDs not reserved when moving colliding MMV rows");
        foreach (var l in new[] { c.Cur, c.EvBase, c.Ev, c.Mmv, c.Er })
            if (l.Errors.Count > 0) throw new InvalidDataException($"{l.Path}: {string.Join("; ", l.Errors)}");
        c.DEv = ParamDiff.Compute("EV", c.EvBase, c.Ev);
        RemapCollisions(c);
        c.DMmv = ParamDiff.Compute("MMV", c.Cur, c.Mmv);
        return c;
    }

    /// <summary>Params whose rows sharing an ID form weighted lottery tables (merged by content, spec §5).</summary>
    public static readonly HashSet<string> TableParams = new(StringComparer.Ordinal)
    {
        "ItemTableParam", "MagicTableParam", "SwordArtsTableParam", "AttachEffectTableParam",
    };

    /// <summary>Fields that identify what a row *is*; if both mods filled them differently the rows are unrelated content.</summary>
    static readonly Dictionary<string, string[]> IdentityFields = new(StringComparer.Ordinal)
    {
        ["EquipParamWeapon"] = new[] { "equipModelId" },
        ["EquipParamProtector"] = new[] { "equipModelId", "equipModelCategory" },
        ["EquipParamCustomWeapon"] = new[] { "targetWeaponId" },
    };

    /// <summary>Row-ID step used when moving colliding MMV rows (keeps weapon/armor ID conventions).</summary>
    static int RemapStep(string pn) => pn switch
    {
        "EquipParamWeapon" or "EquipParamCustomWeapon" or "EquipParamProtector" => 10000,
        _ => 1,
    };

    /// <summary>Params whose IDs are derived from other data (TAE behavior judges), so they cannot be moved here.</summary>
    static readonly HashSet<string> NonMovable = new(StringComparer.Ordinal) { "BehaviorParam_PC", "BehaviorParam" };

    /// <summary>Params whose row IDs encode placement (shop slot ranges): a collision is settled by §5 policy, not by moving.</summary>
    static readonly HashSet<string> ResolveByPolicy = new(StringComparer.Ordinal) { "ShopLineupParam" };

    /// <summary>
    /// Decides whether two rows both mods added under one ID are unrelated content. Without a common Elden Ring
    /// original, differing rows are always unrelated (in this pair of mods every such row is a different effect,
    /// enemy, bullet or weapon); with an ER original they are the same port unless an identity field differs or most
    /// fields differ.
    /// </summary>
    public static bool IsCollisionCore(string pn, IReadOnlyDictionary<string, object> e, IReadOnlyDictionary<string, object> m, bool hasErBase, out string why)
    {
        why = null;
        if (IdentityFields.TryGetValue(pn, out var idf))
            foreach (var f in idf)
                if (e.TryGetValue(f, out var a) && m.TryGetValue(f, out var b) && !Same(a, b))
                {
                    why = $"identity field {f} differs ({Regulation.Fmt(a)} vs {Regulation.Fmt(b)})";
                    return true;
                }
        if (!hasErBase) { why = "no common Elden Ring original and the rows differ"; return true; }
        int same = 0, total = 0;
        foreach (var (f, v) in e)
        {
            if (f.StartsWith("pad", StringComparison.OrdinalIgnoreCase) || f.StartsWith("unknown", StringComparison.OrdinalIgnoreCase)) continue;
            if (!m.TryGetValue(f, out var mv)) continue;
            total++;
            if (Same(v, mv)) same++;
        }
        if (total > 0 && same * 2 < total) { why = $"{same}/{total} fields equal"; return true; }
        return false;
    }

    static Dictionary<string, object> Values(Param.Row r, Dictionary<string, Param.Column> cols)
    {
        var d = new Dictionary<string, object>(cols.Count, StringComparer.Ordinal);
        foreach (var (f, c) in cols) d[f] = c.GetValue(r);
        return d;
    }

    public static bool IsCollision(string pn, Param.Row e, Param.Row m, Dictionary<string, Param.Column> ce, Dictionary<string, Param.Column> cm, bool hasErBase, out string why)
        => IsCollisionCore(pn, Values(e, ce), Values(m, cm), hasErBase, out why);

    /// <summary>Whether a row both mods added has a common Elden Ring original. Without Elden Ring that is unknown: the row is
    /// assumed to have one, so the rows are compared by field similarity (and merged field by field, policy winner on
    /// conflicts) instead of every such row being declared an unrelated collision; journaled.</summary>
    public static bool HasErOriginal(string pn, int id, bool inEr)
    {
        if (Paths.HasEldenRing) return inEr;
        ErFallback.Note($"{pn} {id}", "no Elden Ring regulation: row added by both mods compared by field similarity as if it had an Elden Ring original (no ER base values: field conflicts go to the policy winner)");
        return true;
    }

    public sealed record Remap(string Param, int Old, int New, string MmvName, string EvName);

    /// <summary>
    /// Spec §5 true ID collisions: EV keeps the ID; MMV's row moves to a free ID (same offset for all collisions in a
    /// param, so armor sets stay aligned) and every reference inside MMV's params is rewritten before the merge.
    /// </summary>
    static void RemapCollisions(Context c)
    {
        var pre = ParamDiff.Compute("MMV", c.Cur, c.Mmv);
        var remaps = new List<Remap>();
        foreach (var (pn, rm) in pre.Params)
        {
            if (TableParams.Contains(pn) || !c.DEv.Params.TryGetValue(pn, out var re)) continue;
            var ce = ColMap(c.Ev.Params[pn]);
            var cm = ColMap(c.Mmv.Params[pn]);
            c.Er.Params.TryGetValue(pn, out var erP0);
            var erIds = erP0 != null ? erP0.Rows.Select(r => r.ID).ToHashSet() : new HashSet<int>();
            var hits = new List<(int Id, Param.Row M, Param.Row E, string Why)>();
            foreach (var (k, dm) in rm)
            {
                if (!dm.Added || k.Occ != 0 || !re.TryGetValue(k, out var de) || !de.Added) continue;
                if (de.SourceRow.DataEquals(dm.SourceRow)) continue;
                if (IsCollision(pn, de.SourceRow, dm.SourceRow, ce, cm, HasErOriginal(pn, k.ID, erIds.Contains(k.ID)), out var why)) hits.Add((k.ID, dm.SourceRow, de.SourceRow, why));
            }
            if (hits.Count == 0 || ResolveByPolicy.Contains(pn)) continue;
            if (NonMovable.Contains(pn))
            {
                foreach (var h in hits)
                    Journal.Add("regulation-pending", $"{pn} {h.Id}", $"unrelated rows ({h.Why}); EV keeps the ID for now; MMV row '{h.M.Name}' must be re-pointed from its TAE behavior judge (player stage)");
                continue;
            }
            var used = new HashSet<int>();
            foreach (var l in new[] { c.Cur, c.Ev, c.Mmv, c.Er })
                if (l.Params.TryGetValue(pn, out var p)) foreach (var r in p.Rows) used.Add(r.ID);
            int off = IdRemap.FindFreeOffset(hits.Select(h => h.Id), used.Contains, RemapStep(pn));
            foreach (var h in hits)
            {
                int newId = h.Id + off;
                h.M.ID = newId;
                int refs = 0;
                foreach (var (_, mp) in c.Mmv.Params) refs += IdRemap.RewriteParam(mp, pn, h.Id, newId);
                remaps.Add(new Remap(pn, h.Id, newId, h.M.Name, h.E.Name));
                Journal.Add("regulation", $"{pn} {h.Id}", $"ID collision ({h.Why}): EV keeps '{h.E.Name}'; MMV '{h.M.Name}' moved to {newId}; {refs} MMV references rewritten");
                c.Count("rows-remapped");
            }
            var mmP = c.Mmv.Params[pn];
            mmP.Rows = mmP.Rows.OrderBy(r => r.ID).ToList();
        }
        Directory.CreateDirectory(Paths.Out);
        File.WriteAllText(Path.Combine(Paths.Out, "remap.json"), System.Text.Json.JsonSerializer.Serialize(remaps, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public static int Run()
    {
        Journal.Clear();
        var c = Load();
        MergeAll(c);
        var rules = EvRules.Derive(c);
        foreach (var r in rules) Console.WriteLine("rule: " + r.Describe());
        EvRules.Apply(c, rules);
        Write(c, Path.Combine(Paths.OutMod, "regulation.bin"));
        Journal.Save();
        foreach (var (k, v) in c.Stats) Console.WriteLine($"{k}\t{v}");
        return 0;
    }

    public static void MergeAll(Context c)
    {
        foreach (var (pn, vp) in c.Cur.Params)
        {
            c.DEv.Params.TryGetValue(pn, out var re);
            c.DMmv.Params.TryGetValue(pn, out var rm);
            if ((re == null || re.Count == 0) && (rm == null || rm.Count == 0)) continue;
            c.Merged[pn] = TableParams.Contains(pn) ? MergeTable(c, pn, vp) : MergeParam(c, pn, vp, re ?? new(), rm ?? new());
        }
    }

    static Dictionary<string, Param.Column> ColMap(Param p)
    {
        var d = new Dictionary<string, Param.Column>(StringComparer.Ordinal);
        if (p == null) return d;
        foreach (var col in p.Columns) d.TryAdd(col.Def.InternalName, col);
        return d;
    }

    static Param MergeParam(Context c, string pn, Param vp, Dictionary<RowKey, RowDelta> re, Dictionary<RowKey, RowDelta> rm)
    {
        var outP = new Param(vp);
        var outCols = ColMap(outP);
        var rows = new List<Param.Row>(vp.Rows.Count + re.Count + rm.Count);
        var vkeys = new HashSet<RowKey>();
        var occ = new Dictionary<int, int>();

        foreach (var vrow in vp.Rows)
        {
            occ.TryGetValue(vrow.ID, out var n);
            occ[vrow.ID] = n + 1;
            var k = new RowKey(vrow.ID, n);
            vkeys.Add(k);
            re.TryGetValue(k, out var dE);
            rm.TryGetValue(k, out var dM);
            if (dE == null && dM == null) { rows.Add(new Param.Row(vrow, outP)); continue; }

            bool remE = dE?.Removed == true, remM = dM?.Removed == true;
            if (remE || remM)
            {
                var other = remE ? dM : dE;
                if (other == null || other.Removed)
                {
                    Journal.Add("regulation", $"{pn} {k}", $"row removed by {(remE && remM ? "both" : remE ? "EV" : "MMV")}");
                    c.Count("rows-removed");
                    continue;
                }
                Journal.Add("regulation", $"{pn} {k}", $"removed by {(remE ? "EV" : "MMV")} but modified by the other: kept");
                c.Count("removal-overridden");
                if (remE) dE = null; else dM = null;
            }

            var nrow = new Param.Row(vrow, outP);
            var fields = (dE?.Fields.Keys ?? Enumerable.Empty<string>()).Union(dM?.Fields.Keys ?? Enumerable.Empty<string>());
            bool any = false;
            foreach (var f in fields)
            {
                if (!outCols.TryGetValue(f, out var col)) continue;
                var b = col.GetValue(vrow);
                var e = dE != null && dE.Fields.TryGetValue(f, out var ev) ? ev : b;
                var m = dM != null && dM.Fields.TryGetValue(f, out var mv) ? mv : b;
                var v = MergeField(b, e, m, MergePolicy.Winner(pn, f), out var conflict, IsRefField(f));
                if (conflict)
                {
                    Journal.Add("regulation", $"{pn} {k} {f}", $"both changed: base={Regulation.Fmt(b)} EV={Regulation.Fmt(e)} MMV={Regulation.Fmt(m)} -> {(Same(v, e) ? "EV" : "MMV")}");
                    c.Count("field-conflicts");
                }
                if (!Same(v, b)) { col.SetValue(nrow, v); any = true; }
            }
            c.Count(dE != null && dM != null ? "rows-modified-both" : dE != null ? "rows-modified-EV" : "rows-modified-MMV");
            rows.Add(nrow);
        }

        var evP = c.Ev.Params[pn];
        var mmP = c.Mmv.Params[pn];
        c.Er.Params.TryGetValue(pn, out var erP);
        var erIndex = erP != null ? ParamDiff.Index(erP) : new Dictionary<RowKey, Param.Row>();
        var evCols = ColMap(evP);
        var mmCols = ColMap(mmP);
        var erCols = ColMap(erP);
        var mmvOnly = c.MmvOnlyAdded.TryGetValue(pn, out var mo) ? mo : c.MmvOnlyAdded[pn] = new HashSet<RowKey>();

        var addKeys = re.Keys.Where(k => !vkeys.Contains(k)).Union(rm.Keys.Where(k => !vkeys.Contains(k)))
            .OrderBy(k => k.ID).ThenBy(k => k.Occ).ToList();
        foreach (var k in addKeys)
        {
            re.TryGetValue(k, out var aE);
            rm.TryGetValue(k, out var aM);
            var srcE = aE != null && !aE.Removed ? aE.SourceRow : null;
            var srcM = aM != null && !aM.Removed ? aM.SourceRow : null;
            if (srcE == null && srcM == null) continue;
            if (srcM == null) { rows.Add(new Param.Row(srcE, outP)); c.Count("rows-added-EV"); continue; }
            if (srcE == null) { rows.Add(new Param.Row(srcM, outP)); mmvOnly.Add(k); c.Count("rows-added-MMV"); continue; }

            if (srcE.DataEquals(srcM)) { rows.Add(new Param.Row(srcE, outP)); c.Count("rows-added-both-identical"); continue; }

            erIndex.TryGetValue(k, out var erRow);
            if (IsCollision(pn, srcE, srcM, evCols, mmCols, HasErOriginal(pn, k.ID, erRow != null), out var whyC))
            {
                if (ResolveByPolicy.Contains(pn))
                {
                    var keep = MergePolicy.Winner(pn, "") == Side.EV ? srcE : srcM;
                    rows.Add(new Param.Row(keep, outP));
                    if (keep == srcM) mmvOnly.Add(k);
                    Journal.Add("regulation", $"{pn} {k}", $"unrelated rows ({whyC}); slot kept by policy winner {MergePolicy.Winner(pn, "")}: '{keep.Name}'");
                    c.Count("rows-collision-policy");
                    continue;
                }
                rows.Add(new Param.Row(srcE, outP));
                c.Collisions.Add((pn, k, srcM));
                Journal.Add("regulation", $"{pn} {k}", $"ID collision ({whyC}); EV keeps the ID; MMV row '{srcM.Name}' left out (see regulation-pending)");
                c.Count("rows-collision-unresolved");
                continue;
            }

            var nrow = new Param.Row(srcE, outP);
            foreach (var (f, col) in outCols)
            {
                var e = evCols[f].GetValue(srcE);
                var m = mmCols[f].GetValue(srcM);
                object b = erRow != null && erCols.TryGetValue(f, out var ec) ? ec.GetValue(erRow) : null;
                var v = MergeField(b, e, m, MergePolicy.Winner(pn, f), out var conflict, IsRefField(f));
                if (conflict)
                {
                    Journal.Add("regulation", $"{pn} {k} {f}", $"both added, differ: {(erRow != null ? "ER=" + Regulation.Fmt(b) : "no ER base")} EV={Regulation.Fmt(e)} MMV={Regulation.Fmt(m)} -> {(Same(v, e) ? "EV" : "MMV")}");
                    c.Count(erRow != null ? "added-both-field-conflicts-erbase" : "added-both-field-conflicts-nobase");
                }
                if (!Same(v, e)) col.SetValue(nrow, v);
            }
            rows.Add(nrow);
            c.Count(erRow != null ? "rows-added-both-merged-erbase" : "rows-added-both-merged-nobase");
        }

        outP.Rows = rows.OrderBy(r => r.ID).ToList();
        return outP;
    }

    /// <summary>Merges a lottery-table param: each table ID is a sequence of entries merged by content (spec §5).</summary>
    static Param MergeTable(Context c, string pn, Param vp)
    {
        var outP = new Param(vp);
        var outCols = ColMap(outP);
        const string w1 = "chanceWeight";
        string w2 = outCols.ContainsKey("chanceWeight_dlc") ? "chanceWeight_dlc" : null;

        Dictionary<int, List<TableMerge.Entry>> Group(Param p)
        {
            var cols = ColMap(p);
            var keyCols = cols.Where(kv => kv.Key != w1 && kv.Key != w2).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
            var d = new Dictionary<int, List<TableMerge.Entry>>();
            foreach (var r in p.Rows)
            {
                var key = string.Join("|", keyCols.Select(col => Regulation.Fmt(col.GetValue(r))));
                int a = Convert.ToInt32(cols[w1].GetValue(r));
                int b = w2 != null ? Convert.ToInt32(cols[w2].GetValue(r)) : 0;
                if (!d.TryGetValue(r.ID, out var l)) d[r.ID] = l = new List<TableMerge.Entry>();
                l.Add(new TableMerge.Entry(key, a, b, r));
            }
            return d;
        }

        var gb = Group(vp);
        var ge = Group(c.Ev.Params[pn]);
        var gm = Group(c.Mmv.Params[pn]);
        var rows = new List<Param.Row>();
        foreach (var id in gb.Keys.Union(ge.Keys).Union(gm.Keys).OrderBy(x => x))
        {
            var b = gb.GetValueOrDefault(id) ?? new List<TableMerge.Entry>();
            var e = ge.GetValueOrDefault(id) ?? new List<TableMerge.Entry>();
            var m = gm.GetValueOrDefault(id) ?? new List<TableMerge.Entry>();
            var merged = TableMerge.MergeEntries(b, e, m, out var conflicts);
            if (conflicts > 0)
            {
                Journal.Add("regulation-tables", $"{pn} {id}", $"{conflicts} base entr{(conflicts == 1 ? "y" : "ies")} replaced differently by both; kept both replacements with halved weights");
                c.Count("table-replacement-conflicts", conflicts);
            }
            bool evT = !e.Select(x => x.MatchKey).SequenceEqual(b.Select(x => x.MatchKey));
            bool mmT = !m.Select(x => x.MatchKey).SequenceEqual(b.Select(x => x.MatchKey));
            if (evT || mmT) c.Count(evT && mmT ? "tables-changed-both" : evT ? "tables-changed-EV" : "tables-changed-MMV");
            foreach (var en in merged)
            {
                var src = (Param.Row)en.Source;
                var nr = new Param.Row(src, outP);
                if (Convert.ToInt32(outCols[w1].GetValue(nr)) != en.Weight)
                    outCols[w1].SetValue(nr, Convert.ChangeType(en.Weight, outCols[w1].ValueType));
                if (w2 != null && Convert.ToInt32(outCols[w2].GetValue(nr)) != en.Weight2)
                    outCols[w2].SetValue(nr, Convert.ChangeType(en.Weight2, outCols[w2].ValueType));
                rows.Add(nr);
            }
        }
        outP.Rows = rows;
        return outP;
    }

    public static void Write(Context c, string path)
    {
        foreach (var (pn, p) in c.Merged)
        {
            var f = c.Cur.Files[pn];
            var mapped = p.ParamType;
            p.ParamType = c.Cur.OriginalParamTypes[pn];
            f.Bytes = p.Write();
            p.ParamType = mapped;
        }
        var bytes = SFUtil.EncryptNightreignRegulation(c.Cur.Bnd, new byte[16]);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes);
    }
}
