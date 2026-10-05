using Xunit;

namespace NRMerge.Tests;

public class SteamLocatorTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "nrm-steam-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    const string Vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1129968520\"\n\t\t}\n\t}\n"
        + "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam\"\n\t\t\"label\"\t\t\"games\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"2622380\"\t\t\"1\"\n\t\t}\n\t}\n}\n";

    [Fact]
    public void ParsesBothLibrariesOfAVdfAndUnescapesBackslashes()
    {
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\Steam" }, SteamLocator.ParseLibraryFolders(Vdf));
    }

    [Fact]
    public void EmptyOrBrokenVdfYieldsNoLibraries()
    {
        Assert.Empty(SteamLocator.ParseLibraryFolders(""));
        Assert.Empty(SteamLocator.ParseLibraryFolders(null));
        Assert.Empty(SteamLocator.ParseLibraryFolders("\"libraryfolders\" { \"0\" { \"label\" \"x\" } }"));
    }

    string MakeLibrary(string name, string gameFolder = null, string exe = null)
    {
        var lib = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(lib, "steamapps"));
        if (gameFolder != null)
        {
            var game = Path.Combine(lib, "steamapps", "common", gameFolder, "Game");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, exe), "exe");
        }
        return lib;
    }

    static string VdfFor(params string[] libs) =>
        "\"libraryfolders\"\n{\n" + string.Concat(libs.Select((l, i) => $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{l.Replace("\\", "\\\\")}\"\n\t}}\n")) + "}\n";

    [Fact]
    public void LibrariesComeFromTheSteamRootAndItsVdf_ExistingOnly_Deduplicated()
    {
        var steam = MakeLibrary("Steam");
        var second = MakeLibrary("Lib2");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), VdfFor(steam, second, Path.Combine(root, "gone")));
        var libs = SteamLocator.Libraries(new[] { steam, steam.ToUpperInvariant() });
        Assert.Equal(new[] { steam, second }, libs);
    }

    [Fact]
    public void FindsTheGamesInWhicheverLibraryHoldsThem()
    {
        var steam = MakeLibrary("Steam", SteamLocator.EldenRingFolder, SteamLocator.EldenRingExe);
        var second = MakeLibrary("Lib2", SteamLocator.NightreignFolder, SteamLocator.NightreignExe);
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), VdfFor(steam, second));
        var libs = SteamLocator.Libraries(new[] { steam });
        Assert.Equal(Path.Combine(second, "steamapps", "common", SteamLocator.NightreignFolder, "Game"),
            SteamLocator.FindGame(libs, SteamLocator.NightreignFolder, SteamLocator.NightreignExe));
        Assert.Equal(Path.Combine(steam, "steamapps", "common", SteamLocator.EldenRingFolder, "Game"),
            SteamLocator.FindGame(libs, SteamLocator.EldenRingFolder, SteamLocator.EldenRingExe));
        Assert.Null(SteamLocator.FindGame(new[] { steam }, SteamLocator.NightreignFolder, SteamLocator.NightreignExe));
    }

    [Fact]
    public void AGameLocationMayBeTheExeTheGameFolderOrTheInstallFolder()
    {
        var lib = MakeLibrary("Steam", SteamLocator.NightreignFolder, SteamLocator.NightreignExe);
        var install = Path.Combine(lib, "steamapps", "common", SteamLocator.NightreignFolder);
        var game = Path.Combine(install, "Game");
        Assert.Equal(game, SteamLocator.NormalizeGameDir(Path.Combine(game, "nightreign.exe"), "nightreign.exe"));
        Assert.Equal(game, SteamLocator.NormalizeGameDir(game, "nightreign.exe"));
        Assert.Equal(game, SteamLocator.NormalizeGameDir(install, "nightreign.exe"));
        Assert.Equal(game, SteamLocator.NormalizeGameDir("\"" + install + "\"", "nightreign.exe"));
        Assert.Null(SteamLocator.NormalizeGameDir(lib, "nightreign.exe"));
        Assert.Null(SteamLocator.NormalizeGameDir(" ", "nightreign.exe"));
    }
}
