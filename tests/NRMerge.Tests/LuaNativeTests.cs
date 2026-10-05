using Xunit;

namespace NRMerge.Tests;

public class LuaNativeTests
{
    static string TempDir()
    {
        return Repo.Scratch("native");
    }

    [Fact]
    public void FindLua502_PrefersLuaNativeSubfolder()
    {
        var d = TempDir();
        Directory.CreateDirectory(Path.Combine(d, "LuaNative"));
        File.WriteAllText(Path.Combine(d, "LuaNative", "lua502.dll"), "");
        File.WriteAllText(Path.Combine(d, "lua502.dll"), "");
        Assert.Equal(Path.Combine(d, "LuaNative", "lua502.dll"), LuaNative.FindLua502(d));
    }

    [Fact]
    public void FindLua502_FallsBackToBaseFolder()
    {
        var d = TempDir();
        File.WriteAllText(Path.Combine(d, "lua502.dll"), "");
        Assert.Equal(Path.Combine(d, "lua502.dll"), LuaNative.FindLua502(d));
    }

    [Fact]
    public void FindLua502_MissingReturnsNull() => Assert.Null(LuaNative.FindLua502(TempDir()));
}
