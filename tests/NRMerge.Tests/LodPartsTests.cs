using Xunit;

namespace NRMerge.Tests;

/// <summary>Other players are drawn with the low-detail (_l) parts; EV ships none and its skins installer copies each part to
/// its _l name. The merge does the same for every merged part, keeping MMV's real low-detail model only next to MMV's part.</summary>
[Collection("Paths")]
public class LodPartsTests : IDisposable
{
    readonly string tmp = Path.Combine(Path.GetTempPath(), "nrm-lod-" + Guid.NewGuid().ToString("N")[..8]);

    void Put(string root, string rel, string text)
    {
        var f = Paths.In(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f));
        File.WriteAllText(f, text);
    }
    string Read(string root, string rel) => File.ReadAllText(Paths.In(root, rel));

    public LodPartsTests() => Paths.Use(new BuildConfig
    {
        WorkDir = Path.Combine(tmp, "w"), DataDir = Path.Combine(tmp, "data"), NrGame = Path.Combine(tmp, "nr"),
        EvMod = Path.Combine(tmp, "ev"), MmvMod = Path.Combine(tmp, "mmv"),
    });

    public void Dispose()
    {
        Paths.Use(BuildConfig.Dev());
        try { Directory.Delete(tmp, true); } catch { }
    }

    [Fact]
    public void EveryMergedPartGetsALowDetailPartOtherPlayersCanSee()
    {
        Put(Paths.MMV, "parts/wp_a_0001.partsbnd.dcx", "mmv weapon");
        Put(Paths.MMV, "parts/wp_a_0001_l.partsbnd.dcx", "mmv weapon low");
        Put(Paths.MMV, "parts/wp_a_0002.partsbnd.dcx", "mmv weapon 2");
        Put(Paths.MMV, "parts/wp_a_0002_l.partsbnd.dcx", "mmv weapon 2 low");
        Put(Paths.OutMod, "parts/bd_m_1000.partsbnd.dcx", "ev skin");                 // EV part: no _l anywhere
        Put(Paths.OutMod, "parts/wp_a_0001.partsbnd.dcx", "mmv weapon");              // MMV's part: keep MMV's _l
        Put(Paths.OutMod, "parts/wp_a_0001_l.partsbnd.dcx", "mmv weapon low");
        Put(Paths.OutMod, "parts/wp_a_0002.partsbnd.dcx", "ev weapon 2");             // EV won: MMV's _l would show MMV's model
        Put(Paths.OutMod, "parts/wp_a_0002_l.partsbnd.dcx", "mmv weapon 2 low");

        Assert.Equal(0, LodParts.Run());

        Assert.Equal("ev skin", Read(Paths.OutMod, "parts/bd_m_1000_l.partsbnd.dcx"));
        Assert.Equal("mmv weapon low", Read(Paths.OutMod, "parts/wp_a_0001_l.partsbnd.dcx"));
        Assert.Equal("ev weapon 2", Read(Paths.OutMod, "parts/wp_a_0002_l.partsbnd.dcx"));
        Assert.Equal(0, LodParts.Run()); // idempotent
        Assert.Equal("ev skin", Read(Paths.OutMod, "parts/bd_m_1000_l.partsbnd.dcx"));
    }

    [Theory]
    [InlineData("parts/bd_m_1000_l.partsbnd.dcx", "parts/bd_m_1000.partsbnd.dcx")]
    [InlineData("parts/wp_a_0001.partsbnd.dcx", null)]
    [InlineData("chr/c1000_l.partsbnd.dcx", null)]
    public void LowDetailNames(string rel, string hi) => Assert.Equal(hi, LodParts.HighDetailOf(rel));

    [Fact]
    public void SkinsInstallerCopiesInTheEvFolderAreRecognised()
    {
        Put(Paths.EV, "parts/bd_m_1000.partsbnd.dcx", "ev skin");
        Put(Paths.EV, "parts/bd_m_1000_l.partsbnd.dcx", "ev skin");
        Put(Paths.EV, "parts/bd_m_2000.partsbnd.dcx", "ev skin 2");
        Put(Paths.EV, "parts/bd_m_2000_l.partsbnd.dcx", "a real low-detail model");
        Assert.True(LodParts.IsSkinsCopy(Paths.EV, "parts/bd_m_1000_l.partsbnd.dcx"));
        Assert.False(LodParts.IsSkinsCopy(Paths.EV, "parts/bd_m_2000_l.partsbnd.dcx"));
        Assert.False(LodParts.IsSkinsCopy(Paths.EV, "parts/bd_m_1000.partsbnd.dcx"));
    }
}
