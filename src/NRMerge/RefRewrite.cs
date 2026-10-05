using SoulsFormats;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace NRMerge;

/// <summary>Old→new IDs of MMV rows moved by the regulation stage (out\remap.json), per param.</summary>
public sealed class RemapTable
{
    public readonly Dictionary<string, Dictionary<long, long>> ByParam = new(StringComparer.Ordinal);

    public static RemapTable FromList(IEnumerable<RegMerge.Remap> list)
    {
        var t = new RemapTable();
        foreach (var r in list)
        {
            if (!t.ByParam.TryGetValue(r.Param, out var d)) t.ByParam[r.Param] = d = new Dictionary<long, long>();
            d[r.Old] = r.New;
        }
        return t;
    }

    public static RemapTable Load(string path = null)
    {
        path ??= Path.Combine(Paths.Out, "remap.json");
        var list = JsonSerializer.Deserialize<List<RegMerge.Remap>>(File.ReadAllText(path)) ?? new();
        return FromList(list);
    }

    public bool TryMap(string param, long value, out long mapped)
    {
        mapped = value;
        return param != null && ByParam.TryGetValue(param, out var d) && d.TryGetValue(value, out mapped);
    }

    public IReadOnlyDictionary<long, long> For(params string[] parms)
    {
        var d = new Dictionary<long, long>();
        foreach (var p in parms)
            if (ByParam.TryGetValue(p, out var m)) foreach (var (k, v) in m) d[k] = v;
        return d;
    }
}

/// <summary>Reference-typed parameters of TAE events, read from Smithbox's NR template (which SoulsFormats' loader cannot read).</summary>
public sealed class TaeRefTemplate
{
    public sealed record RefDef(int Offset, int Size, string Ref);
    public readonly Dictionary<int, List<RefDef>> Refs = new();

    static int SizeOf(string tag) => tag switch
    {
        "s8" or "u8" or "b" => 1,
        "s16" or "u16" => 2,
        "s32" or "u32" or "f32" => 4,
        "s64" or "u64" or "f64" => 8,
        _ => throw new InvalidDataException("unknown TAE param type " + tag),
    };

    public static TaeRefTemplate Parse(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var t = new TaeRefTemplate();
        foreach (XmlNode ev in doc.DocumentElement.SelectNodes("event"))
        {
            int id = int.Parse(ev.Attributes["id"].Value);
            int off = 0;
            var list = new List<RefDef>();
            foreach (XmlNode p in ev.ChildNodes)
            {
                if (p.NodeType != XmlNodeType.Element) continue;
                int size = SizeOf(p.Name);
                int count = p.Attributes?["length"] != null ? int.Parse(p.Attributes["length"].Value) : 1;
                var r = p.Attributes?["ref"]?.Value;
                if (!string.IsNullOrEmpty(r) && count == 1) list.Add(new RefDef(off, size, r));
                off += size * count;
            }
            if (list.Count > 0) t.Refs[id] = list;
        }
        return t;
    }

    public static TaeRefTemplate Load(string path) => Parse(File.ReadAllText(path));
}

/// <summary>Instruction argument layouts from DarkScript3's nr-common.emedf.json.</summary>
public sealed class Emedf
{
    public sealed record Arg(string Name, int Type, int Offset, int Size);
    public sealed record Instr(int Bank, int Id, string Name, List<Arg> Args);
    public readonly Dictionary<(int, int), Instr> Instrs = new();

    static int SizeOf(int type) => type switch { 0 or 3 => 1, 1 or 4 => 2, _ => 4 };

    public static Emedf Load(string path)
    {
        var e = new Emedf();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var c in doc.RootElement.GetProperty("main_classes").EnumerateArray())
        {
            int bank = c.GetProperty("index").GetInt32();
            foreach (var ins in c.GetProperty("instrs").EnumerateArray())
            {
                int id = ins.GetProperty("index").GetInt32();
                var args = new List<Arg>();
                int off = 0;
                foreach (var a in ins.GetProperty("args").EnumerateArray())
                {
                    int type = a.GetProperty("type").GetInt32();
                    int size = SizeOf(type);
                    off = (off + size - 1) / size * size;
                    args.Add(new Arg(a.GetProperty("name").GetString(), type, off, size));
                    off += size;
                }
                e.Instrs[(bank, id)] = new Instr(bank, id, ins.GetProperty("name").GetString(), args);
            }
        }
        return e;
    }
}

/// <summary>Re-points MMV-origin references to rows the regulation stage moved (spec §14).</summary>
public static class RefRewrite
{
    static readonly Regex WholeNumber = new(@"(?<![\w.])(\d+)(?![\w.])", RegexOptions.Compiled);

    public static string RewriteText(string text, IReadOnlyDictionary<long, long> map, out int count)
    {
        int n = 0;
        var r = WholeNumber.Replace(text, m =>
        {
            if (long.TryParse(m.Value, out var v) && map.TryGetValue(v, out var nv)) { n++; return nv.ToString(); }
            return m.Value;
        });
        count = n;
        return r;
    }

    public static int RewriteEventBytes(byte[] bytes, List<TaeRefTemplate.RefDef> refs, RemapTable t)
    {
        int n = 0;
        foreach (var r in refs)
        {
            if (r.Offset + r.Size > bytes.Length) continue;
            long v = r.Size switch
            {
                4 => BitConverter.ToInt32(bytes, r.Offset),
                2 => BitConverter.ToInt16(bytes, r.Offset),
                _ => -1,
            };
            if (!t.TryMap(r.Ref, v, out var nv)) continue;
            if (r.Size == 4) BitConverter.GetBytes((int)nv).CopyTo(bytes, r.Offset);
            else if (r.Size == 2 && nv <= short.MaxValue) BitConverter.GetBytes((short)nv).CopyTo(bytes, r.Offset);
            else continue;
            n++;
        }
        return n;
    }

    public static int RewriteTae(TAE tae, TaeRefTemplate tmpl, RemapTable t)
    {
        int n = 0;
        foreach (var anim in tae.Animations)
            foreach (var ev in anim.Events)
            {
                if (!tmpl.Refs.TryGetValue(ev.Type, out var refs)) continue;
                var bytes = ev.GetParameterBytes(tae.BigEndian);
                int k = RewriteEventBytes(bytes, refs, t);
                if (k > 0) { ev.SetParameterBytes(tae.BigEndian, bytes, true); n += k; }
            }
        return n;
    }

    static string ParamForArg(string argName) => argName switch
    {
        "SpEffect ID" => "SpEffectParam",
        "NPC Param ID" => "NpcParam",
        "NPC Think Param ID" => "NpcThinkParam",
        "Character Param ID" => "CharaInitParam",
        _ => null,
    };

    static bool IsInit(EMEVD.Instruction i) => i.Bank == 2000 && (i.ID == 0 || i.ID == 6);

    /// <summary>For every event: which parameter source offsets carry IDs of which param (direct or forwarded to other events).</summary>
    public static Dictionary<long, Dictionary<int, string>> EventSignatures(IEnumerable<EMEVD> files, Emedf d)
    {
        var sigs = new Dictionary<long, Dictionary<int, string>>();
        var events = files.SelectMany(f => f.Events).ToList();
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            foreach (var ev in events)
                foreach (var p in ev.Parameters)
                {
                    if (p.InstructionIndex < 0 || p.InstructionIndex >= ev.Instructions.Count) continue;
                    var ins = ev.Instructions[(int)p.InstructionIndex];
                    string param = null;
                    if (IsInit(ins))
                    {
                        if (ins.ArgData.Length < 8) continue;
                        long callee = BitConverter.ToUInt32(ins.ArgData, 4);
                        if (sigs.TryGetValue(callee, out var cs)) cs.TryGetValue((int)p.TargetStartByte - 8, out param);
                    }
                    else if (d.Instrs.TryGetValue((ins.Bank, ins.ID), out var def))
                        param = ParamForArg(def.Args.FirstOrDefault(a => a.Offset == p.TargetStartByte)?.Name);
                    if (param == null) continue;
                    if (!sigs.TryGetValue(ev.ID, out var s)) sigs[ev.ID] = s = new Dictionary<int, string>();
                    if (s.TryAdd((int)p.SourceStartByte, param)) changed = true;
                }
            if (!changed) break;
        }
        return sigs;
    }

    public static int RewriteEmevd(EMEVD e, Emedf d, RemapTable t, Dictionary<long, Dictionary<int, string>> sigs)
    {
        int n = 0;
        foreach (var ev in e.Events)
            foreach (var ins in ev.Instructions)
            {
                var data = ins.ArgData;
                if (IsInit(ins))
                {
                    if (data.Length < 8) continue;
                    long callee = BitConverter.ToUInt32(data, 4);
                    if (!sigs.TryGetValue(callee, out var cs)) continue;
                    foreach (var (src, param) in cs)
                    {
                        int at = 8 + src;
                        if (at + 4 > data.Length) continue;
                        if (t.TryMap(param, BitConverter.ToInt32(data, at), out var nv)) { BitConverter.GetBytes((int)nv).CopyTo(data, at); n++; }
                    }
                    continue;
                }
                if (!d.Instrs.TryGetValue((ins.Bank, ins.ID), out var def)) continue;
                foreach (var a in def.Args)
                {
                    var param = ParamForArg(a.Name);
                    if (param == null || a.Size != 4 || a.Offset + 4 > data.Length) continue;
                    if (t.TryMap(param, BitConverter.ToInt32(data, a.Offset), out var nv)) { BitConverter.GetBytes((int)nv).CopyTo(data, a.Offset); n++; }
                }
            }
        return n;
    }

    public static int RewriteMsb(MSB_NR m, RemapTable t)
    {
        int n = 0;
        foreach (MSB_NR.Part.EnemyBase en in m.Parts.Enemies.Cast<MSB_NR.Part.EnemyBase>().Concat(m.Parts.DummyEnemies))
        {
            if (t.TryMap("NpcParam", en.NpcParamId, out var a)) { en.NpcParamId = (int)a; n++; }
            if (t.TryMap("NpcThinkParam", en.NpcThinkParamId, out var b)) { en.NpcThinkParamId = (int)b; n++; }
            if (t.TryMap("CharaInitParam", en.CharaInitParamId, out var c)) { en.CharaInitParamId = (int)c; n++; }
        }
        return n;
    }
}
