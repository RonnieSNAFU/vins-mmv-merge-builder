using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;
using HKLib.Serialization.hk2018.Binary.Util;
using System.Reflection;

namespace NRMerge;

/// <summary>Analysis helper: prints the type definitions embedded in an hk2018 tagfile (as the file declares them).</summary>
public static class HkxTypes
{
    static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);

    /// <summary>Type hashes declared by the last file read (THSH section), by type name.</summary>
    public static Dictionary<string, uint> LastHashes = new();

    public static IReadOnlyList<HavokTypeBuilder> Read(byte[] hkx)
    {
        var s = new HavokBinarySerializer();
        var r = new HavokBinaryReader(new MemoryStream(hkx));
        r.EnterSection("TAG0");
        Call(s, "ReadSDKV", r);
        Call(s, "ReadDATA", r);
        r.EnterSection("TYPE");
        Call(s, "ReadTPTR", r);
        var ts = (IReadOnlyList<string>)Call(s, "ReadTSTR", r);
        var builders = (IReadOnlyList<HavokTypeBuilder>)Call(s, "ReadTNA1", r, ts);
        var fs = (IReadOnlyList<string>)Call(s, "ReadFSTR", r);
        Call(s, "ReadTBDY", r, builders, fs);
        LastHashes = new Dictionary<string, uint>();
        if (r.GetSectionId() == "THSH")
        {
            r.EnterSection("THSH");
            int n = (int)r.ReadHavokVarUInt();
            for (int i = 0; i < n; i++) { int t = (int)r.ReadHavokVarUInt(); LastHashes[builders[t].Name] = r.ReadUInt32(); }
        }
        return builders;
    }

    static string Try(Func<string> f) { try { return f(); } catch (Exception e) { return "?" + e.GetType().Name; } }

    public static int Run(string file, string[] names)
    {
        var bytes = File.ReadAllBytes(file);
        if (file.EndsWith(".dcx")) bytes = Dcx.Decompress(bytes);
        if (file.Contains(".behbnd")) bytes = SoulsFormats.BND4.Read(bytes).Files.First(f => f.Name.Contains(@"\behaviors\", StringComparison.OrdinalIgnoreCase)).Bytes.ToArray();
        foreach (var b in Read(bytes))
        {
            if (b == null || !names.Any(n => b.Name == n || (n == "*"))) continue;
            Console.WriteLine($"{b.Name} size={b.Size} align={b.Alignment} ver={b.Version} fmt={b.Format} hash={(LastHashes.TryGetValue(b.Name, out var h) ? h : 0)}");
            foreach (var f in b.Fields) Console.WriteLine($"   {f.Offset,4} {f.Name} : {(Try(() => f.Type?.Name))} flags={f.Flags}");
        }
        return 0;
    }
}

/// <summary>Analysis helper: compares the types an hk2018 file declares with HKLib's registry (by name and serialized members).</summary>
public static class HkxTypeCompare
{
    public static int Run(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (file.EndsWith(".dcx")) bytes = Dcx.Decompress(bytes);
        if (file.Contains(".behbnd")) bytes = SoulsFormats.BND4.Read(bytes).Files.First(f => f.Name.Contains(@"\behaviors\", StringComparison.OrdinalIgnoreCase)).Bytes.ToArray();
        var reg = System.Xml.Linq.XElement.Load(Path.Combine(AppContext.BaseDirectory, "Res", "HavokTypeRegistry20180100.xml"));
        var regTypes = reg.Descendants("HavokType").GroupBy(x => (string)x.Attribute("Name")).ToDictionary(g => g.Key, g => g.ToList());
        int diff = 0;
        foreach (var b in HkxTypes.Read(bytes))
        {
            if (b == null || b.Name == "NULL") continue;
            var mine = string.Join(",", b.Fields.Select(f => $"{f.Name}@{f.Offset}"));
            if (!regTypes.TryGetValue(b.Name, out var cands)) { Console.WriteLine($"MISSING {b.Name}"); diff++; continue; }
            var theirs = cands.Select(c => string.Join(",", c.Descendants("Member").Select(m => $"{(string)m.Attribute("Name")}@{(string)m.Attribute("Offset")}"))).ToList();
            if (theirs.Contains(mine)) continue;
            diff++;
            Console.WriteLine($"DIFF {b.Name} size={b.Size}\n  file: {mine}\n  reg:  {string.Join(" || ", theirs)}");
        }
        Console.WriteLine($"{diff} differing types");
        return 0;
    }
}
