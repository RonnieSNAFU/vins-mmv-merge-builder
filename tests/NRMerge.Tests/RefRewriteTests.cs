using NRMerge;
using SoulsFormats;
using Xunit;

public class RefRewriteTests
{
    static RemapTable Table() => RemapTable.FromList(new[]
    {
        new RegMerge.Remap("SpEffectParam", 46033, 46468, "radahn", "adel"),
        new RegMerge.Remap("NpcParam", 43510050, 43510051, "knight", "godrick"),
        new RegMerge.Remap("CharaInitParam", 50970, 50971, "dragonman", "undertaker"),
    });

    [Fact]
    public void TextRewriteReplacesWholeNumbersOnly()
    {
        var src = "if ai:HasSpecialEffectId(TARGET_SELF, 46033) and x46033 == 460331 or 146033 or 46033.5 then f(46033) end";
        var r = RefRewrite.RewriteText(src, new Dictionary<long, long> { [46033] = 46468 }, out var n);
        Assert.Equal(2, n);
        Assert.Equal("if ai:HasSpecialEffectId(TARGET_SELF, 46468) and x46033 == 460331 or 146033 or 46033.5 then f(46468) end", r);
    }

    [Fact]
    public void TaeTemplateOffsetsFollowDeclaredSizes()
    {
        var xml = """
            <event_template game="SDT">
              <event id="67" name="Add SpEffect"><s32 name="SpEffect ID" ref="SpEffectParam"/></event>
              <event id="122" name="FFX"><s32 name="FFX ID"/><u8 name="a"/><u8 name="b"/><s16 name="SpEffect ID" ref="SpEffectParam"/></event>
            </event_template>
            """;
        var t = TaeRefTemplate.Parse(xml);
        Assert.Equal(0, t.Refs[67][0].Offset);
        Assert.Equal(4, t.Refs[67][0].Size);
        Assert.Equal(6, t.Refs[122][0].Offset);
        Assert.Equal(2, t.Refs[122][0].Size);
    }

    [Fact]
    public void TaeEventBytesAreRewrittenAtRefOffsets()
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(46033).CopyTo(bytes, 0);
        BitConverter.GetBytes(46033).CopyTo(bytes, 4); // not a ref position
        var refs = new List<TaeRefTemplate.RefDef> { new(0, 4, "SpEffectParam") };
        int n = RefRewrite.RewriteEventBytes(bytes, refs, Table());
        Assert.Equal(1, n);
        Assert.Equal(46468, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(46033, BitConverter.ToInt32(bytes, 4));
    }

    [Fact]
    public void EmevdDirectAndPassThroughSpEffectArgsAreRewritten()
    {
        if (!File.Exists(Paths.Emedf)) return; // dev-only: needs the EMEDF from the dev checkout or Data dir
        var emedf = Emedf.Load(Paths.Emedf);
        var common = new EMEVD(EMEVD.Game.Sekiro);
        var ce = new EMEVD.Event(90000);
        ce.Instructions.Add(new EMEVD.Instruction(2004, 8, new object[] { 0u, 0 }));
        ce.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4)); // param slot 0 -> SpEffect ID
        common.Events.Add(ce);

        var map = new EMEVD(EMEVD.Game.Sekiro);
        var e0 = new EMEVD.Event(0);
        e0.Instructions.Add(new EMEVD.Instruction(2004, 8, new object[] { 1000u, 46033 }));
        e0.Instructions.Add(new EMEVD.Instruction(2000, 6, new object[] { 0, 90000u, 46033 }));
        map.Events.Add(e0);

        var sigs = RefRewrite.EventSignatures(new[] { common, map }, emedf);
        int n = RefRewrite.RewriteEmevd(map, emedf, Table(), sigs);
        Assert.Equal(2, n);
        Assert.Equal(46468, BitConverter.ToInt32(e0.Instructions[0].ArgData, 4));
        Assert.Equal(46468, BitConverter.ToInt32(e0.Instructions[1].ArgData, 8));
    }

    [Fact]
    public void MsbEnemyNpcAndCharaInitIdsAreRewritten()
    {
        var msb = new MSB_NR();
        var en = new MSB_NR.Part.Enemy { Name = "c4351_9000", NpcParamId = 43510050, CharaInitParamId = 50970 };
        msb.Parts.Enemies.Add(en);
        int n = RefRewrite.RewriteMsb(msb, Table());
        Assert.Equal(2, n);
        Assert.Equal(43510051, en.NpcParamId);
        Assert.Equal(50971, en.CharaInitParamId);
    }
}
