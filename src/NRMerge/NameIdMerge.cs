using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>Union of action name-ID tables (eventnameid/statenameid/variablenameid): EV's entries, then names only MMV has.</summary>
public static class NameIdMerge
{
    static readonly Regex Entry = new(@"^\s*(\d+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    static List<string> Names(string text) =>
        text.Replace("\0", "").Split('\n').Select(l => Entry.Match(l.TrimEnd('\r'))).Where(m => m.Success).Select(m => m.Groups[2].Value).ToList();

    public static string Merge(string ev, string mmv, out List<string> added)
    {
        var have = Names(ev).ToHashSet();
        added = Names(mmv).Where(n => !have.Contains(n)).Distinct().ToList();
        if (added.Count == 0) return ev;
        var lines = ev.Replace("\0", "").Split("\r\n").ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        int max = lines.Select(l => Entry.Match(l)).Where(m => m.Success).Max(m => int.Parse(m.Groups[1].Value));
        foreach (var n in added) lines.Add($"{(++max).ToString().PadRight(4)} = \"{n}\"");
        int count = Names(string.Join("\n", lines)).Count;
        int numAt = lines.FindIndex(l => l.StartsWith("Num"));
        lines[numAt] = $"Num  = {count}";
        return string.Join("\r\n", lines) + "\r\n";
    }
}
