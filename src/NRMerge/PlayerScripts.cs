using System.Text;

namespace NRMerge;

/// <summary>
/// Stage (Task 12): c0000.hks three-way text merge (base El-Fonz0 197b182, fetched pinned + hashed by Fetch; 14 hunks resolved in-process by HksResolve),
/// c9997.hks (MMV's re-pointed file: EV's only change, ANIME_ID_ATTACK_END 3050, is covered by MMV's 3099), name-ID tables.
/// </summary>
public static class PlayerScripts
{
    static string Lf(string path) => File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");

    /// <summary>
    /// <c>git merge-file -p --diff3 -L EV -L BASE -L MMV ev base mmv</c> in-process: the files' bytes are read as UTF-8 (no BOM
    /// stripping, as git sees them) and the result is returned as UTF-8 bytes without BOM. <paramref name="conflicts"/> = hunk count.
    /// </summary>
    public static byte[] C0000Diff3(string evPath, string basePath, string mmvPath, out int conflicts)
    {
        var utf8 = new UTF8Encoding(false);
        string Read(string p) => utf8.GetString(File.ReadAllBytes(p));
        var text = TextMerge3.Merge(Read(evPath), Read(basePath), Read(mmvPath), TextMergeStyle.Diff3, "EV", "BASE", "MMV", out conflicts);
        return utf8.GetBytes(text);
    }

    public static int Run()
    {
        Journal.Clear();
        var work = Path.Combine(Paths.Work, "hks");
        Directory.CreateDirectory(work);
        const string rel = "action/script/c0000.hks";
        var ev = Path.Combine(work, "ev.hks"); var mmv = Path.Combine(work, "mmv.hks"); var bas = Fetch.Get("c0000-hks-base-197b182", Paths.Config);
        File.WriteAllText(ev, Lf(Paths.In(Paths.EV, rel)), new UTF8Encoding(false));
        File.WriteAllText(mmv, Lf(MmvRewrite.MmvInput(rel)), new UTF8Encoding(false));
        var d3 = C0000Diff3(ev, bas, mmv, out var hunks);
        Journal.Add("player", rel, $"diff3 merge (TextMerge3): {hunks} conflict hunk(s)");
        var d3Path = Path.Combine(work, "c0000.diff3.hks");
        File.WriteAllBytes(d3Path, d3);
        var resolved = Path.Combine(work, "c0000.resolved.hks");
        PyText.Write(resolved, HksResolve.Resolve(PyText.Read(d3Path)));
        var text = File.ReadAllText(resolved, Encoding.UTF8);
        if (text.Contains("<<<<<<<") || text.Contains(">>>>>>>")) throw new InvalidDataException("conflict markers left in c0000.hks");
        File.WriteAllText(Paths.In(Paths.OutMod, rel), text.Replace("\n", "\r\n"), new UTF8Encoding(false));
        Journal.Add("player", rel, "14 hunks resolved by HksResolve (both sides' additions kept; EV conditions + MMV's lists/bodies for MMV weapon kinds and DS3 paired weapons)");

        const string rel9 = "action/script/c9997.hks";
        File.Copy(MmvRewrite.MmvInput(rel9), Paths.In(Paths.OutMod, rel9), true);
        Journal.Add("player", rel9, "MMV's (re-pointed) file: EV's only semantic change ANIME_ID_ATTACK_END=3050 is contained in MMV's 3099; EV's other differences are decompiler naming");

        foreach (var t in new[] { "eventnameid", "statenameid", "variablenameid" })
        {
            var r = $"action/{t}.txt";
            var sjis = Encoding.GetEncoding("shift_jis");
            var merged = NameIdMerge.Merge(File.ReadAllText(Paths.In(Paths.EV, r), sjis), File.ReadAllText(MmvRewrite.MmvInput(r), sjis), out var added);
            File.WriteAllText(Paths.In(Paths.OutMod, r), merged, sjis);
            Journal.Add("player", r, added.Count == 0 ? "EV's table (MMV's adds no names)" : $"EV's table + MMV-only names: {string.Join(",", added)}");
        }
        Journal.Save();
        Console.WriteLine("player scripts written");
        return 0;
    }
}
