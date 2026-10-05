using SoulsFormats;

namespace NRMerge;

/// <summary>Stage (Task 6): binders, menu TPF, weapon parts and text.</summary>
public static class ContentMerge
{
    public static readonly string[] Binders =
    {
        "material/allmaterial.matbinbnd.dcx", "material/allmaterial_dlc01.matbinbnd.dcx",
        "sfx/sfxbnd_c0000.ffxbnd.dcx", "sfx/sfxbnd_commoneffects_dlc01.ffxbnd.dcx",
        "menu/01_common_h.tpf.dcx",
        "parts/wp_a_0272.partsbnd.dcx", "parts/wp_a_0940.partsbnd.dcx", "parts/wp_a_1570.partsbnd.dcx",
        "parts/wp_a_1572.partsbnd.dcx", "parts/wp_a_1671.partsbnd.dcx", "parts/wp_a_1672.partsbnd.dcx",
    };

    /// <summary>Entries where MMV's version wins a two-sided conflict (filled after inspecting the conflicts).</summary>
    public static readonly Dictionary<string, HashSet<string>> MmvOwned = new(StringComparer.Ordinal)
    {
        // only MMV weapons use these models (Star-Lined Sword, Spear of the Impaler)
        ["parts/wp_a_0272.partsbnd.dcx"] = new() { "wp_a_0272.flver", "wp_a_0272.tpf" },
        ["parts/wp_a_0940.partsbnd.dcx"] = new() { "wp_a_0940.flver" },
    };

    public static int Run()
    {
        Journal.Clear();
        foreach (var rel in Binders)
        {
            var owned = MmvOwned.GetValueOrDefault(rel) ?? new HashSet<string>();
            var r = BinderMerge.Merge(Paths.BaseOf(rel), Paths.In(Paths.EV, rel), MmvRewrite.MmvInput(rel), Paths.In(Paths.OutMod, rel),
                k => owned.Contains(k) ? Side.MMV : Side.EV, rel);
            Console.WriteLine($"{rel}: {r}");
        }
        MergeText();
        CloneSharedModels();
        Journal.Save();
        return 0;
    }

    /// <summary>Model IDs both mods use for *different* weapons: MMV's model is copied to a free ID and MMV's weapons re-pointed.</summary>
    static readonly int[] SharedModelIds = { 1572, 1671, 1672 };

    static void CloneSharedModels()
    {
        var regPath = Path.Combine(Paths.OutMod, "regulation.bin");
        var reg = Regulation.Load(regPath);
        var mmvReg = Regulation.Load(Path.Combine(Paths.MMV, "regulation.bin"));
        var w = reg.Params["EquipParamWeapon"];
        var col = w["equipModelId"];
        var mcol = mmvReg.Params["EquipParamWeapon"]["equipModelId"];
        var mmvUsers = mmvReg.Params["EquipParamWeapon"].Rows.GroupBy(r => Convert.ToInt32(mcol.GetValue(r))).ToDictionary(g => g.Key, g => g.Select(r => r.ID).ToHashSet());
        var remaps = RemapTable.Load().For("EquipParamWeapon");
        var used = w.Rows.Select(r => Convert.ToInt32(col.GetValue(r))).ToHashSet();
        if (!Paths.HasEldenRing)
            ErFallback.Note("parts/wp_a_*.partsbnd.dcx", "no Elden Ring base: free weapon model IDs chosen from EV, MMV, vanilla Nightreign and both Andre dictionaries (no vanilla_er/parts scan)");
        foreach (var dir in Paths.HasEldenRing ? new[] { Paths.EV, Paths.MMV, Paths.VanillaNR, Paths.VanillaER } : new[] { Paths.EV, Paths.MMV, Paths.VanillaNR })
            if (Directory.Exists(Path.Combine(dir, "parts")))
                foreach (var f in Directory.GetFiles(Path.Combine(dir, "parts"), "wp_a_*.partsbnd.dcx"))
                    if (int.TryParse(Path.GetFileName(f)[5..9], out var mid)) used.Add(mid);
        foreach (var dict in new[] { "EldenRingNightreignDictionary.txt", "EldenRingDictionary.txt" })
            foreach (var line in File.ReadLines(Path.Combine(Paths.AndreResources, dict)))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"/parts/wp_a_(\d{4})\.partsbnd");
                if (m.Success) used.Add(int.Parse(m.Groups[1].Value));
            }
        foreach (var oldId in SharedModelIds)
        {
            int newId = oldId + 1;
            while (used.Contains(newId)) newId++;
            used.Add(newId);
            var rel = $"parts/wp_a_{oldId:D4}.partsbnd.dcx";
            ModelClone.Clone(MmvRewrite.MmvInput(rel), oldId, newId, Paths.In(Paths.OutMod, $"parts/wp_a_{newId:D4}.partsbnd.dcx"));
            var lod = $"parts/wp_a_{oldId:D4}_l.partsbnd.dcx";
            if (File.Exists(MmvRewrite.MmvInput(lod)))
            {
                ModelClone.Clone(MmvRewrite.MmvInput(lod), oldId, newId, Paths.In(Paths.OutMod, $"parts/wp_a_{newId:D4}_l.partsbnd.dcx"));
                // EV's model at oldId has no low-detail file; don't let MMV's LOD stand in for it
                if (!File.Exists(Paths.In(Paths.EV, lod)) && File.Exists(Paths.In(Paths.OutMod, lod))) File.Delete(Paths.In(Paths.OutMod, lod));
            }
            int n = 0;
            if (mmvUsers.TryGetValue(oldId, out var ids))
                foreach (var row in w.Rows)
                {
                    var origId = remaps.FirstOrDefault(kv => kv.Value == row.ID).Key;
                    if (!ids.Contains(row.ID) && !(origId != 0 && ids.Contains((int)origId))) continue;
                    if (Convert.ToInt32(col.GetValue(row)) != oldId) continue;
                    col.SetValue(row, Convert.ChangeType(newId, col.ValueType));
                    n++;
                }
            Journal.Add("binders", rel, $"model used by different weapons in EV and MMV: EV keeps {oldId}; MMV's model copied to wp_a_{newId:D4} and {n} MMV weapons re-pointed");
            Console.WriteLine($"model {oldId}: MMV copy -> {newId}, {n} MMV weapons re-pointed");
        }
        Regulation.Save(reg, regPath);
    }

    static IReadOnlyDictionary<long, long> RemapFor(string fmgName, RemapTable t)
    {
        if (fmgName.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase)) return t.For("EquipParamWeapon");
        if (fmgName.StartsWith("Protector", StringComparison.OrdinalIgnoreCase)) return t.For("EquipParamProtector");
        return new Dictionary<long, long>();
    }

    static Dictionary<string, FMG> Fmgs(BND4 b) => b == null ? new() :
        b.Files.ToDictionary(f => BndDiff3.Norm(f.Name), f => FMG.Read(f.Bytes), StringComparer.Ordinal);

    static BND4 ReadBnd(string path, out DCX.Type type)
    {
        type = DCX.Type.None;
        return path != null && File.Exists(path) ? BND4.Read(Dcx.Decompress(File.ReadAllBytes(path), out type)) : null;
    }

    static void MergeText()
    {
        var table = RemapTable.Load();
        foreach (var name in new[] { "item_dlc01.msgbnd.dcx", "menu_dlc01.msgbnd.dcx" })
        {
            var rel = $"msg/engus/{name}";
            var evB = ReadBnd(Paths.In(Paths.EV, rel), out var type);
            var mmB = ReadBnd(MmvRewrite.MmvInput(rel), out _);
            var baB = ReadBnd(Paths.BaseOf(rel), out _);
            var ef = Fmgs(evB); var mf = Fmgs(mmB); var bf = Fmgs(baB);
            foreach (var (k, f) in mf) FmgMerge.ApplyRemap(f, RemapFor(k, table));
            int conflicts = 0, added = 0;
            foreach (var key in ef.Keys.Union(mf.Keys))
            {
                ef.TryGetValue(key, out var e); mf.TryGetValue(key, out var m); bf.TryGetValue(key, out var b);
                if (m == null) continue;
                FMG merged;
                if (e == null) { merged = m; added++; }
                else { merged = FmgMerge.Merge(b, e, m, Side.EV, $"{name}/{key}", out var c); conflicts += c; }
                var file = evB.Files.FirstOrDefault(f => BndDiff3.Norm(f.Name) == key);
                if (file != null) file.Bytes = merged.Write();
                else
                {
                    var src = mmB.Files.First(f => BndDiff3.Norm(f.Name) == key);
                    evB.Files.Add(new BinderFile(src.Flags, src.ID, src.Name, merged.Write()));
                }
            }
            File.WriteAllBytes(Paths.In(Paths.OutMod, rel), evB.Write(type).ToArray());
            Console.WriteLine($"{rel}: text conflicts={conflicts} fmg files added from MMV={added}");

            // other languages: EV's text plus MMV's (English) entries where the language lacks them
            var mmvEn = Fmgs(mmB);
            foreach (var (k, f) in mmvEn) FmgMerge.ApplyRemap(f, RemapFor(k, table));
            foreach (var langDir in Directory.GetDirectories(Path.Combine(Paths.EV, "msg")))
            {
                var lang = Path.GetFileName(langDir);
                if (lang == "engus") continue;
                var lrel = $"msg/{lang}/{name}";
                var lb = ReadBnd(Paths.In(Paths.EV, lrel), out var ltype);
                if (lb == null) continue;
                int fill = 0;
                foreach (var file in lb.Files)
                {
                    var key = BndDiff3.Norm(file.Name);
                    if (!mmvEn.TryGetValue(key, out var en)) continue;
                    var fmg = FMG.Read(file.Bytes);
                    var have = fmg.Entries.Select(e => e.ID).ToHashSet();
                    foreach (var e in en.Entries.Where(e => !have.Contains(e.ID) && !string.IsNullOrEmpty(e.Text)))
                    { fmg.Entries.Add(new FMG.Entry(fmg, e.ID, e.Text)); fill++; }
                    fmg.Entries.Sort((a, b) => a.ID.CompareTo(b.ID));
                    file.Bytes = fmg.Write();
                }
                File.WriteAllBytes(Paths.In(Paths.OutMod, lrel), lb.Write(ltype).ToArray());
                Journal.Add("text", lrel, $"{fill} MMV entries added in English (language fallback)");
            }
        }
    }
}
