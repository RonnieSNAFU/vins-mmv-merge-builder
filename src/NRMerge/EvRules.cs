using Andre.Formats;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>
/// Spec §6.6 and §14: EV's blanket rules, derived from how EV edited vanilla (and ER-ported) rows, applied to rows only
/// MMV added — enemy poise/HP/AI rules, enemy attack rules, weapon-wide rules and per-weapon-type moveset rules.
/// </summary>
public static class EvRules
{
    public sealed record Obs(string Group, object Base, object New);

    public sealed record Rule
    {
        public string Param { get; init; }
        public string Field { get; init; }
        public string GroupField { get; init; }
        public string Kind { get; init; }   // mul | const | map
        public double K { get; init; }
        public object Const { get; init; }
        public Dictionary<(string, string), object> Map { get; init; }
        public string Scope { get; init; } = "all"; // all | erport
        public double Fit { get; init; }
        public int Support { get; init; }

        public string Describe() => Kind switch
        {
            "mul" => $"{Param}.{Field} x{K.ToString(CultureInfo.InvariantCulture)} ({Scope}; fit {Fit:P0} of {Support})",
            "const" => $"{Param}.{Field} = {Regulation.Fmt(Const)} ({Scope}; fit {Fit:P0} of {Support})",
            "gconst" => $"{Param}.{Field} set per {GroupField} ({Map.Count} groups, {Support} rows)",
            _ => $"{Param}.{Field} mapped per {(GroupField ?? "value")} ({Map.Count} keys, {Support} rows)",
        };
    }

    static bool IsNum(object o) => o is sbyte or byte or short or ushort or int or uint or long or ulong or float or double;
    static bool IsFloat(object o) => o is float or double;
    static double D(object o) => Convert.ToDouble(o, CultureInfo.InvariantCulture);

    static bool Close(object baseV, double expected, object actual)
    {
        double a = D(actual);
        if (IsFloat(baseV)) return Math.Abs(a - expected) <= Math.Max(1e-3, Math.Abs(expected) * 1e-4);
        return Math.Abs(a - expected) <= Math.Max(1.0, Math.Abs(expected) * 0.005);
    }

    public static Rule DeriveMultiplier(IEnumerable<Obs> obs, double minFit = 0.9, int minSupport = 3)
    {
        var nz = obs.Where(o => IsNum(o.Base) && IsNum(o.New) && D(o.Base) != 0).ToList();
        var changed = nz.Where(o => D(o.New) != D(o.Base)).ToList();
        if (changed.Count < minSupport) return null;
        Rule best = null;
        foreach (var g in changed.GroupBy(o => Math.Round(D(o.New) / D(o.Base), 3)).OrderByDescending(g => g.Count()).Take(5))
        {
            var k = g.Key;
            if (k == 1 || k == 0) continue;
            double f = (double)nz.Count(o => Close(o.Base, D(o.Base) * k, o.New)) / nz.Count;
            if (f >= minFit && (best == null || f > best.Fit)) best = new Rule { Kind = "mul", K = k, Fit = f, Support = nz.Count };
        }
        return best;
    }

    /// <summary>Per-group constant: within a group (e.g. weapon type) EV set the field to one value regardless of its old value.</summary>
    public static Rule DeriveGroupConstant(IEnumerable<Obs> obs, double minShare = 0.8, int minCount = 3)
    {
        var map = new Dictionary<(string, string), object>();
        int support = 0;
        foreach (var g in obs.GroupBy(o => o.Group ?? ""))
        {
            var top = g.GroupBy(o => Regulation.Fmt(o.New)).OrderByDescending(x => x.Count()).First();
            var eligible = g.Where(o => Regulation.Fmt(o.Base) != top.Key).ToList();
            if (eligible.Count < minCount) continue;
            int fit = eligible.Count(o => Regulation.Fmt(o.New) == top.Key);
            if ((double)fit / eligible.Count < minShare) continue;
            if ((double)top.Count() / g.Count() < minShare) continue;
            // every sizeable value subgroup must actually have moved to the constant (e.g. unique weapons that EV
            // left on their fixed skill invalidate a "whole type uses EV's skill table" rule)
            bool valid = g.GroupBy(o => Regulation.Fmt(o.Base)).Where(sg => sg.Key != top.Key && sg.Count() >= minCount)
                .All(sg => (double)sg.Count(o => Regulation.Fmt(o.New) == top.Key) / sg.Count() >= minShare);
            if (!valid) continue;
            map[(g.Key, "*")] = top.First().New;
            support += g.Count();
        }
        return map.Count == 0 ? null : new Rule { Kind = "gconst", Map = map, Support = support, Fit = 1 };
    }

    public static Rule DeriveConstant(IEnumerable<Obs> obs, double minFit = 0.9, int minSupport = 3)
    {
        var list = obs.ToList();
        var changed = list.Where(o => Regulation.Fmt(o.New) != Regulation.Fmt(o.Base)).ToList();
        if (changed.Count < minSupport) return null;
        var c = changed.GroupBy(o => Regulation.Fmt(o.New)).OrderByDescending(g => g.Count()).First();
        var eligible = list.Where(o => Regulation.Fmt(o.Base) != c.Key).ToList();
        int fit = eligible.Count(o => Regulation.Fmt(o.New) == c.Key);
        double f = eligible.Count == 0 ? 0 : (double)fit / eligible.Count;
        return f >= minFit ? new Rule { Kind = "const", Const = c.First().New, Fit = f, Support = eligible.Count } : null;
    }

    public static Rule DeriveMap(IEnumerable<Obs> obs, double minShare = 0.8, int minCount = 3)
    {
        var map = new Dictionary<(string, string), object>();
        int support = 0;
        foreach (var g in obs.GroupBy(o => (o.Group ?? "", Regulation.Fmt(o.Base))))
        {
            int n = g.Count();
            if (n < minCount) continue;
            var top = g.GroupBy(o => Regulation.Fmt(o.New)).OrderByDescending(x => x.Count()).First();
            if (top.Key == g.Key.Item2) continue;
            if ((double)top.Count() / n < minShare) continue;
            map[g.Key] = top.First().New;
            support += n;
        }
        return map.Count == 0 ? null : new Rule { Kind = "map", Map = map, Support = support, Fit = 1 };
    }

    /// <summary>Per-group rule: exact (group, value) mappings plus validated group constants for values EV never saw.</summary>
    public static Rule DeriveTypeRule(IEnumerable<Obs> obs)
    {
        var list = obs.ToList();
        var g = DeriveGroupConstant(list);
        var m = DeriveMap(list);
        if (g == null && m == null) return null;
        var map = new Dictionary<(string, string), object>();
        if (m != null) foreach (var (k, v) in m.Map) map[k] = v;
        if (g != null) foreach (var (k, v) in g.Map) map[k] = v;
        return new Rule { Kind = "map", Map = map, Support = (g?.Support ?? 0) + (m?.Support ?? 0), Fit = 1 };
    }

    public static bool IsCategorical(string field, IEnumerable<Obs> obs) =>
        RegMerge.IsRefField(field) || obs.Select(o => Regulation.Fmt(o.Base)).Distinct().Count() <= 64;

    /// <summary>Weapon types MMV adds (Shadow of the Erdtree) and the vanilla type whose weapon-wide EV rules they borrow.</summary>
    public static string AnalogType(string wepType) => wepType switch
    {
        "90" => "35", // hand-to-hand arts -> fists
        "92" => "15", // thrusting shields -> thrusting swords
        "94" => "9",  // backhand blades -> curved swords
        "95" => "5",  // light greatswords -> greatswords
        "96" => "13", // great katanas -> katanas
        _ => null,
    };

    /// <summary>Weapon fields that are not part of a moveset; unique MMV types borrow these from their analog type.</summary>
    static readonly HashSet<string> WeaponWideFields = new(StringComparer.Ordinal)
    {
        "staminaConsumptionRate", "guardAngle", "staminaGuardDef", "residentSpEffectId2",
        "magGuardCutRate", "fireGuardCutRate", "thunGuardCutRate", "darkGuardCutRate", "physGuardCutRate", "saWeaponDamage",
    };

    static object ConvertLike(object like, object v)
    {
        if (!IsFloat(like) && IsFloat(v)) v = Math.Round(D(v), MidpointRounding.AwayFromZero);
        return Convert.ChangeType(v, like.GetType(), CultureInfo.InvariantCulture);
    }

    public static object Transform(Rule r, string group, object value)
    {
        switch (r.Kind)
        {
            case "mul":
                if (!IsNum(value) || D(value) == 0) return value;
                var x = D(value) * r.K;
                return ConvertLike(value, IsFloat(value) ? x : Math.Round(x, MidpointRounding.AwayFromZero));
            case "const":
                return Regulation.Fmt(value) == Regulation.Fmt(r.Const) ? value : ConvertLike(value, r.Const);
            case "map":
                if (r.Map.TryGetValue((group ?? "", Regulation.Fmt(value)), out var nv)) return ConvertLike(value, nv);
                return r.Map.TryGetValue((group ?? "", "*"), out var sv) ? ConvertLike(value, sv) : value;
            case "gconst":
                return r.Map.TryGetValue((group ?? "", "*"), out var gv) ? ConvertLike(value, gv) : value;
        }
        return value;
    }

    // ---------------------------------------------------------------- derivation over the real regulations

    static Dictionary<string, Param.Column> Cols(Param p)
    {
        var d = new Dictionary<string, Param.Column>(StringComparer.Ordinal);
        foreach (var c in p.Columns) d.TryAdd(c.Def.InternalName, c);
        return d;
    }

    /// <summary>(group, vanilla value, EV value) for every vanilla row EV kept.</summary>
    static List<Obs> VanillaObs(RegMerge.Context c, string pn, string field, string groupField)
    {
        var bp = c.EvBase.Params[pn];
        var ep = c.Ev.Params[pn];
        var bc = Cols(bp); var ec = Cols(ep);
        if (!bc.ContainsKey(field)) return new();
        var ei = ParamDiff.Index(ep);
        var list = new List<Obs>();
        foreach (var (k, br) in ParamDiff.Index(bp))
        {
            if (!ei.TryGetValue(k, out var er)) continue;
            var g = groupField != null ? Regulation.Fmt(bc[groupField].GetValue(br)) : "";
            list.Add(new Obs(g, bc[field].GetValue(br), ec[field].GetValue(er)));
        }
        return list;
    }

    /// <summary>Fields EV changed on at least <paramref name="min"/> vanilla rows of a param.</summary>
    static List<string> ChangedFields(RegMerge.Context c, string pn, int min)
    {
        if (!c.DEv.Params.TryGetValue(pn, out var rows)) return new();
        return rows.Values.Where(r => !r.Added && !r.Removed).SelectMany(r => r.Fields.Keys)
            .GroupBy(f => f).Where(g => g.Count() >= min).Select(g => g.Key).ToList();
    }

    static readonly HashSet<string> NeverRule = new(StringComparer.Ordinal) { "Name" };

    public static List<Rule> Derive(RegMerge.Context c)
    {
        var rules = new List<Rule>();
        void Add(Rule r, string pn, string f, string gf = null, string scope = "all")
        {
            if (r != null) rules.Add(r with { Param = pn, Field = f, GroupField = gf, Scope = scope });
        }

        // Enemies
        Add(DeriveMultiplier(VanillaObs(c, "NpcParam", "superArmorDurability", null)), "NpcParam", "superArmorDurability");
        Add(DeriveConstant(VanillaObs(c, "NpcParam", "analyseDistCorrection", null)), "NpcParam", "analyseDistCorrection");
        Add(DeriveMultiplier(ErPortObs(c, "NpcParam", "hp")), "NpcParam", "hp", null, "erport");

        // Generic blanket rules for enemy attacks, player attacks, weapons and custom weapons
        foreach (var (pn, group) in new[] { ("AtkParam_Npc", (string)null), ("AtkParam_Pc", null), ("EquipParamWeapon", "wepType"), ("EquipParamCustomWeapon", null) })
            foreach (var f in ChangedFields(c, pn, 50))
            {
                if (NeverRule.Contains(f)) continue;
                var obs = VanillaObs(c, pn, f, group);
                bool categorical = IsCategorical(f, obs);
                var r = (categorical ? null : DeriveMultiplier(obs)) ?? DeriveConstant(obs)
                        ?? (group != null ? (categorical ? DeriveTypeRule(obs) : DeriveGroupConstant(obs))
                                          : categorical ? DeriveMap(obs) : null);
                Add(r, pn, f, r?.Kind is "map" or "gconst" ? group : null);
            }
        return rules;
    }

    /// <summary>(ER value, EV value) for rows EV ported from Elden Ring (exist in ER, not in vanilla NR).</summary>
    static List<Obs> ErPortObs(RegMerge.Context c, string pn, string field)
    {
        var ep = c.Ev.Params[pn];
        var bp = c.EvBase.Params[pn];
        if (!Paths.HasEldenRing)
            ErFallback.Note($"ev-rule {pn}.{field} (erport)", "no Elden Ring regulation: EV's scaling of Elden Ring ports not derived (no ER values to compare), so it is not applied to MMV's Elden Ring ports");
        if (!c.Er.Params.TryGetValue(pn, out var rp)) return new();
        var ec = Cols(ep); var rc = Cols(rp);
        if (!rc.ContainsKey(field)) return new();
        var vi = ParamDiff.Index(bp);
        var ri = ParamDiff.Index(rp);
        var list = new List<Obs>();
        foreach (var (k, er) in ParamDiff.Index(ep))
            if (!vi.ContainsKey(k) && ri.TryGetValue(k, out var rr))
                list.Add(new Obs("", rc[field].GetValue(rr), ec[field].GetValue(er)));
        return list;
    }

    // ---------------------------------------------------------------- application

    static readonly Regex ChrFile = new(@"^/chr/c(\d{4})\.chrbnd\.dcx$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static HashSet<int> ChrSet(string dictionary)
    {
        var s = new HashSet<int>();
        foreach (var line in File.ReadLines(dictionary))
        {
            var m = ChrFile.Match(line.Trim());
            if (m.Success) s.Add(int.Parse(m.Groups[1].Value));
        }
        return s;
    }

    /// <summary>Chrs that exist in Elden Ring but not in vanilla Nightreign (i.e. Elden Ring ports when a mod uses them).</summary>
    public static HashSet<int> ErPortChrs()
    {
        var res = Paths.AndreResources;
        var nr = ChrSet(Path.Combine(res, "EldenRingNightreignDictionary.txt"));
        var er = ChrSet(Path.Combine(res, "EldenRingDictionary.txt"));
        er.ExceptWith(nr);
        return er;
    }

    /// <summary>The candidate whose field values match <paramref name="row"/> most often (fields in <paramref name="ignore"/> excluded).</summary>
    public static int PickSource(IReadOnlyDictionary<string, object> row, IEnumerable<(int Id, Dictionary<string, object> Vals)> candidates, HashSet<string> ignore)
    {
        int best = -1, bestScore = -1;
        foreach (var (id, vals) in candidates)
        {
            int score = 0;
            foreach (var (f, v) in row)
                if (!ignore.Contains(f) && vals.TryGetValue(f, out var cv) && RegMerge.Same(v, cv)) score++;
            if (score > bestScore) { bestScore = score; best = id; }
        }
        return best;
    }

    static readonly HashSet<string> ScaledNpcFields = new(StringComparer.Ordinal) { "hp", "superArmorDurability", "analyseDistCorrection" };

    /// <summary>
    /// Spec §6.6: MMV-only clones of NR-native enemies inherit the HP ratio EV hand-applied to the vanilla row they
    /// were copied from (matched by chr and field similarity).
    /// </summary>
    static void ApplyCloneHpBuffs(RegMerge.Context c, HashSet<int> erChrs)
    {
        const string pn = "NpcParam";
        if (!c.Merged.TryGetValue(pn, out var p) || !c.MmvOnlyAdded.TryGetValue(pn, out var keys)) return;
        var cols = Cols(p);
        var hp = cols["hp"];
        var ratios = new Dictionary<int, double>();
        if (c.DEv.Params.TryGetValue(pn, out var dev))
        {
            var bi = ParamDiff.Index(c.EvBase.Params[pn]);
            var bc = Cols(c.EvBase.Params[pn]);
            foreach (var (k, d) in dev)
                if (!d.Added && !d.Removed && d.Fields.TryGetValue("hp", out var nhp) && bi.TryGetValue(k, out var br))
                {
                    var old = Convert.ToDouble(bc["hp"].GetValue(br), CultureInfo.InvariantCulture);
                    if (old > 0) ratios[k.ID] = Convert.ToDouble(nhp, CultureInfo.InvariantCulture) / old;
                }
        }
        var buffedChrs = ratios.Keys.Select(id => id / 10000).ToHashSet();
        var vp = c.Cur.Params[pn];
        var vcols = Cols(vp);
        var byChr = vp.Rows.GroupBy(r => r.ID / 10000).Where(g => buffedChrs.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(r => (r.ID, vcols.ToDictionary(kv => kv.Key, kv => kv.Value.GetValue(r)))).ToList());
        c.Er.Params.TryGetValue(pn, out var erP);
        var erIds = erP != null ? erP.Rows.Select(r => r.ID).ToHashSet() : new HashSet<int>();
        int n = 0;
        foreach (var row in p.Rows)
        {
            if (!keys.Contains(new RowKey(row.ID, 0))) continue;
            int chr = row.ID / 10000;
            if (erIds.Contains(row.ID) || erChrs.Contains(chr) || !byChr.TryGetValue(chr, out var cands)) continue;
            var vals = cols.ToDictionary(kv => kv.Key, kv => kv.Value.GetValue(row));
            var src = PickSource(vals, cands, ScaledNpcFields);
            if (!ratios.TryGetValue(src, out var r)) continue;
            var v = hp.GetValue(row);
            var nv = Convert.ChangeType(Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture) * r, MidpointRounding.AwayFromZero), hp.ValueType, CultureInfo.InvariantCulture);
            hp.SetValue(row, nv);
            Journal.Add("ev-rules", $"NpcParam {row.ID} hp", $"clone of vanilla {src} which EV scaled x{r:0.###}: {Regulation.Fmt(v)} -> {Regulation.Fmt(nv)} ({row.Name})");
            n++;
        }
        c.Count("ev-rule NpcParam.hp (clones of EV-buffed bosses)", n);
    }

    public static void Apply(RegMerge.Context c, List<Rule> rules)
    {
        var erChrs = ErPortChrs();
        foreach (var r in rules) Journal.Add("ev-rules", r.Describe(), "derived");
        ApplyCloneHpBuffs(c, erChrs);
        foreach (var grp in rules.GroupBy(r => r.Param))
        {
            var pn = grp.Key;
            if (!c.Merged.TryGetValue(pn, out var p) || !c.MmvOnlyAdded.TryGetValue(pn, out var keys) || keys.Count == 0) continue;
            var cols = Cols(p);
            c.Er.Params.TryGetValue(pn, out var erP);
            var erIds = erP != null ? erP.Rows.Select(x => x.ID).ToHashSet() : new HashSet<int>();
            var occ = new Dictionary<int, int>();
            var counts = new Dictionary<string, int>();
            foreach (var row in p.Rows)
            {
                occ.TryGetValue(row.ID, out var n);
                occ[row.ID] = n + 1;
                if (!keys.Contains(new RowKey(row.ID, n))) continue;
                bool erPort = erIds.Contains(row.ID) || erChrs.Contains(row.ID / 10000);
                foreach (var r in grp)
                {
                    if (r.Scope == "erport" && !erPort) continue;
                    if (!cols.TryGetValue(r.Field, out var col)) continue;
                    var g = r.GroupField != null && cols.TryGetValue(r.GroupField, out var gc) ? Regulation.Fmt(gc.GetValue(row)) : "";
                    var v = col.GetValue(row);
                    var nv = Transform(r, g, v);
                    if (RegMerge.Same(v, nv) && pn == "EquipParamWeapon" && WeaponWideFields.Contains(r.Field) && AnalogType(g) is string ag)
                        nv = Transform(r, ag, v);
                    if (RegMerge.Same(v, nv)) continue;
                    col.SetValue(row, nv);
                    counts[r.Field] = counts.GetValueOrDefault(r.Field) + 1;
                }
            }
            foreach (var (f, n) in counts)
            {
                Journal.Add("ev-rules", $"{pn}.{f}", $"applied to {n} MMV-only rows");
                c.Count($"ev-rule {pn}.{f}", n);
            }
        }
    }
}
