using System.Text;

namespace NRMerge;

/// <summary>
/// Decisions taken because Elden Ring is not available (<c>--no-eldenring</c>, or not installed): every place that would have
/// read an Elden Ring base records what it did instead. The list lives for the whole process (stages clear the normal
/// <see cref="Journal"/>), and <see cref="Journal.Save"/> writes it to <c>&lt;journal&gt;\no-eldenring.tsv</c>.
/// With Elden Ring present nothing is recorded and no file is written.
/// </summary>
public static class ErFallback
{
    public const string Area = "no-eldenring";
    static readonly List<(string Item, string Decision)> _entries = new();
    static readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    static Dictionary<string, string> _erRels;
    static string _erRelsSource;

    public static IReadOnlyList<(string Item, string Decision)> Entries => _entries;

    public static void Reset() { lock (_entries) { _entries.Clear(); _seen.Clear(); } }

    /// <summary>Records a fallback once per (item, decision).</summary>
    public static void Note(string item, string decision)
    {
        lock (_entries)
            if (_seen.Add(item + "\t" + decision)) _entries.Add((item, decision));
    }

    static string NormRel(string rel) => (rel ?? "").Replace('\\', '/').TrimStart('/');

    /// <summary>True when the shipped vanilla manifest lists <paramref name="rel"/> as an Elden Ring base file, i.e. the merge of
    /// the verified build read it from vanilla_er.</summary>
    public static bool IsErBase(string rel) => ErBaseSha(rel) != null;

    /// <summary>SHA-256 of the Elden Ring base file of <paramref name="rel"/> per the shipped vanilla manifest, or null when the
    /// verified build had no Elden Ring base for it. Lets file-level decisions ("which mod changed this file?") be made exactly as
    /// in the verified build without the file itself.</summary>
    public static string ErBaseSha(string rel)
    {
        var src = VanillaManifest.DefaultPath;
        if (_erRels == null || _erRelsSource != src)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(src))
                foreach (var e in VanillaManifest.Load(src).Entries)
                    if (e.Game == "ER" && !e.Absent) map[NormRel(e.Rel)] = e.Sha256;
            (_erRels, _erRelsSource) = (map, src);
        }
        return _erRels.GetValueOrDefault(NormRel(rel));
    }

    /// <summary>Records that the Elden Ring original of <paramref name="rel"/> was not used for <paramref name="purpose"/>
    /// (only when the verified build had one).</summary>
    public static void NoErBase(string rel, string purpose)
    {
        if (IsErBase(rel)) Note(NormRel(rel), $"no Elden Ring base: {purpose}");
    }

    /// <summary>Writes the accumulated entries to <paramref name="dir"/>\no-eldenring.tsv (nothing when empty).</summary>
    public static void Save(string dir)
    {
        List<(string, string)> copy;
        lock (_entries) copy = _entries.ToList();
        if (copy.Count == 0) return;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("item\tdecision\n");
        foreach (var (i, d) in copy) sb.Append(Clean(i)).Append('\t').Append(Clean(d)).Append('\n');
        File.WriteAllText(Path.Combine(dir, Area + ".tsv"), sb.ToString(), new UTF8Encoding(false));
    }

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ");
}
