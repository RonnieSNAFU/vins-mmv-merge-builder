using System.Text;
using Microsoft.Extensions.Logging;

namespace NRMerge;

/// <summary>Library console logging: Andre and SoulsFormats default to Debug-level console loggers ("dbug: ..." lines while
/// opening archives); the builder keeps only warnings and errors from them.</summary>
public static class LibraryLogging
{
    public static void Quiet()
    {
        ILoggerFactory F() => LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning).AddConsole());
        Andre.Core.AndreLogging.LoggerFactory = F();
        SoulsFormats.Util.Logging.LoggerFactory = F();
    }
}

/// <summary><c>selftest</c>: exercises every native and data dependency of a release without game files, so a published
/// single-file exe can be checked on a clean PC (Lua 5.0 compiler = lua502.dll, zstd = libzstd.dll, Havok type registry,
/// SharpCompress, release data folder).</summary>
public static class SelfTest
{
    public static int Run(TextWriter w)
    {
        int fails = 0;
        void Step(string name, Func<string> f)
        {
            try { w.WriteLine($"ok    {name}: {f()}"); }
            catch (Exception e) { fails++; w.WriteLine($"FAIL  {name}: {e.GetType().Name}: {e.Message}"); }
        }
        Step("runtime", () => $".NET {Environment.Version}, app folder {AppContext.BaseDirectory}");
        Step("lua502.dll (Lua 5.0 compiler)", () =>
        {
            var bc = LuaNorm.Compile(Encoding.UTF8.GetBytes("function f(a) return a + 1 end\n"));
            if (bc.Length == 0 || bc[0] != 0x1B) throw new InvalidOperationException("no bytecode");
            var err = LuaNorm.Check("function (");
            if (err == null) throw new InvalidOperationException("syntax error not reported");
            return $"compiled {bc.Length} bytes, normalized {LuaNorm.Norm(bc).Length} chars";
        });
        Step("libzstd.dll (zstd)", () =>
        {
            var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("nightreign ", 200)));
            using var c = new ZstdNet.Compressor();
            var z = c.Wrap(data);
            using var d = new ZstdNet.Decompressor();
            if (!d.Unwrap(z).AsSpan().SequenceEqual(data)) throw new InvalidOperationException("round trip differs");
            return $"{data.Length} -> {z.Length} bytes";
        });
        Step("Havok type registry", () => $"{BehGraph.NrRegistry.GetType().Name} loaded from {Path.Combine(AppContext.BaseDirectory, "Res")}");
        Step("SharpCompress", () =>
        {
            var ms = new MemoryStream();
            using (var za = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            using (var s = za.CreateEntry("a.txt").Open()) s.Write(Encoding.ASCII.GetBytes("hello"));
            ms.Position = 0;
            using var a = SharpCompress.Archives.ArchiveFactory.OpenArchive(ms);
            return $"{a.Entries.Count()} entry read";
        });
        Step("data folder", () =>
        {
            var d = Builder.ResolveDataDir(null);
            if (d == null) return "none next to the exe (dev checkout layout)";
            var missing = new[] { "mod-manifest.json", "vanilla-manifest.tsv", "fetch.json", @"smithbox\TAE\TAE.Template.NR.xml",
                @"smithbox\PARAM\NR\Param Type Info.json", @"smithbox\PARAM\ER\Param Type Info.json", @"andre\EldenRingNightreignDictionary.txt",
                @"andre\EldenRingDictionary.txt", @"merge\ai\473000_battle.lua.rules.json", @"templates\README.txt" }
                .Where(f => !File.Exists(Path.Combine(d, f))).ToList();
            if (missing.Count > 0) throw new FileNotFoundException("missing in " + d + ": " + string.Join(", ", missing));
            return d;
        });
        w.WriteLine(fails == 0 ? "selftest: OK" : $"selftest: {fails} FAILED");
        return fails == 0 ? 0 : 1;
    }
}
