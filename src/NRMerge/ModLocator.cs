using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NRMerge;

/// <summary>A place where a mod download was found: an extracted folder (its download root, the folder holding the .me3) or an archive.</summary>
public sealed record ModCandidate(string Path, bool IsArchive, int Score, string Why);

/// <summary>
/// Finds the players' mod downloads without asking: folders holding the mod's .me3 and .zip/.7z/.rar archives containing it,
/// under the Nightreign install folder, Downloads, Desktop, Documents and the Steam libraries. Candidates are ranked
/// (extracted folder whose mod\regulation.bin has the manifest's hash > other folder > archive; inside the Nightreign folder breaks
/// ties); two candidates with the top score are ambiguous and the builder asks the player to pick.
/// </summary>
public static class ModLocator
{
    public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };
    public const int DefaultDepth = 3;

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(System.IO.Path.GetExtension(path ?? ""), StringComparer.OrdinalIgnoreCase);

    /// <summary>Default places to look: the Nightreign install folder, Downloads, Desktop, Documents, and every Steam library's
    /// Nightreign folder (duplicates removed).</summary>
    public static List<string> DefaultRoots(string nightreignGameDir, IEnumerable<string> steamLibraries)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        if (nightreignGameDir != null) roots.Add(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(nightreignGameDir).TrimEnd('\\')));
        roots.Add(System.IO.Path.Combine(profile, "Downloads"));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        foreach (var lib in steamLibraries ?? Array.Empty<string>())
            roots.Add(System.IO.Path.Combine(lib, "steamapps", "common", SteamLocator.NightreignFolder));
        return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => System.IO.Path.GetFullPath(r).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every folder or archive under <paramref name="roots"/> (to <paramref name="depth"/> levels) that holds
    /// <paramref name="spec"/>'s .me3, ranked best first. Folders inside <paramref name="exclude"/> are skipped.</summary>
    public static List<ModCandidate> Find(ModSpec spec, IEnumerable<string> roots, string nightreignRoot = null,
        IEnumerable<string> exclude = null, int depth = DefaultDepth)
    {
        var skip = (exclude ?? Array.Empty<string>()).Where(e => e != null).Select(e => System.IO.Path.GetFullPath(e).TrimEnd('\\')).ToList();
        var found = new Dictionary<string, ModCandidate>(StringComparer.OrdinalIgnoreCase);
        var regSha = spec.Files.FirstOrDefault(f => string.Equals(f.Path, "mod/regulation.bin", StringComparison.OrdinalIgnoreCase))?.Sha256;

        bool Skipped(string dir) => skip.Any(s => dir.Equals(s, StringComparison.OrdinalIgnoreCase)
            || dir.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase));

        void Visit(string dir, int level)
        {
            if (Skipped(dir) || found.ContainsKey(dir)) return;
            try
            {
                if (File.Exists(System.IO.Path.Combine(dir, spec.Me3)))
                {
                    if (File.Exists(System.IO.Path.Combine(dir, ModManifest.MergedMe3))) return;
                    int score = 2;
                    var why = new List<string> { "extracted folder" };
                    var reg = System.IO.Path.Combine(dir, "mod", "regulation.bin");
                    if (regSha != null && File.Exists(reg) && Sha(reg) == regSha) { score += 4; why.Add("regulation.bin matches"); }
                    if (Under(dir, nightreignRoot)) { score += 1; why.Add("inside the Nightreign folder"); }
                    found[dir] = new ModCandidate(dir, false, score, string.Join(", ", why));
                    return;   // never look inside a mod download
                }
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (IsArchive(f) && !found.ContainsKey(f) && ArchiveContains(f, spec.Me3))
                        found[f] = new ModCandidate(f, true, Under(f, nightreignRoot) ? 1 : 0,
                            "archive" + (Under(f, nightreignRoot) ? ", inside the Nightreign folder" : ""));
                if (level >= depth) return;
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = System.IO.Path.GetFileName(d);
                    var attr = File.GetAttributes(d);
                    if ((attr & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 || name.StartsWith('.')
                        || name.Equals("mod", StringComparison.OrdinalIgnoreCase) || name.Equals("_build", StringComparison.OrdinalIgnoreCase)) continue;
                    Visit(d, level + 1);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        }

        foreach (var r in roots ?? Array.Empty<string>())
            if (!string.IsNullOrEmpty(r) && Directory.Exists(r)) Visit(System.IO.Path.GetFullPath(r).TrimEnd('\\'), 0);
        return found.Values.OrderByDescending(c => c.Score).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The best candidate, or null with <paramref name="ambiguous"/> set when two share the top score (or none exists).</summary>
    public static ModCandidate Choose(IReadOnlyList<ModCandidate> ranked, out bool ambiguous)
    {
        ambiguous = ranked.Count > 1 && ranked[0].Score == ranked[1].Score;
        return ranked.Count == 0 || ambiguous ? null : ranked[0];
    }

    static bool Under(string path, string root) =>
        root != null && System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    static string Sha(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    /// <summary>True when the archive lists a file named <paramref name="fileName"/> (any folder). Unreadable archives: false.</summary>
    public static bool ArchiveContains(string archive, string fileName)
    {
        try
        {
            using var a = ArchiveFactory.OpenArchive(archive);
            return a.Entries.Any(e => !e.IsDirectory && e.Key != null
                && string.Equals(System.IO.Path.GetFileName(e.Key.Replace('\\', '/').TrimEnd('/')), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Extracts <paramref name="archive"/> into <paramref name="destDir"/> (emptied first) and returns the download root
    /// inside it (the folder holding <paramref name="me3"/>).</summary>
    public static string Extract(string archive, string destDir, string me3, IProgress<string> progress = null)
    {
        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        Directory.CreateDirectory(destDir);
        var root = System.IO.Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
        try
        {
            using var a = ArchiveFactory.OpenArchive(archive);
            var entries = a.Entries.Where(e => !e.IsDirectory).ToList();
            int n = 0;
            void Write(string key, Action<Stream> copy)
            {
                var rel = key.Replace('/', '\\').TrimStart('\\');
                var dst = System.IO.Path.GetFullPath(System.IO.Path.Combine(destDir, rel));
                if (!dst.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"entry '{key}' points outside the folder");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
                using (var o = File.Create(dst)) copy(o);
                if (++n % 200 == 0) progress?.Report($"  {n}/{entries.Count} files");
            }
            if (a.IsSolid || a.Type == ArchiveType.SevenZip)
            {
                // solid archives and 7z must be read front to back
                using var reader = a.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                    if (!reader.Entry.IsDirectory) Write(reader.Entry.Key, o => reader.WriteEntryTo(o));
            }
            else
                foreach (var e in entries)
                    Write(e.Key, o => { using var s = e.OpenEntryStream(); s.CopyTo(o); });
            progress?.Report($"  {n} files extracted");
        }
        catch (Exception e) when (e is not BuildException)
        {
            throw new BuildException($"Could not extract \"{archive}\" ({e.Message}). Extract it yourself (e.g. with 7-Zip) and run the builder again, or pick the extracted folder.", e);
        }
        var found = FindMe3Dir(destDir, me3);
        if (found == null) throw new BuildException($"\"{archive}\" does not contain \"{me3}\": it is not the expected mod download.");
        return found;
    }

    static string FindMe3Dir(string dir, string me3) =>
        Directory.EnumerateFiles(dir, me3, SearchOption.AllDirectories).Select(System.IO.Path.GetDirectoryName)
            .OrderBy(d => d.Length).FirstOrDefault();

    /// <summary>A player-given mod location as the download root: a .me3 file -> its folder; a 'mod' folder -> its parent;
    /// a folder holding the .me3 (or a single nested one) -> that folder (see <see cref="ModManifest.ResolveRoot"/>).</summary>
    public static string NormalizeFolder(string path, string me3 = null)
    {
        var full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        if (File.Exists(full) && full.EndsWith(".me3", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetDirectoryName(full);
        return ModManifest.ResolveRoot(full, me3);
    }
}
