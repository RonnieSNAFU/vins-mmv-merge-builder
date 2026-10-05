using System.Security.Cryptography;
using System.Text.Json;

namespace NRMerge;

/// <summary>A pinned upstream file: where to download it, its SHA-256 after normalization, and its name in the cache.</summary>
public sealed record FetchEntry(string Url, string Sha256, string Normalize, string File);

/// <summary>
/// Third-party inputs we may not ship (Smithbox's NR 1.03.4 regulation, El-Fonz0's c0000.hks base, DarkScript3's EMEDF) are
/// downloaded at build time from a pinned commit and checked against a SHA-256 (data\fetch.json). Files live in
/// <see cref="BuildConfig.CacheDir"/>; a cached file with the right hash is used without network, so a folder holding the files
/// can be passed with --cache-dir to build offline.
/// </summary>
public static class Fetch
{
    public static string CatalogPath(BuildConfig cfg) =>
        Path.Combine(string.IsNullOrEmpty(cfg.DataDir) ? Path.Combine(BuildConfig.DevRepo, "data") : cfg.DataDir, "fetch.json");

    public static Dictionary<string, FetchEntry> Catalog(BuildConfig cfg)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(CatalogPath(cfg)));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => new FetchEntry(
            p.Value.GetProperty("url").GetString(), p.Value.GetProperty("sha256").GetString().ToLowerInvariant(),
            p.Value.TryGetProperty("normalize", out var n) ? n.GetString() : "none", p.Value.GetProperty("file").GetString()));
    }

    /// <summary>Local path of the verified file <paramref name="id"/>, downloading it into the cache when needed.</summary>
    public static string Get(string id, BuildConfig cfg) => Get(id, cfg, null);

    /// <param name="handler">HTTP handler (null = the default network stack).</param>
    public static string Get(string id, BuildConfig cfg, HttpMessageHandler handler)
    {
        if (!Catalog(cfg).TryGetValue(id, out var e)) throw new ArgumentException($"unknown pinned file '{id}'", nameof(id));
        var dst = Path.Combine(cfg.CacheDir, e.File);
        if (System.IO.File.Exists(dst) && Sha(System.IO.File.ReadAllBytes(dst)) == e.Sha256) return dst;

        byte[] data = null;
        string sha = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                data = Normalized(Download(e.Url, handler), e.Normalize);
                sha = Sha(data);
                if (sha == e.Sha256) break;
                if (attempt >= Retries)
                    throw new BuildException($"Downloaded {e.File} but it is not the pinned version (SHA-256 {sha}).\n  URL: {e.Url}\n  expected SHA-256: {e.Sha256}\n" + Offline(e, cfg));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (attempt >= Retries)
                    throw new BuildException($"Could not download {e.File} ({ex.Message}). Is this PC offline?\n  URL: {e.Url}\n  expected SHA-256: {e.Sha256}\n" + Offline(e, cfg), ex);
            }
            var wait = RetryDelay * (1 << attempt);
            Console.Error.WriteLine($"  download of {e.File} failed, retrying in {wait.TotalSeconds:0}s ({attempt + 1}/{Retries})");
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
        Directory.CreateDirectory(cfg.CacheDir);
        var tmp = dst + ".part";
        System.IO.File.WriteAllBytes(tmp, data);
        System.IO.File.Move(tmp, dst, true);
        return dst;
    }

    /// <summary>Extra attempts after a failed or corrupt download (3 retries, delays <see cref="RetryDelay"/> x1, x2, x4).</summary>
    public static int Retries { get; set; } = 3;
    public static TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    static byte[] Download(string url, HttpMessageHandler handler)
    {
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NRMerge-builder");
        using var resp = client.GetAsync(url).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    static string Offline(FetchEntry e, BuildConfig cfg) =>
        $"  To build offline, download that file yourself, save it as \"{e.File}\" in a folder and run the builder with --cache-dir \"<that folder>\""
        + $" (current cache folder: {cfg.CacheDir}).";

    static byte[] Normalized(byte[] data, string mode) => mode switch
    {
        "none" or null => data,
        "crlf-to-lf" => CrlfToLf(data),
        _ => throw new InvalidDataException($"unknown normalization '{mode}'"),
    };

    static byte[] CrlfToLf(byte[] d)
    {
        var o = new List<byte>(d.Length);
        for (int i = 0; i < d.Length; i++)
            if (!(d[i] == '\r' && i + 1 < d.Length && d[i + 1] == '\n')) o.Add(d[i]);
        return o.ToArray();
    }

    static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
}
