using System.Text;
using Andre.Formats;
using SoulsFormats;

namespace NRMerge;

/// <summary>Stage (Task 15): re-read every merged file, check DCX types against the source, and param references.</summary>
public static class Verify
{
    public sealed record Dangling(string Param, int Row, string Field, long Value);

    /// <summary>References (Smithbox Param Meta) whose target param has no row with that ID.</summary>
    public static HashSet<Dangling> DanglingRefs(Regulation.Loaded reg)
    {
        var res = new HashSet<Dangling>();
        var ids = reg.Params.ToDictionary(p => p.Key, p => p.Value.Rows.Select(r => r.ID).ToHashSet());
        foreach (var (name, p) in reg.Params)
        {
            // one field may point at several params (e.g. refId -> AtkParam_Pc or AtkParam_Npc for refType 0): dangling only if none has the ID
            foreach (var group in ParamRefs.For(p.ParamType).GroupBy(sp => sp.Field))
            {
                var col = p.Columns.FirstOrDefault(c => c.Def.InternalName == group.Key);
                if (col == null) continue;
                var specs = group.Where(sp => ids.ContainsKey(sp.Target)).Select(sp => (sp, cond: sp.CondField == null ? null : p.Columns.FirstOrDefault(c => c.Def.InternalName == sp.CondField))).ToList();
                if (specs.Count == 0) continue;
                foreach (var r in p.Rows)
                {
                    long v;
                    try { v = Convert.ToInt64(col.GetValue(r)); } catch { continue; }
                    if (v <= 0) continue;
                    var applicable = specs.Where(x => x.cond == null || Convert.ToString(x.cond.GetValue(r)) == x.sp.CondValue).ToList();
                    if (applicable.Count == 0 || applicable.Any(x => ids[x.sp.Target].Contains((int)v))) continue;
                    res.Add(new Dangling(name, r.ID, group.Key, v));
                }
            }
        }
        return res;
    }

    /// <summary>Dangling references in the merged regulation that neither source mod has (same param, row, field and value).</summary>
    public static List<Dangling> IntroducedDangling(HashSet<Dangling> merged, HashSet<Dangling> ev, HashSet<Dangling> mmv) =>
        merged.Where(d => !ev.Contains(d) && !mmv.Contains(d)
                       && !ev.Concat(mmv).Any(x => x.Param == d.Param && x.Field == d.Field && x.Value == d.Value))   // same dangling value elsewhere in a source (e.g. a copied row)
              .OrderBy(d => d.Param).ThenBy(d => d.Row).ToList();

    /// <summary>Files removed on purpose: backups/logs, and MMV's LOD parts at model IDs whose MMV models were cloned to new IDs (Task 6 ruling).</summary>
    static bool ExpectedAbsent(string rel) =>
        rel.EndsWith(".bak") || rel.EndsWith(".log") || rel is "parts/wp_a_1572_l.partsbnd.dcx" or "parts/wp_a_1671_l.partsbnd.dcx" or "parts/wp_a_1672_l.partsbnd.dcx";

    static int fails;
    static void Fail(string what) { fails++; Console.WriteLine("FAIL " + what); Journal.Add("verify", "FAIL", what); }

    static void ReadDeep(string rel, byte[] data)
    {
        if (rel.EndsWith(".msb.dcx")) { MSB_NR.Read(data); return; }
        if (rel.EndsWith(".emevd.dcx")) { EMEVD.Read(data); return; }
        if (TPF.Is(data)) { TPF.Read(data); return; }
        if (!BND4.Is(data)) return;
        foreach (var f in BND4.Read(data).Files)
        {
            var n = f.Name.ToLowerInvariant();
            var b = f.Bytes.ToArray();
            if (n.EndsWith(".tae")) TAE.Read(b);
            else if (n.EndsWith(".fmg")) FMG.Read(b);
            else if (n.EndsWith(".luagnl")) LUAGNL.Read(b);
            else if (n.EndsWith(".flver")) FLVER2.Read(b);
            else if (n.EndsWith(".hkx") && n.Contains(@"\behaviors\")) BehGraph.Read(b);
        }
    }

    public static int Run()
    {
        Journal.Clear();
        fails = 0;
        // Housekeeping: backups and logs copied from the source mods do not belong in the mod.
        foreach (var f in Directory.GetFiles(Paths.OutMod, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".bak") || f.EndsWith(".log")))
        {
            File.Delete(f);
            Journal.Add("verify", Path.GetRelativePath(Paths.OutMod, f), "removed (backup/log file from a source mod)");
        }

        var classes = File.ReadAllLines(Path.Combine(Paths.Out, "assembly.tsv")).Skip(1).Select(l => l.Split('\t')).ToDictionary(p => p[1], p => p[0]);
        int read = 0;
        foreach (var (rel, cls) in classes)
        {
            var path = Paths.In(Paths.OutMod, rel);
            if (!File.Exists(path))
            {
                if (ExpectedAbsent(rel)) { Journal.Add("verify", rel, "absent on purpose"); continue; }
                Fail($"{rel}: missing ({cls})"); continue;
            }
            if (cls != "merge" && !rel.EndsWith(".msb.dcx") && !rel.EndsWith(".emevd.dcx")) continue;
            try
            {
                var raw = File.ReadAllBytes(path);
                var data = Dcx.Decompress(raw, out var type);
                var src = File.Exists(Paths.In(Paths.EV, rel)) ? Paths.In(Paths.EV, rel) : Paths.In(Paths.MMV, rel);
                if (rel.EndsWith(".dcx") && File.Exists(src))
                {
                    Dcx.Decompress(File.ReadAllBytes(src), out var srcType);
                    if (srcType != type) Fail($"{rel}: DCX {type} but source has {srcType}");
                }
                if (rel == "regulation.bin")
                {
                    var reg = Regulation.Load(path);
                    if (reg.Errors.Count > 0) Fail($"regulation.bin: {reg.Errors.Count} param read errors: {reg.Errors[0]}");
                    if (reg.Version != 10350000) Fail($"regulation.bin: version {reg.Version}");
                }
                else ReadDeep(rel, data);
                read++;
            }
            catch (Exception ex) { Fail($"{rel}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Journal.Add("verify", "re-read", $"{read} merged/map files re-read with SoulsFormats/HKLib; {classes.Count} assembled paths present");
        Console.WriteLine($"re-read {read} files");

        // Lua AI scripts written by the merge compile; c0000.hks parses.
        foreach (var rel in classes.Where(c => c.Value == "merge" && c.Key.EndsWith(".luabnd.dcx")).Select(c => c.Key))
        {
            var bnd = BND4.Read(Dcx.Decompress(File.ReadAllBytes(Paths.In(Paths.OutMod, rel))));
            foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".lua")))
            {
                try { LuaNorm.Compile(f.Bytes.ToArray()); }
                catch (Exception ex) { Fail($"{rel}: Lua compile: {BndDiff3.Norm(f.Name)}: {ex.Message[..Math.Min(300, ex.Message.Length)]}"); }
            }
        }
        // Lua 5.1 syntax check of the merged player script (parse only; luaparser-equivalent, BOM tolerated).
        var hksErr = LuaSyntax.Check(File.ReadAllText(Paths.In(Paths.OutMod, "action/script/c0000.hks"), Encoding.UTF8));
        if (hksErr != null) Fail("c0000.hks: parse: " + hksErr);

        // Param references.
        var merged = DanglingRefs(Regulation.Load(Paths.In(Paths.OutMod, "regulation.bin")));
        var ev = DanglingRefs(Regulation.Load(Path.Combine(Paths.EV, "regulation.bin")));
        var mmv = DanglingRefs(Regulation.Load(Path.Combine(Paths.MMV, "regulation.bin")));
        var introduced = IntroducedDangling(merged, ev, mmv);
        Journal.Add("verify", "param references", $"dangling in merged {merged.Count} (EV alone {ev.Count}, MMV alone {mmv.Count}); introduced by the merge {introduced.Count}");
        foreach (var d in introduced) Journal.Add("verify-dangling", $"{d.Param} {d.Row} {d.Field}", d.Value.ToString());
        Console.WriteLine($"param refs: dangling merged {merged.Count}, EV {ev.Count}, MMV {mmv.Count}, introduced {introduced.Count}");
        foreach (var d in introduced.Take(25)) Console.WriteLine($"  {d.Param} {d.Row} {d.Field} -> {d.Value}");

        Journal.Save();
        Console.WriteLine(fails == 0 ? "VERIFY OK" : $"VERIFY: {fails} failure(s)");
        var ok = Path.Combine(Paths.Out, "verify.ok");
        if (fails == 0 && introduced.Count == 0) File.WriteAllText(ok, DateTime.Now.ToString("O")); else if (File.Exists(ok)) File.Delete(ok);
        return fails == 0 && introduced.Count == 0 ? 0 : 1;
    }
}
