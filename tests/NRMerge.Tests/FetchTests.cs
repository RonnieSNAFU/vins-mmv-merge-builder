using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NRMerge;
using Xunit;

namespace NRMerge.Tests;

/// <summary>Serves fixed bytes per URL (or throws, like an offline PC) and records the requests.</summary>
sealed class FakeHttp : HttpMessageHandler
{
    public readonly Dictionary<string, byte[]> Files = new();
    public readonly List<string> Requests = new();
    public bool Offline;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri.AbsoluteUri;
        Requests.Add(url);
        if (Offline) throw new HttpRequestException("No such host is known.");
        return Task.FromResult(Files.TryGetValue(url, out var b)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

[Collection("Paths")]
public class FetchTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "nrm-fetch-" + Guid.NewGuid().ToString("N"));
    readonly BuildConfig cfg;
    readonly FakeHttp http = new();

    const string RegUrl = "https://example.invalid/Smithbox/1.03.4%20(10340000)/regulation.bin";
    const string HksUrl = "https://example.invalid/hks/c0000.hks";
    static readonly byte[] Reg = { 1, 2, 3, 4, 5 };
    const string HksCrlf = "a\r\nb\r\n";

    static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    public FetchTests()
    {
        Fetch.RetryDelay = TimeSpan.Zero;
        cfg = new BuildConfig
        {
            DataDir = Path.Combine(root, "Data"), CacheDir = Path.Combine(root, "cache"),
            WorkDir = Path.Combine(root, "w"), NrGame = Path.Combine(root, "nr"), ErGame = Path.Combine(root, "er"),
        };
        Directory.CreateDirectory(cfg.DataDir);
        var catalog = new Dictionary<string, object>
        {
            ["reg"] = new { url = RegUrl, sha256 = Sha(Reg), normalize = "none", file = "regulation-10340000.bin" },
            ["hks"] = new { url = HksUrl, sha256 = Sha(Encoding.UTF8.GetBytes("a\nb\n")), normalize = "crlf-to-lf", file = "c0000.hks" },
        };
        File.WriteAllText(Path.Combine(cfg.DataDir, "fetch.json"), JsonSerializer.Serialize(catalog));
        http.Files[RegUrl] = Reg;
        http.Files[HksUrl] = Encoding.UTF8.GetBytes(HksCrlf);
    }

    public void Dispose()
    {
        Paths.Use(BuildConfig.Dev());
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void DownloadsVerifiesAndCaches()
    {
        var p = Fetch.Get("reg", cfg, http);
        Assert.Equal(Path.Combine(cfg.CacheDir, "regulation-10340000.bin"), p);
        Assert.Equal(Reg, File.ReadAllBytes(p));
        Assert.Equal(new[] { RegUrl }, http.Requests);
    }

    [Fact]
    public void CacheHitNeedsNoNetwork()
    {
        Fetch.Get("reg", cfg, http);
        http.Offline = true;
        http.Requests.Clear();
        var p = Fetch.Get("reg", cfg, http);
        Assert.Empty(http.Requests);
        Assert.Equal(Reg, File.ReadAllBytes(p));
    }

    [Fact]
    public void AFilePutInTheCacheDirByHandWorksOffline()
    {
        Directory.CreateDirectory(cfg.CacheDir);
        File.WriteAllBytes(Path.Combine(cfg.CacheDir, "regulation-10340000.bin"), Reg);
        http.Offline = true;
        Assert.Equal(Reg, File.ReadAllBytes(Fetch.Get("reg", cfg, http)));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void CrlfIsNormalizedBeforeHashing()
    {
        var p = Fetch.Get("hks", cfg, http);
        Assert.Equal("a\nb\n", File.ReadAllText(p));
    }

    [Fact]
    public void HashMismatchIsRejectedAndNotCached()
    {
        http.Files[RegUrl] = new byte[] { 9, 9 };
        var ex = Assert.Throws<BuildException>(() => Fetch.Get("reg", cfg, http));
        Assert.Contains(RegUrl, ex.Message);
        Assert.Contains(Sha(Reg), ex.Message);
        Assert.Contains("--cache-dir", ex.Message);
        Assert.False(File.Exists(Path.Combine(cfg.CacheDir, "regulation-10340000.bin")));
    }

    [Fact]
    public void ACorruptCachedFileIsDownloadedAgain()
    {
        Directory.CreateDirectory(cfg.CacheDir);
        File.WriteAllBytes(Path.Combine(cfg.CacheDir, "regulation-10340000.bin"), new byte[] { 7 });
        Assert.Equal(Reg, File.ReadAllBytes(Fetch.Get("reg", cfg, http)));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void OfflineErrorNamesUrlHashAndTheCacheDirOption()
    {
        http.Offline = true;
        var ex = Assert.Throws<BuildException>(() => Fetch.Get("reg", cfg, http));
        Assert.Contains(RegUrl, ex.Message);
        Assert.Contains(Sha(Reg), ex.Message);
        Assert.Contains("--cache-dir", ex.Message);
        Assert.Contains("regulation-10340000.bin", ex.Message);
        Assert.Contains("offline", ex.Message);
        Assert.Equal(1 + Fetch.Retries, http.Requests.Count);   // 3 retries before giving up
    }

    [Fact]
    public void ATransientFailureIsRetried()
    {
        var flaky = new FlakyHttp(http, failures: 2);
        Assert.Equal(Reg, File.ReadAllBytes(Fetch.Get("reg", cfg, flaky)));
        Assert.Equal(3, flaky.Calls);
    }

    sealed class FlakyHttp(FakeHttp inner, int failures) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (++Calls <= failures) throw new HttpRequestException("connection reset");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(inner.Files[request.RequestUri.AbsoluteUri]) });
        }
    }

    [Fact]
    public void HttpErrorStatusIsABuildException()
    {
        http.Files.Remove(RegUrl);
        var ex = Assert.Throws<BuildException>(() => Fetch.Get("reg", cfg, http));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public void UnknownIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Fetch.Get("nope", cfg, http));
    }

    [Fact]
    public void ShippedCatalogPinsTheThreeUpstreamFiles()
    {
        var c = Fetch.Catalog(BuildConfig.Dev());
        Assert.Equal("776f1741ab8bf80c2229e109a826973ad2f4c3298699ff4ee0fe36fbe3f2a05e", c["nr-regulation-1.03.4"].Sha256);
        Assert.Contains("vawser/Smithbox/cbd477a8fd6d436b3e011c8548e1de8fd8876918/", c["nr-regulation-1.03.4"].Url);
        Assert.Equal("none", c["nr-regulation-1.03.4"].Normalize);
        Assert.Equal("ad599c44ba01e451265903ba52fe55b33aa9bdb19cc67fb96cfe9ded84ae03f3", c["c0000-hks-base-197b182"].Sha256);
        Assert.Contains("El-Fonz0/EldenRingNightreignHKS/197b182e391f977148a5f6925a39deaa81e7ada6/", c["c0000-hks-base-197b182"].Url);
        Assert.Equal("crlf-to-lf", c["c0000-hks-base-197b182"].Normalize);
        Assert.Equal("6323d7c5f806a4b32e88e75c065c53b513887529d41aecf3fb7d66e77cf56344", c["nr-emedf"].Sha256);
        Assert.Contains("AinTunez/DarkScript3/4b570f513a58075f47e0d47645adc054882b209f/", c["nr-emedf"].Url);
    }

    /// <summary>Release mode: the regulation base and the EMEDF come from the fetch cache, not from Smithbox/tools checkouts.</summary>
    [Fact]
    public void ReleaseModeResolvesFetchedFilesThroughTheCache()
    {
        var reg = Encoding.UTF8.GetBytes("regulation");
        var emedf = Encoding.UTF8.GetBytes("{}");
        var catalog = new Dictionary<string, object>
        {
            ["nr-regulation-1.03.4"] = new { url = RegUrl, sha256 = Sha(reg), normalize = "none", file = "reg.bin" },
            ["nr-emedf"] = new { url = HksUrl, sha256 = Sha(emedf), normalize = "none", file = "nr-common.emedf.json" },
        };
        File.WriteAllText(Path.Combine(cfg.DataDir, "fetch.json"), JsonSerializer.Serialize(catalog));
        Directory.CreateDirectory(cfg.CacheDir);
        File.WriteAllBytes(Path.Combine(cfg.CacheDir, "reg.bin"), reg);
        File.WriteAllBytes(Path.Combine(cfg.CacheDir, "nr-common.emedf.json"), emedf);
        Paths.Use(cfg);
        Assert.Equal(Path.Combine(cfg.CacheDir, "reg.bin"), Regulation.VanillaRegulationForVersion("10340000"));
        Assert.Equal(Path.Combine(cfg.CacheDir, "nr-common.emedf.json"), Paths.Emedf);
        Assert.Null(Regulation.VanillaRegulationForVersion("10350000")); // no Smithbox regulations in a release
    }
}
