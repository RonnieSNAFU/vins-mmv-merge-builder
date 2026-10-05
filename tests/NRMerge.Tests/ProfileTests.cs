using NRMerge;
using Xunit;

public class ProfileTests
{
    [Fact]
    public void RedirectorIniGetsTheNewShardAndAltSave()
    {
        var mmv = "[cl_launcher]\r\nLAUNCHER_PAYLOAD=dllMods/cl_server_redirector.dll\r\n[cl_server_redirector]\r\nCL_SERVER_URL=https://nightreign.fs-emu.net\r\nCL_CUSTOM_SHARD_NAME=MMV\r\nCL_USE_ALT_SAVE=false\r\n";
        var r = Profile.RedirectorIni(mmv);
        Assert.Contains("CL_CUSTOM_SHARD_NAME=VINSMMV", r);
        Assert.Contains("CL_USE_ALT_SAVE=true", r);
        Assert.DoesNotContain("=MMV\r", r);
    }

    [Fact]
    public void ProfileHasNoSavefileAndLoadsOneRedirector()
    {
        Assert.DoesNotContain("savefile", Profile.Me3);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Profile.Me3, "cl_server_redirector.dll"));
    }
}
