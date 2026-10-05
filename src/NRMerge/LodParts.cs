using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>
/// Stage <c>lodparts</c>: the game draws other players with the low-detail parts (<c>parts\X_l.partsbnd.dcx</c>). Elden Vins ships
/// none for its skins and armour, and its OPEN-THIS-TO-INSTALL-SKINS.bat copies every part to its <c>_l</c> name; without them
/// a co-op partner's model is missing (name and weapons still show). The merge does the same for every merged part, so the
/// result does not depend on whether a player ran the .bat. MMV's own low-detail model is kept only next to MMV's unchanged
/// part; anywhere else it would show MMV's model for an EV or merged part. Copies are hard links (no extra disk space) when
/// the file system allows it.
/// </summary>
public static class LodParts
{
    static readonly Regex Low = new(@"^(?<dir>(?:.*/)?parts/)(?<base>[^/]+)_l\.partsbnd\.dcx$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex High = new(@"^(?:.*/)?parts/[^/]+\.partsbnd\.dcx$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The high-detail part of a low-detail part path (mod-relative, '/'), or null.</summary>
    public static string HighDetailOf(string rel)
    {
        var m = Low.Match(rel.Replace('\\', '/'));
        return m.Success ? $"{m.Groups["dir"].Value}{m.Groups["base"].Value}.partsbnd.dcx" : null;
    }

    static string LowDetailOf(string rel) => rel[..^".partsbnd.dcx".Length] + "_l.partsbnd.dcx";

    /// <summary>True when <paramref name="rel"/> under <paramref name="root"/> is a byte-identical copy of its high-detail part
    /// (what EV's skins installer writes).</summary>
    public static bool IsSkinsCopy(string root, string rel)
    {
        var hi = HighDetailOf(rel);
        if (hi == null) return false;
        string l = Paths.In(root, rel), h = Paths.In(root, hi);
        return File.Exists(l) && File.Exists(h) && Assemble.SameBytes(l, h);
    }

    public static int Run()
    {
        var dir = Path.Combine(Paths.OutMod, "parts");
        if (!Directory.Exists(dir)) { Console.WriteLine("no parts"); return 0; }
        int linked = 0, keptMmv = 0;
        foreach (var f in Directory.GetFiles(dir, "*.partsbnd.dcx").Order(StringComparer.Ordinal))
        {
            var rel = "parts/" + Path.GetFileName(f);
            if (HighDetailOf(rel) != null || !High.IsMatch(rel)) continue;
            var lowRel = LowDetailOf(rel);
            var low = Paths.In(Paths.OutMod, lowRel);
            string mmvHi = Paths.In(Paths.MMV, rel), mmvLow = Paths.In(Paths.MMV, lowRel);
            if (File.Exists(mmvLow) && File.Exists(mmvHi) && Assemble.SameBytes(f, mmvHi))
            {
                if (!File.Exists(low) || !Assemble.SameBytes(low, mmvLow)) File.Copy(mmvLow, low, true);
                keptMmv++;
                continue;
            }
            if (File.Exists(low) && Assemble.SameBytes(low, f)) { linked++; continue; }
            var why = File.Exists(low) ? "replaced MMV's low-detail model (the part itself is not MMV's)" : "added";
            Link(f, low);
            Journal.Add("lodparts", lowRel, $"{why}: copy of {rel} (EV skins installer rule)");
            linked++;
        }
        Journal.Add("lodparts", "parts", $"{linked} low-detail part(s) are copies of the merged part, {keptMmv} are MMV's own");
        Console.WriteLine($"{linked} low-detail copies, {keptMmv} MMV low-detail parts kept");
        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

    static void Link(string existing, string newFile)
    {
        if (File.Exists(newFile)) File.Delete(newFile);
        if (!OperatingSystem.IsWindows() || !CreateHardLink(newFile, existing, IntPtr.Zero)) File.Copy(existing, newFile);
    }
}
