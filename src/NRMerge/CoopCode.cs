using System.Security.Cryptography;
using System.Text;

namespace NRMerge;

/// <summary>
/// Short code over the game data of a merged mod, for co-op partners to compare before a run: players whose codes differ run
/// different game data, which desyncs bosses (health bars that do not go down) and damage. Hashes content (decompressed;
/// regulation.bin decrypted), so the same data compressed with another Oodle build gives the same code. Logs, natives and their
/// configs are not game data and are ignored.
/// </summary>
public static class CoopCode
{
    static readonly HashSet<string> NotGameData = new(StringComparer.OrdinalIgnoreCase) { ".log", ".dll", ".exe", ".ini", ".json", ".yaml", ".toml", ".txt" };

    public static string Compute(string mod)
    {
        var rels = Directory.GetFiles(mod, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(mod, f).Replace('\\', '/'))
            .Where(r => !NotGameData.Contains(Path.GetExtension(r)))
            .Order(StringComparer.Ordinal).ToArray();
        var shas = new string[rels.Length];
        Parallel.For(0, rels.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => shas[i] = NoErPatch.Sha(NoErPatch.ReadContent(Path.Combine(mod, rels[i]), rels[i], out _)));
        var sb = new StringBuilder();
        for (int i = 0; i < rels.Length; i++) sb.Append(rels[i].ToLowerInvariant()).Append('\t').Append(shas[i]).Append('\n');
        var h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        return $"{h[..4]}-{h[4..8]}";
    }
}
