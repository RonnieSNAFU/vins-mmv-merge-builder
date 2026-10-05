using Andre.Core;
using Andre.Formats;
using Andre.Core.Util;
using SoulsFormats;

namespace NRMerge;

/// <summary>Extracts vanilla files from a game's encrypted data archives (NR, ER or DS3).</summary>
public static class Extract
{
    /// <summary>The opened data archives of one game; <see cref="Read"/> returns a file's stored bytes (as in the archive) or null.</summary>
    public sealed class Archives : IDisposable
    {
        readonly BinderArchive[] archives;
        readonly BHD5.Game bhdGame;

        public Archives(string gameName, string gameDir)
        {
            var game = gameName.ToUpperInvariant() switch
            {
                "NR" => Game.NR,
                "ER" => Game.ER,
                "DS3" => Game.DS3,
                _ => throw new ArgumentException(gameName)
            };
            bhdGame = game == Game.DS3 ? BHD5.Game.DarkSouls3 : BHD5.Game.EldenRing;
            archives = BinderArchive.FindBHDs(gameDir, game)
                .Select(s => new BinderArchive(s, s.Replace(".bhd", ".bdt"), game)).ToArray();
        }

        public int Count => archives.Length;

        public byte[] Read(string rel)
        {
            var hash = BhdDictionary.ComputeHash("/" + rel.Replace('\\', '/'), bhdGame);
            foreach (var a in archives)
            {
                var h = a.TryGetFileFromHash(hash);
                if (h != null) return a.ReadFile(h);
            }
            return null;
        }

        public void Dispose() { foreach (var a in archives) a.Dispose(); }
    }

    public static int Run(string gameName, string gameDir, string listFile, string outDir)
    {
        using var archives = new Archives(gameName, gameDir);
        Console.WriteLine($"opened {archives.Count} archives");
        var wanted = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        int found = 0, missing = 0;
        var missingList = new List<string>();
        foreach (var rel in wanted)
        {
            var data = archives.Read(rel);
            if (data == null) { missing++; missingList.Add(rel); continue; }
            var dst = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.WriteAllBytes(dst, data);
            found++;
        }
        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(outDir, "_not_in_vanilla.txt"), missingList);
        Console.WriteLine($"extracted {found}, not found {missing}");
        return 0;
    }
}
