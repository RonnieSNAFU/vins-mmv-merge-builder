using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NRMerge;

/// <summary>One file of a mod download. Path is relative to the download root, '/' separated.</summary>
public sealed class ManifestFile
{
    public string Path { get; set; }
    public long Size { get; set; }
    /// <summary>Lower-case hex SHA-256; null for presence-only files (user-editable configs and the .me3 profile).</summary>
    public string Sha256 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PresenceOnly { get; set; }
}

/// <summary>Expected mod download: metadata plus the list of game-relevant files.</summary>
public sealed class ModSpec
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>Short name used in sentences ("Elden Vins", "More Map Variations").</summary>
    public string ShortName { get; set; }
    public string Version { get; set; }
    public string NexusUrl { get; set; }
    /// <summary>File name of the download's ME3 profile, at the download root.</summary>
    public string Me3 { get; set; }
    public List<ManifestFile> Files { get; set; } = new();

    [JsonIgnore] public string DisplayName => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";

    public ModSpec Clone() => new()
    {
        Id = Id, Name = Name, ShortName = ShortName, Version = Version, NexusUrl = NexusUrl, Me3 = Me3,
        Files = Files.Select(f => new ManifestFile { Path = f.Path, Size = f.Size, Sha256 = f.Sha256, PresenceOnly = f.PresenceOnly }).ToList(),
    };

    /// <summary>Metadata of Elden Vins Nightreign (Nexus 287). The download carries no version string, so the
    /// manifest date identifies the version this merge was made for.</summary>
    public static ModSpec EvInfo(string manifestDate) => new()
    {
        Id = "ev", Name = "Elden Vins Nightreign", ShortName = "Elden Vins",
        Version = $"(the version this merge was made for, manifest dated {manifestDate})",
        NexusUrl = "https://www.nexusmods.com/eldenringnightreign/mods/287",
        Me3 = "ELDEN VINS NIGHTREIGN.me3",
    };

    /// <summary>Metadata of More Map Variations 2.1.8-hotfix3 &amp; Weapons (Nexus 578).</summary>
    public static ModSpec MmvInfo() => new()
    {
        Id = "mmv", Name = "More Map Variations", ShortName = "More Map Variations",
        Version = "2.1.8-hotfix3 & Weapons",
        NexusUrl = "https://www.nexusmods.com/eldenringnightreign/mods/578",
        Me3 = "MMV 2.1.8-hf3 & Weapons Mod.me3",
    };
}

public readonly record struct HashProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal)
{
    public override string ToString() =>
        $"{FilesDone}/{FilesTotal} files, {BytesDone / 1048576.0:0} / {BytesTotal / 1048576.0:0} MB";
}

public enum ModDiagnosis { Ok, NotFound, Empty, MergedOutput, Swapped, WrongVersion, Partial, Mismatch }

/// <summary>Manifest of the two mod downloads this merge was made for, and the check of a player's folders.</summary>
public sealed class ModManifest
{
    public const string MergedMe3 = "Elden Vins with more map variations.me3";
    /// <summary>Upper bound of parallel hashing threads.</summary>
    public static int MaxParallelism { get; set; } = Environment.ProcessorCount;

    public int FormatVersion { get; set; } = 1;
    public string Created { get; set; }
    public List<ModSpec> Mods { get; set; } = new();

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------- file classification ----------

    static readonly HashSet<string> PresenceOnlyExt = new(StringComparer.OrdinalIgnoreCase) { ".ini", ".json", ".yaml", ".yml" };
    /// <summary>Documents, images and backups: never read by the game or the merge.</summary>
    static readonly HashSet<string> NonGameExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".pdf", ".url", ".htm", ".html", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".bak", ".prev", ".tmp", ".orig",
    };

    /// <summary>Default location of the shipped manifest (the repo's data folder in dev mode).</summary>
    public static string DefaultPath => System.IO.Path.Combine(Paths.Manifests, "mod-manifest.json");

    /// <summary>Runtime output written while playing: any *.log and anything below a dll/logs folder.</summary>
    public static bool IsRuntime(string rel)
    {
        if (rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return true;
        var r = "/" + rel;
        return r.Contains("/dll/logs/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recorded by name and size only: user-editable configs, and the download's .me3 profile (ME3 managers
    /// rewrite profiles; the merge writes its own). The .me3 name still identifies the download (swap detection).</summary>
    public static bool IsPresenceOnly(string rel) =>
        PresenceOnlyExt.Contains(System.IO.Path.GetExtension(rel)) || IsRootMe3(rel);

    static bool IsRootMe3(string rel) => !rel.Contains('/') && rel.EndsWith(".me3", StringComparison.OrdinalIgnoreCase);

    /// <summary>In the manifest's scope: everything under mod/ plus .me3 profiles at the root.</summary>
    public static bool InScope(string rel) =>
        rel.StartsWith("mod/", StringComparison.OrdinalIgnoreCase) ||
        IsRootMe3(rel);

    /// <summary>An unexpected file that cannot change the merged output (docs, backups, configs, Smithbox
    /// project data, anything outside mod/). Other unexpected files under mod/ are fatal extras.</summary>
    public static bool IsIgnorableExtra(string rel)
    {
        if (!rel.StartsWith("mod/", StringComparison.OrdinalIgnoreCase)) return true;
        return IsNonGameUnderMod(rel) || PresenceOnlyExt.Contains(System.IO.Path.GetExtension(rel));
    }

    /// <summary>A manifest file whose absence cannot change the merged output (a warning, not a refusal): docs,
    /// readme*, images and backups under mod/, and anything under mod/.smithbox/. Missing configs, game files and
    /// the .me3 stay fatal.</summary>
    public static bool IsIgnorableMissing(string rel) =>
        rel.StartsWith("mod/", StringComparison.OrdinalIgnoreCase) && IsNonGameUnderMod(rel);

    static bool IsNonGameUnderMod(string rel)
    {
        if (rel.StartsWith("mod/.smithbox/", StringComparison.OrdinalIgnoreCase)) return true;
        if (System.IO.Path.GetFileName(rel).StartsWith("readme", StringComparison.OrdinalIgnoreCase)) return true;
        return NonGameExt.Contains(System.IO.Path.GetExtension(rel));
    }

    // ---------- create ----------

    /// <summary>Hashes both downloads and returns the manifest JSON (command make-mod-manifest).</summary>
    public static string Create(string evRoot, string mmvRoot, IProgress<HashProgress> progress = null, string date = null)
    {
        date ??= DateTime.Now.ToString("yyyy-MM-dd");
        var m = new ModManifest { Created = date };
        m.Mods.Add(Scan(ModSpec.EvInfo(date), evRoot, progress));
        m.Mods.Add(Scan(ModSpec.MmvInfo(), mmvRoot, progress));
        return m.ToJson();
    }

    /// <summary>Lists and hashes one download (folder may be the root or its mod folder).
    /// <paramref name="excluded"/> receives runtime files and out-of-scope files that were left out.</summary>
    public static ModSpec Scan(ModSpec info, string folder, IProgress<HashProgress> progress = null, List<string> excluded = null)
    {
        var root = ResolveRoot(folder, info.Me3);
        if (!File.Exists(System.IO.Path.Combine(root, info.Me3)))
            throw new BuildException($"'{root}' does not contain '{info.Me3}': select the {info.DisplayName} download folder.");
        var spec = info.Clone();
        spec.Files.Clear();
        var toHash = new List<(ManifestFile entry, string full)>();
        foreach (var (rel, fi) in Enumerate(root))
        {
            if (IsRuntime(rel) || !InScope(rel)) { excluded?.Add(rel); continue; }
            var e = new ManifestFile { Path = rel, Size = fi.Length };
            if (IsPresenceOnly(rel)) e.PresenceOnly = true;
            else toHash.Add((e, fi.FullName));
            spec.Files.Add(e);
        }
        var hashes = HashAll(toHash.Select(t => (t.full, t.entry.Size)).ToList(), progress);
        for (int i = 0; i < toHash.Count; i++) toHash[i].entry.Sha256 = hashes[i];
        spec.Files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        excluded?.Sort(StringComparer.Ordinal);
        return spec;
    }

    public static ModManifest Parse(string json) => JsonSerializer.Deserialize<ModManifest>(json, Json);

    /// <summary>Loads the shipped manifest (default <see cref="DefaultPath"/>).</summary>
    public static ModManifest Load(string path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            throw new BuildException($"The mod manifest '{path}' is missing; the builder package is incomplete, extract it again.");
        try { return Parse(File.ReadAllText(path)); }
        catch (JsonException e) { throw new BuildException($"The mod manifest '{path}' is damaged ({e.Message}); extract the builder package again.", e); }
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public ModSpec Get(string id) => Mods.Single(m => m.Id == id);

    // ---------- input normalization ----------

    /// <summary>Resolves a player-chosen folder to the download root (the folder holding the .me3):
    /// the folder itself, the parent of a 'mod' folder, or a single nested subfolder holding the .me3
    /// (archive extracted into an extra folder).</summary>
    public static string ResolveRoot(string folder, string me3 = null)
    {
        var full = System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (!Directory.Exists(full)) return full;
        if (HasMe3(full)) return full;
        var parent = System.IO.Path.GetDirectoryName(full);
        if (string.Equals(System.IO.Path.GetFileName(full), "mod", StringComparison.OrdinalIgnoreCase) && parent != null)
            return parent;
        if (Directory.Exists(System.IO.Path.Combine(full, "mod"))) return full;
        var subs = Directory.GetDirectories(full).Where(HasMe3).ToList();
        if (me3 != null)
        {
            var exact = subs.Where(s => File.Exists(System.IO.Path.Combine(s, me3))).ToList();
            if (exact.Count == 1) return exact[0];
        }
        return subs.Count == 1 ? subs[0] : full;
    }

    static bool HasMe3(string dir) => Directory.EnumerateFiles(dir, "*.me3").Any();

    static bool IsMergedOutput(string dir) =>
        Directory.Exists(dir) &&
        (File.Exists(System.IO.Path.Combine(dir, MergedMe3)) ||
         File.Exists(System.IO.Path.Combine(dir, "MERGE_REPORT.md")) ||
         Directory.Exists(System.IO.Path.Combine(dir, "merge-journal")));

    static IEnumerable<(string rel, FileInfo fi)> Enumerate(string root)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var fi in new DirectoryInfo(root).EnumerateFiles("*", opts))
            yield return (System.IO.Path.GetRelativePath(root, fi.FullName).Replace('\\', '/'), fi);
    }

    // ---------- check ----------

    public ModCheckResult Check(string id, string folder, IProgress<HashProgress> progress = null) =>
        Check(Get(id), folder, progress, Mods.Where(m => m.Id != id));

    /// <summary>Compares a player's folder with the expected download. <paramref name="others"/> are the other
    /// mods of the manifest, used to detect swapped folders.</summary>
    public static ModCheckResult Check(ModSpec spec, string folder, IProgress<HashProgress> progress = null, IEnumerable<ModSpec> others = null)
    {
        var r = new ModCheckResult { Spec = spec, Root = System.IO.Path.GetFullPath(folder) };
        string get = $"Download {spec.DisplayName} from {spec.NexusUrl}, extract it and select the folder that contains '{spec.Me3}'.";
        if (!Directory.Exists(r.Root))
            return r.Set(ModDiagnosis.NotFound, $"The folder '{r.Root}' does not exist. {get}");

        r.Root = ResolveRoot(folder, spec.Me3);
        if (IsMergedOutput(r.Root) || IsMergedOutput(System.IO.Path.GetFullPath(folder)))
            return r.Set(ModDiagnosis.MergedOutput,
                $"This is the merged mod, not the original download (the folder holds the output of this builder: '{MergedMe3}', MERGE_REPORT.md or merge-journal). Select the original {spec.DisplayName} download instead. {get}");

        var actual = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, fi) in Enumerate(r.Root))
        {
            if (IsRuntime(rel)) r.IgnoredRuntime.Add(rel);
            else actual[rel] = fi;
        }
        if (actual.Count == 0)
            return r.Set(ModDiagnosis.Empty, $"The folder '{r.Root}' is empty (or holds only runtime logs). {get}");

        var swapped = (others ?? Enumerable.Empty<ModSpec>()).FirstOrDefault(o => LooksLike(o, spec, r.Root, actual));
        if (swapped != null)
            return r.Set(ModDiagnosis.Swapped,
                $"This folder is {swapped.ShortName}, not {spec.ShortName}; did you swap them? Select the {swapped.ShortName} folder as {swapped.ShortName} and the {spec.ShortName} folder as {spec.ShortName}.");

        var toHash = new List<(ManifestFile entry, string full)>();
        foreach (var e in spec.Files)
        {
            if (!actual.TryGetValue(e.Path, out var fi))
            {
                (IsIgnorableMissing(e.Path) ? r.IgnorableMissing : r.Missing).Add(e.Path);
                continue;
            }
            if (e.PresenceOnly) continue;
            if (fi.Length != e.Size) { r.Changed.Add(e.Path); continue; }
            toHash.Add((e, fi.FullName));
        }
        var expected = new HashSet<string>(spec.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var byPath = spec.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var skinCandidates = new List<(string rel, ManifestFile hi, string full)>();
        foreach (var rel in actual.Keys.Where(k => !expected.Contains(k)))
        {
            // EV's OPEN-THIS-TO-INSTALL-SKINS.bat copies every part to its low-detail (_l) name: accepted when byte-identical
            if (LodParts.HighDetailOf(rel) is string hiRel && byPath.TryGetValue(hiRel, out var hi) && hi.Sha256 != null && actual[rel].Length == hi.Size)
            { skinCandidates.Add((rel, hi, actual[rel].FullName)); continue; }
            (IsIgnorableExtra(rel) ? r.IgnorableExtra : r.Extra).Add(rel);
        }

        var hashes = HashAll(toHash.Select(t => (t.full, t.entry.Size)).Concat(skinCandidates.Select(c => (c.full, c.hi.Size))).ToList(), progress);
        for (int i = 0; i < toHash.Count; i++)
            if (!string.Equals(hashes[i], toHash[i].entry.Sha256, StringComparison.OrdinalIgnoreCase))
                r.Changed.Add(toHash[i].entry.Path);
        for (int i = 0; i < skinCandidates.Count; i++)
            (string.Equals(hashes[toHash.Count + i], skinCandidates[i].hi.Sha256, StringComparison.OrdinalIgnoreCase) ? r.SkinsCopies : r.Extra).Add(skinCandidates[i].rel);

        foreach (var l in new[] { r.Missing, r.Changed, r.Extra, r.IgnorableExtra, r.IgnorableMissing, r.IgnoredRuntime, r.SkinsCopies }) l.Sort(StringComparer.Ordinal);
        return Diagnose(r, spec, get);
    }

    /// <summary>Quick fingerprint: the folder holds <paramref name="other"/>'s .me3 (and not the expected one),
    /// or its mod/regulation.bin is byte-identical to <paramref name="other"/>'s.</summary>
    static bool LooksLike(ModSpec other, ModSpec spec, string root, Dictionary<string, FileInfo> actual)
    {
        if (actual.ContainsKey(other.Me3) && !actual.ContainsKey(spec.Me3)) return true;
        var oreg = other.Files.FirstOrDefault(f => f.Path == "mod/regulation.bin");
        var sreg = spec.Files.FirstOrDefault(f => f.Path == "mod/regulation.bin");
        if (oreg?.Sha256 == null || !actual.TryGetValue("mod/regulation.bin", out var fi) || fi.Length != oreg.Size) return false;
        if (sreg != null && sreg.Sha256 == oreg.Sha256) return false;
        return string.Equals(HashFile(fi.FullName, null), oreg.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    static ModCheckResult Diagnose(ModCheckResult r, ModSpec spec, string get)
    {
        int total = Math.Max(1, spec.Files.Count);
        int miss = r.Missing.Count, chg = r.Changed.Count, extra = r.Extra.Count;
        if (miss == 0 && chg == 0 && extra == 0)
        {
            int cfg = spec.Files.Count(f => f.PresenceOnly);
            string warn = (r.SkinsCopies.Count > 0 ? $"; skins installer copies recognised ({r.SkinsCopies.Count} low-detail part(s), not needed: the merge writes its own)" : "") +
                          (r.IgnorableExtra.Count > 0 ? $"; {r.IgnorableExtra.Count} unexpected non-game file(s) ignored" : "") +
                          (r.IgnorableMissing.Count > 0 ? $"; {r.IgnorableMissing.Count} non-game file(s) missing (docs/readme, not needed for the merge)" : "");
            return r.Set(ModDiagnosis.Ok, $"OK: {spec.DisplayName} ({spec.Files.Count - cfg} files verified, {cfg} config files present{warn}).");
        }
        string counts = $"{chg} changed, {miss} missing, {extra} extra game file(s) out of {spec.Files.Count}";
        bool keyChanged = r.Changed.Contains("mod/regulation.bin");
        string partialText = $"{miss} of {spec.Files.Count} files of {spec.DisplayName} are missing: the download looks incomplete or partially extracted; please re-extract the downloaded archive (or download it again from {spec.NexusUrl}) and select the folder that contains '{spec.Me3}'. ({counts})";
        if (chg == 0 && extra == 0)
            return r.Set(ModDiagnosis.Partial, partialText);
        if (keyChanged || chg >= Math.Max(5, total / 10))
            return r.Set(ModDiagnosis.WrongVersion,
                $"This looks like a different version of {spec.ShortName}; this builder needs {spec.DisplayName}; download it from {spec.NexusUrl}. ({counts})");
        if (miss >= total / 5 && chg <= miss / 4)
            return r.Set(ModDiagnosis.Partial, partialText);
        return r.Set(ModDiagnosis.Mismatch,
            $"This folder differs from the {spec.DisplayName} download this merge was made for ({counts}). Changed or extra game files under mod\\ would change the merged mod and hand resolutions may not apply. Re-extract a clean copy of the download (from {spec.NexusUrl}) and select it.");
    }

    // ---------- hashing ----------

    const int BufferSize = 1 << 20;
    const long ProgressStep = 64L << 20;

    /// <summary>Hashes files in parallel (bounded by <see cref="MaxParallelism"/>), biggest first, reporting progress.</summary>
    static string[] HashAll(List<(string path, long size)> files, IProgress<HashProgress> progress)
    {
        var result = new string[files.Count];
        long bytesTotal = files.Sum(f => f.size), bytesDone = 0;
        int filesDone = 0;
        var gate = new object();
        void Report()
        {
            if (progress == null) return;
            lock (gate) progress.Report(new HashProgress(Volatile.Read(ref filesDone), files.Count, Interlocked.Read(ref bytesDone), bytesTotal));
        }
        Report();
        var order = Enumerable.Range(0, files.Count).OrderByDescending(i => files[i].size).ToList();
        Parallel.ForEach(order, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, MaxParallelism) }, i =>
        {
            long sinceReport = 0;
            result[i] = HashFile(files[i].path, n =>
            {
                Interlocked.Add(ref bytesDone, n);
                if ((sinceReport += n) >= ProgressStep) { sinceReport = 0; Report(); }
            });
            Interlocked.Increment(ref filesDone);
            Report();
        });
        return result;
    }

    public static string HashFile(string path, Action<long> onBytes)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int n;
            while ((n = fs.Read(buf, 0, BufferSize)) > 0)
            {
                h.AppendData(buf, 0, n);
                onBytes?.Invoke(n);
            }
            return Convert.ToHexStringLower(h.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }
}

public sealed class ModCheckResult
{
    public bool Ok => Kind == ModDiagnosis.Ok;
    public ModDiagnosis Kind { get; set; }
    public string Diagnosis { get; set; }
    /// <summary>The resolved download root that was checked.</summary>
    public string Root { get; set; }
    public ModSpec Spec { get; set; }
    public List<string> Missing { get; } = new();
    /// <summary>Unexpected game files under mod/ (fatal: they would change the merged output).</summary>
    public List<string> Extra { get; } = new();
    public List<string> Changed { get; } = new();
    /// <summary>Unexpected files that cannot affect the merge (warnings).</summary>
    public List<string> IgnorableExtra { get; } = new();
    /// <summary>Manifest files that are absent but cannot affect the merge (docs/readme/backups under mod/; warnings).</summary>
    public List<string> IgnorableMissing { get; } = new();
    /// <summary>Low-detail parts EV's skins installer copied from their high-detail part (byte-identical; ignored).</summary>
    public List<string> SkinsCopies { get; } = new();
    /// <summary>Runtime logs found and skipped.</summary>
    public List<string> IgnoredRuntime { get; } = new();

    internal ModCheckResult Set(ModDiagnosis kind, string text) { Kind = kind; Diagnosis = text; return this; }

    public string FormatReport(int maxPerCategory = 15)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Spec?.DisplayName} [{Spec?.Id}]: {(Ok ? "OK" : "PROBLEM (" + Kind + ")")}");
        sb.AppendLine($"  Folder: {Root}");
        sb.AppendLine($"  {Diagnosis}");
        void List(string title, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"  {title} ({items.Count}):");
            foreach (var s in items.Take(maxPerCategory)) sb.AppendLine($"    {s}");
            if (items.Count > maxPerCategory) sb.AppendLine($"    ... and {items.Count - maxPerCategory} more");
        }
        List("Missing", Missing);
        List("Changed", Changed);
        List("Extra game files", Extra);
        List("Ignored extra files (warning)", IgnorableExtra);
        List("Missing non-game files (warning)", IgnorableMissing);
        if (IgnoredRuntime.Count > 0) sb.AppendLine($"  Runtime logs ignored: {IgnoredRuntime.Count}");
        if (!Ok && Spec != null) sb.AppendLine($"  Expected: {Spec.DisplayName}, {Spec.NexusUrl}");
        return sb.ToString();
    }
}

/// <summary>Hashing progress on one console line (stderr), at most once per second.</summary>
public sealed class ConsoleHashProgress(string label = "hashing") : IProgress<HashProgress>
{
    DateTime last;
    public void Report(HashProgress p)
    {
        if ((DateTime.Now - last).TotalMilliseconds < 1000 && p.FilesDone != p.FilesTotal) return;
        last = DateTime.Now;
        Console.Error.Write($"\r  {label} {p}        ");
    }

    /// <summary>Ends the progress line.</summary>
    public void Done() => Console.Error.WriteLine();
}
