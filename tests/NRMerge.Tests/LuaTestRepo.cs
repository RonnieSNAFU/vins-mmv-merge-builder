namespace NRMerge.Tests;

/// <summary>Repo-relative paths for dev-only Lua tests (they return early when the files are absent).</summary>
static class Repo
{
    public static readonly string Root = Find();
    static string Find()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "DSLuaDecompiler"))) d = d.Parent;
        return d?.FullName ?? BuildConfig.DevRepo;
    }
    public static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());
    public static readonly string Installed = BuildConfig.Dev().OutputDir;
    public static string LuaNormExe => P("tools", "luanorm", "LuaNorm.exe");
    public static string Scratch(string name)
    {
        var d = Path.Combine(Path.GetTempPath(), "nrmerge-tests", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}
