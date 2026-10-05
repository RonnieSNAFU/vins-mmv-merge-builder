using System.Text.Json;

namespace NRMerge;

/// <summary>
/// Mod Engine 3 (garyttierney/me3) is not shipped: it is found on the PC (registry Install_Dir, PATH, default install folder),
/// and when missing the builder offers to download and run the latest official installer from GitHub.
/// </summary>
public static class Me3
{
    public const string RegistryKey = @"Software\garyttierney\me3", RegistryValue = "Install_Dir";
    public const string LatestReleaseApi = "https://api.github.com/repos/garyttierney/me3/releases/latest";
    public const string InstallerName = "me3_installer.exe";

    /// <summary>me3.exe from, in order: <c>&lt;Install_Dir&gt;\bin</c>, each PATH folder, <c>%LOCALAPPDATA%\Programs\garyttierney\me3\bin</c>; null when absent.</summary>
    public static string Find(string registryInstallDir, string pathEnv, string localAppData, Func<string, bool> exists = null)
    {
        exists ??= File.Exists;
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(registryInstallDir))
        {
            candidates.Add(Path.Combine(registryInstallDir, "bin", "me3.exe"));
            candidates.Add(Path.Combine(registryInstallDir, "me3.exe"));
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(dir.Trim('"'), "me3.exe"));
        if (!string.IsNullOrWhiteSpace(localAppData))
            candidates.Add(Path.Combine(localAppData, "Programs", "garyttierney", "me3", "bin", "me3.exe"));
        return candidates.FirstOrDefault(c => { try { return exists(c); } catch { return false; } });
    }

    /// <summary>HKCU\Software\garyttierney\me3\Install_Dir, or null.</summary>
    public static string RegistryInstallDir()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey);
            return k?.GetValue(RegistryValue) as string;
        }
        catch { return null; }
    }

    /// <summary>Finds me3 on this PC.</summary>
    public static string Find() => Find(RegistryInstallDir(), Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The installer's download URL in a GitHub "latest release" JSON, or null.</summary>
    public static string InstallerUrl(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
            if (string.Equals(a.GetProperty("name").GetString(), InstallerName, StringComparison.OrdinalIgnoreCase))
                return a.GetProperty("browser_download_url").GetString();
        return null;
    }

    /// <summary>Downloads the latest official me3_installer.exe into <paramref name="dir"/> (3 retries) and returns its path.</summary>
    public static string DownloadInstaller(string dir, HttpMessageHandler handler = null)
    {
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NRMerge-builder");
        string Text(string url) => Retry(() => client.GetStringAsync(url).GetAwaiter().GetResult(), url);
        var url = InstallerUrl(Text(LatestReleaseApi))
            ?? throw new BuildException($"The latest Mod Engine 3 release has no {InstallerName}. Install ME3 yourself from https://github.com/garyttierney/me3/releases and run the builder again.");
        var bytes = Retry(() => client.GetByteArrayAsync(url).GetAwaiter().GetResult(), url);
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, InstallerName);
        File.WriteAllBytes(dst, bytes);
        return dst;
    }

    static T Retry<T>(Func<T> f, string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return f(); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                if (attempt >= Fetch.Retries)
                    throw new BuildException($"Could not download Mod Engine 3 ({e.Message}). Is this PC offline?\n  URL: {url}\n  Install ME3 yourself from https://github.com/garyttierney/me3/releases (the build itself does not need it).", e);
                var wait = Fetch.RetryDelay * (1 << attempt);
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            }
        }
    }

    /// <summary>Arguments that launch Nightreign with a profile.</summary>
    public static string LaunchArguments(string profilePath) => $"launch --game nightreign --profile \"{profilePath}\"";
}
