using System.Security.Cryptography;

namespace NRMerge;

/// <summary>One vanilla base file: which game's archives it comes from, its mod-relative path, SHA-256 and size.
/// <see cref="Sha256"/> null = the file must NOT exist in that game's vanilla folder (from <c>_not_in_vanilla.txt</c>).</summary>
public sealed record VanillaEntry(string Game, string Rel, string Sha256, long Size)
{
    public bool Absent => Sha256 == null;
}

/// <summary>
/// The exact set of vanilla Nightreign (<c>vanilla\</c>, game "NR") and Elden Ring (<c>vanilla_er\</c>, game "ER") base files the
/// merge was made with. The release ships only paths and hashes (data\vanilla-manifest.tsv); the files themselves are extracted from
/// the player's game archives. The set must be exact: <see cref="Paths.BaseOf"/> and directory scans depend on which files exist.
/// </summary>
public sealed class VanillaManifest
{
    public const string AbsentList = "_not_in_vanilla.txt";
    const string Header = "game\trel\tsha256\tsize";
    const string AbsentMark = "absent";

    public List<VanillaEntry> Entries { get; } = new();

    public static string DefaultPath => Path.Combine(Paths.Manifests, "vanilla-manifest.tsv");

    public static VanillaManifest Load(string path)
    {
        var m = new VanillaManifest();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (line.Length == 0) continue;
            var c = line.Split('\t');
            m.Entries.Add(new VanillaEntry(c[0], c[1], c[2] == AbsentMark ? null : c[2], long.Parse(c[3])));
        }
        return m;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllLines(path, new[] { Header }.Concat(Entries.Select(e => $"{e.Game}\t{e.Rel}\t{e.Sha256 ?? AbsentMark}\t{e.Size}")));
    }

    /// <summary>Builds the manifest from existing vanilla folders (dev command <c>make-vanilla-manifest</c>).</summary>
    public static VanillaManifest Create(string vanillaDir, string vanillaErDir)
    {
        var m = new VanillaManifest();
        foreach (var (game, dir) in Dirs(vanillaDir, vanillaErDir))
        {
            m.Entries.AddRange(Files(dir).Select(rel => { var f = Paths.In(dir, rel); return new VanillaEntry(game, rel, Sha(f), new FileInfo(f).Length); }));
            var absent = Path.Combine(dir, AbsentList);
            if (File.Exists(absent))
                m.Entries.AddRange(File.ReadAllLines(absent).Select(l => l.Trim()).Where(l => l.Length > 0)
                    .Select(l => new VanillaEntry(game, l.Replace('\\', '/'), null, 0)));
        }
        return m;
    }

    /// <summary>Differences between the folders and the manifest (missing, changed, extra, present-but-absent files); empty = exact.</summary>
    public List<string> Check(string vanillaDir, string vanillaErDir)
    {
        var problems = new List<string>();
        foreach (var (game, dir) in Dirs(vanillaDir, vanillaErDir))
        {
            var mine = Entries.Where(e => e.Game == game).ToList();
            foreach (var e in mine)
            {
                var f = Paths.In(dir, e.Rel);
                if (e.Absent) { if (File.Exists(f)) problems.Add($"{game} {e.Rel}: present but should be absent"); }
                else if (!File.Exists(f)) problems.Add($"{game} {e.Rel}: missing");
                else if (new FileInfo(f).Length != e.Size || Sha(f) != e.Sha256) problems.Add($"{game} {e.Rel}: changed");
            }
            var known = mine.Select(e => e.Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                problems.AddRange(Files(dir).Where(r => !known.Contains(r)).Select(r => $"{game} {r}: extra file not in the manifest"));
        }
        return problems;
    }

    /// <summary>
    /// Writes every listed file, read through <paramref name="read"/>(game, rel), into the two folders and verifies its hash;
    /// files already present with the right hash are skipped, a file with a wrong hash is not written. Also writes the
    /// <c>_not_in_vanilla.txt</c> lists. Returns the problems (empty = success).
    /// </summary>
    public List<string> ExtractTo(Func<string, string, byte[]> read, string vanillaDir, string vanillaErDir, IProgress<string> progress)
    {
        var problems = new List<string>();
        foreach (var (game, dir) in Dirs(vanillaDir, vanillaErDir))
        {
            var mine = Entries.Where(e => e.Game == game).ToList();
            var present = mine.Where(e => !e.Absent).ToList();
            int done = 0, skipped = 0;
            foreach (var e in present)
            {
                done++;
                var f = Paths.In(dir, e.Rel);
                if (File.Exists(f) && new FileInfo(f).Length == e.Size && Sha(f) == e.Sha256) { skipped++; continue; }
                var data = read(game, e.Rel);
                if (data == null) { problems.Add($"{game} {e.Rel}: not found in the game's archives"); continue; }
                if (data.LongLength != e.Size || Convert.ToHexStringLower(SHA256.HashData(data)) != e.Sha256)
                {
                    problems.Add($"{game} {e.Rel}: differs from the version this merge was made for");
                    if (File.Exists(f)) File.Delete(f);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, data);
                if (done % 50 == 0) progress?.Report($"{game}: {done}/{present.Count}");
            }
            progress?.Report($"{game}: {present.Count} files ({skipped} already present)");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, AbsentList), mine.Where(e => e.Absent).Select(e => e.Rel));
            problems.AddRange(mine.Where(e => e.Absent && File.Exists(Paths.In(dir, e.Rel))).Select(e => $"{game} {e.Rel}: present but should be absent"));
        }
        return problems;
    }

    /// <summary>Extracts the shipped manifest's files from the configured games into <c>&lt;WorkDir&gt;\vanilla{,_er}</c>.
    /// Throws <see cref="BuildException"/> listing the files that are missing or differ.</summary>
    public static void Extract(BuildConfig cfg, IProgress<string> progress, string manifestPath = null)
    {
        var m = Load(manifestPath ?? DefaultPath);
        var opened = new Dictionary<string, NRMerge.Extract.Archives>();
        try
        {
            byte[] Read(string game, string rel)
            {
                if (!opened.TryGetValue(game, out var a))
                {
                    var dir = game == "NR" ? cfg.NrGame : cfg.ErGame;
                    progress?.Report($"opening {game} archives in {dir}");
                    opened[game] = a = new NRMerge.Extract.Archives(game, dir);
                    if (a.Count == 0) throw new BuildException($"No data archives (*.bhd) found in \"{dir}\"; is this the {(game == "NR" ? "Nightreign" : "Elden Ring")} Game folder?");
                }
                return a.Read(rel);
            }
            string erDir = Path.Combine(cfg.WorkDir, "vanilla_er");
            if (cfg.ErGame == null)
            {
                int n = m.Entries.Count(e => e.Game == "ER" && !e.Absent);
                ErFallback.Note("vanilla_er", $"no Elden Ring: {n} Elden Ring base files not extracted");
                progress?.Report($"ER: skipped ({n} Elden Ring base files; building without Elden Ring)");
                if (Directory.Exists(erDir)) Directory.Delete(erDir, true);
                erDir = null;
            }
            var problems = m.ExtractTo(Read, Path.Combine(cfg.WorkDir, "vanilla"), erDir, progress);
            if (problems.Count > 0)
                throw new BuildException($"{problems.Count} vanilla game file(s) could not be extracted as expected (is the game up to date and unmodified?):\n  "
                    + string.Join("\n  ", problems.Take(20)) + (problems.Count > 20 ? $"\n  ... and {problems.Count - 20} more" : ""));
        }
        finally { foreach (var a in opened.Values) a.Dispose(); }
    }

    /// <summary>The folders to handle; a null Elden Ring folder (building without Elden Ring) skips the ER rows.</summary>
    static IEnumerable<(string game, string dir)> Dirs(string nr, string er) => er == null ? new[] { ("NR", nr) } : new[] { ("NR", nr), ("ER", er) };

    /// <summary>Mod-relative paths (forward slashes, ordinal order) of the files in a vanilla folder, without the absent list.</summary>
    static IEnumerable<string> Files(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
        .Where(r => r != AbsentList)
        .Order(StringComparer.Ordinal);

    static string Sha(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}
