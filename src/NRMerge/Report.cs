using System.Text;
using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>Stage (Task 15): MERGE_REPORT.md from the decision journals and the ledger's rulings.</summary>
public static class Report
{
    static readonly (string area, string title)[] Areas =
    {
        ("assembly", "File assembly"), ("regulation", "Regulation: fields both mods changed"), ("regulation-collisions", "Regulation: ID collisions (MMV rows moved)"),
        ("regulation-tables", "Regulation: lottery tables"), ("ev-rules", "EV rules applied to MMV content"), ("refs", "MMV references re-pointed to moved rows"), ("regulation-pending", "Regulation: deferred collisions (resolved in the player stage)"),
        ("binders", "Binders, effects, menu textures, parts"), ("binders-enemies", "Enemy binders"), ("binders-player", "Player binders"), ("text", "Text (FMG)"), ("models", "Weapon models"), ("events", "Event scripts (EMEVD)"),
        ("maps", "Maps (MSB)"), ("enemies", "Enemies (TAE, behavior, models, sounds)"), ("ai", "Enemy AI scripts"), ("player", "Player (scripts, animations, behavior, behavior judges)"),
        ("profile", "Profile, DLLs, configs"), ("verify", "Verification"), (ErFallback.Area, "Elden Ring absent: fallbacks (replayed from the verified build, see noer-patches)"),
    };

    public static string Build(string ledgerPath) => Build(File.ReadAllLines(ledgerPath));

    public static string Build(IEnumerable<string> ledgerLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Elden Vins × More Map Variations — merge report");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Inputs: Elden Vins Nightreign (EV, built on NR 1.03.4) and More Map Variations 2.1.8-hotfix3 & Weapons (MMV, NR 1.03.5); target Nightreign 1.03.5 (regulation 10350000).");
        sb.AppendLine();
        sb.AppendLine("## How to play");
        sb.AppendLine();
        sb.AppendLine("- Launch `Elden Vins with more map variations.me3` with Mod Engine 3 (double-click, or `me3 launch --profile \"Elden Vins with more map variations.me3\"`).");
        sb.AppendLine("- Online: custom server `https://nightreign.fs-emu.net`, shard **VINSMMV** (matchmaking only with players on this same merge).");
        sb.AppendLine("- Save: Elden Vins' save through the server redirector (`NR0000.CL_SAVE`, alternative save on; no ME3 savefile override). A copy was made in `%APPDATA%\\Nightreign\\<Steam ID>\\backup-before-merge\\` when the merge was built.");
        sb.AppendLine("- Natives: MMV's server redirector (one redirector), MMV's `custom_drop_fxrs.dll`, EV's `NightreignFPSFOV.dll` and `nighter.dll` entry. **`nighter.dll` is not part of EV's download** (EV's profile lists it but the file is absent); copy it into `mod\\dll\\` to get EV's forced Deep of Night (its config `nighter.json` is included).");
        sb.AppendLine();
        sb.AppendLine("## Precedence");
        sb.AppendLine();
        sb.AppendLine("Merges are done at the finest unit each format allows (param field, binder entry, event instruction, map part property, TAE event, behavior node, script function/line). Only when both mods changed the same unit differently: combat and balance → EV; world/content placement, drops and map variations → MMV; lottery tables keep both replacements; unrelated content under one ID → EV keeps the ID and MMV's row moves (all references re-pointed). EV's scaling rules are applied to MMV's new enemies and weapons; MMV's weapons that use a standard moveset adopt EV's moveset for their weapon type.");
        sb.AppendLine();
        sb.AppendLine("## Decisions by area");
        sb.AppendLine();
        sb.AppendLine("Every individual decision is listed in `merge-journal\\<area>.tsv` next to this report. Summary (most frequent decision kinds):");
        sb.AppendLine();
        foreach (var (area, title) in Areas)
        {
            var path = Path.Combine(Journal.Dir, area + ".tsv");
            if (!File.Exists(path)) continue;
            var rows = File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();
            sb.AppendLine($"### {title} — {rows.Count} entr{(rows.Count == 1 ? "y" : "ies")} (`{area}.tsv`)");
            sb.AppendLine();
            var kinds = rows.GroupBy(r => Kind(r.Length > 1 ? r[1] : "")).OrderByDescending(g => g.Count()).Take(12);
            foreach (var g in kinds) sb.AppendLine($"- {g.Count()} × {g.Key}");
            sb.AppendLine();
        }
        sb.AppendLine("## Judgement calls (rulings)");
        sb.AppendLine();
        foreach (var l in ledgerLines.Where(l => l.Contains("Ruling:")))
            sb.AppendLine("- " + Regex.Replace(l, @"^(Task \d+|Setup|Final): Ruling: ", "**$1:** "));
        sb.AppendLine();
        sb.AppendLine("## Known limitations");
        sb.AppendLine();
        sb.AppendLine("- Non-English text for MMV's additions is shown in English.");
        sb.AppendLine("- Where both mods changed the same effect file (FXR) or the same behavior/TAE value, EV's version is used, so some MMV visual effects or timings look like EV's.");
        sb.AppendLine("- AI scripts that both mods changed are shipped as normalised decompiled Lua (comments are lost; behaviour preserved).");
        sb.AppendLine("- EV's wepType 94 items show MMV's backhand-blade drop effect (custom_drop_fxrs.yaml is keyed by weapon type).");
        sb.AppendLine("- Behavior, animation and AI merges are verified structurally (re-read, references, compile); how every MMV weapon and boss variant feels under EV's balance can only be judged in play.");
        return sb.ToString();
    }

    /// <summary>Decision text with numbers and quoted values generalised, for grouping.</summary>
    static string Kind(string decision)
    {
        var s = Regex.Replace(decision, @"""[^""]*""", "\"…\"");
        s = Regex.Replace(s, @"-?\d+(\.\d+)?", "N");
        s = Regex.Replace(s, @"\([^)]{40,}\)", "(…)");
        return s.Length > 160 ? s[..160] + "…" : s;
    }

    /// <summary>The merge ledger whose Ruling lines go into MERGE_REPORT.md (dev checkout; <c>export-rulings</c> copies them into
    /// the embedded Resources\rulings.txt that the builder uses).</summary>
    public static string LedgerPath => Path.Combine(BuildConfig.DevRepo, "src", "NRMerge", "Resources", "rulings.txt");

    /// <summary>The ledger's Ruling lines embedded in the exe (src/NRMerge/Resources/rulings.txt).</summary>
    public static string[] EmbeddedRulings()
    {
        using var s = typeof(Report).Assembly.GetManifestResourceStream("rulings.txt");
        if (s == null) return Array.Empty<string>();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Writes the ledger's Ruling lines to <paramref name="outPath"/> (command <c>export-rulings</c>).</summary>
    public static int ExportRulings(string ledgerPath, string outPath)
    {
        var lines = File.ReadAllLines(ledgerPath).Where(l => l.Contains("Ruling:")).ToList();
        File.WriteAllText(outPath, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"{lines.Count} rulings -> {outPath}");
        return 0;
    }

    /// <summary>Delivery templates (README.txt, Collect Crash Report.bat/.ps1): dist-src in dev mode, Data\templates in a release.</summary>
    public static string TemplatesDir => Paths.DevData ? Path.Combine(BuildConfig.DevRepo, "dist-src") : Path.Combine(Paths.Data, "templates");
    static readonly string[] Templates = { "README.txt", "Collect Crash Report.bat", "Collect-CrashReport.ps1" };

    /// <summary>VERSION.txt of a builder-made delivery.</summary>
    public static string VersionText(string builderVersion, DateTime built, bool eldenRing, string coopCode = null) =>
        $"Elden Vins x More Map Variations (merged), built on this PC by the VinsMMV merge builder {builderVersion}\r\n"
        + $"Built {built:yyyy-MM-dd HH:mm}. Inputs: Elden Vins Nightreign + More Map Variations 2.1.8-hotfix3 & Weapons (both checked against the builder's manifests). Game: Nightreign 1.03.5 (regulation 10350000).\r\n"
        + (eldenRing ? "Elden Ring 1.16.1 was used as merge base for the content Elden Vins ports from Elden Ring (same as the verified build).\r\n"
                     : "Built without Elden Ring: the Elden-Ring-based decisions were replayed from the verified build, so the game data is identical to it (see merge-journal\no-eldenring.tsv).\r\n")
        + (coopCode == null ? "" : $"Co-op code: {coopCode} (everyone in a co-op group needs the same code; compare before a run).\r\n");

    /// <summary>Moves the verified staging tree into the output folder with the profile, report and journals.
    /// The output folder must be empty, except for the build workspace itself when it lies inside it (builder layout
    /// <c>&lt;output&gt;\_build</c>).</summary>
    public static int Deliver() => Deliver(null, null);

    /// <param name="rulings">Ruling lines for MERGE_REPORT.md (null = the dev ledger).</param>
    /// <param name="versionText">VERSION.txt content (null = no VERSION.txt and no templates, as the dev command did).</param>
    public static int Deliver(IEnumerable<string> rulings, string versionText)

    {
        var ok = Path.Combine(Paths.Out, "verify.ok");
        if (!File.Exists(ok) || File.GetLastWriteTimeUtc(ok) < Directory.GetFiles(Paths.OutMod, "*", SearchOption.AllDirectories).Max(File.GetLastWriteTimeUtc))
        { Console.WriteLine("refusing: run verify successfully after the last change to out/mod"); return 1; }
        var final = Paths.FinalDir;
        Directory.CreateDirectory(final);
        var workspace = Path.GetFullPath(Paths.Workspace).TrimEnd('\\');
        if (Directory.EnumerateFileSystemEntries(final).Any(e => !string.Equals(Path.GetFullPath(e).TrimEnd('\\'), workspace, StringComparison.OrdinalIgnoreCase)))
        { Console.WriteLine("refusing: output folder is not empty"); return 1; }
        File.WriteAllText(Path.Combine(final, "MERGE_REPORT.md"), Build(rulings ?? File.ReadAllLines(LedgerPath)), new UTF8Encoding(false));
        var jdir = Path.Combine(final, "merge-journal");
        Directory.CreateDirectory(jdir);
        foreach (var f in Directory.GetFiles(Journal.Dir, "*.tsv")) File.Copy(f, Path.Combine(jdir, Path.GetFileName(f)));
        foreach (var f in new[] { "assembly.tsv", "remap.json", "judge_remap.json" }) File.Copy(Path.Combine(Paths.Out, f), Path.Combine(jdir, f));
        File.Copy(Path.Combine(Paths.Out, Profile.ProfileName), Path.Combine(final, Profile.ProfileName));
        Directory.Move(Paths.OutMod, Path.Combine(final, "mod"));
        if (versionText != null)
        {
            File.WriteAllText(Path.Combine(final, "VERSION.txt"), versionText, new UTF8Encoding(false));
            foreach (var t in Templates)
                if (File.Exists(Path.Combine(TemplatesDir, t))) File.Copy(Path.Combine(TemplatesDir, t), Path.Combine(final, t));
        }
        Console.WriteLine("delivered to " + final);
        return 0;
    }
}
