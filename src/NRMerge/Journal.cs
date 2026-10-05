using System.Text;

namespace NRMerge;

/// <summary>Records every merge decision; each stage's areas are written to their own TSV (rerunning a stage replaces them).</summary>
public static class Journal
{
    static string _dir;
    /// <summary>Journal folder: out\journal of the active config unless overridden (tests); <see cref="Paths.Use"/> clears the override.</summary>
    public static string Dir { get => _dir ?? Path.Combine(Paths.Out, "journal"); set => _dir = value; }
    static readonly List<(string Area, string Item, string Decision)> Entries = new();

    public static void Add(string area, string item, string decision) => Entries.Add((area, item, decision));

    public static void Clear() => Entries.Clear();

    public static int Count(string area) => Entries.Count(e => e.Area == area);

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        foreach (var g in Entries.GroupBy(e => e.Area))
        {
            var sb = new StringBuilder("item\tdecision\n");
            foreach (var e in g) sb.Append(Clean(e.Item)).Append('\t').Append(Clean(e.Decision)).Append('\n');
            File.WriteAllText(Path.Combine(Dir, g.Key + ".tsv"), sb.ToString(), new UTF8Encoding(false));
        }
        ErFallback.Save(Dir);
    }

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ");
}
