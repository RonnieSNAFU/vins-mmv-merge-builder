using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;

namespace NRMerge.Tests;

public class LuaNormTests
{
    static byte[] B(string s) => new UTF8Encoding(false).GetBytes(s);

    [Fact]
    public void Norm_TinyPlaintext_IsStableAcrossCompileDecompile()
    {
        var once = LuaNorm.Norm(B("function f(a, b)\n  local c = a + b\n  return c * 2\nend\nx = f(1, 2)\n"));
        Assert.Contains("function f(", once);
        Assert.Equal(once, LuaNorm.Norm(B(once)));
    }

    [Fact]
    public void Norm_StripsLocalNames_SoRenamedSourcesNormalizeEqual()
    {
        Assert.Equal(LuaNorm.Norm(B("function f(a) local foo = a return foo end\n")),
                     LuaNorm.Norm(B("function f(q) local bar = q return bar end\n")));
    }

    [Fact]
    public void Norm_AcceptsBytecodeAndPlaintextEquivalently()
    {
        var src = "function g(t) return t[1] end\n";
        var bc = LuaNorm.Compile(B(src));
        Assert.Equal(0x1B, bc[0]);
        Assert.Equal(LuaNorm.Norm(B(src)), LuaNorm.Norm(bc));
    }

    [Fact]
    public void Norm_UsesInvariantCulture_RegardlessOfThreadCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var s = LuaNorm.Norm(B("x = 1.5\n"));
            Assert.Contains("1.5", s);
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void Norm_ShiftJisPlaintext_MatchesExe()
    {
        var sjis = CodePagesEncodingProvider.Instance.GetEncoding(932);
        if (!File.Exists(Repo.LuaNormExe)) return;   // dev-only: compares against tools\luanorm\LuaNorm.exe
        var input = Path.Combine(Scratch(), "sjis.lua");
        File.WriteAllBytes(input, sjis.GetBytes("x = \"テスト\"\n"));
        Assert.Equal(RunExeNorm(input), new UTF8Encoding(false).GetBytes(LuaNorm.Norm(File.ReadAllBytes(input))));
    }

    [Fact]
    public void Check_BrokenSource_MessageMatchesExe()
    {
        if (!File.Exists(Repo.LuaNormExe)) return;   // dev-only
        var input = Path.Combine(Scratch(), "broken.lua");
        File.WriteAllText(input, "function f()\n  if x then\nend\n");
        var psi = new ProcessStartInfo(Repo.LuaNormExe) { UseShellExecute = false, RedirectStandardOutput = true };
        psi.ArgumentList.Add("check"); psi.ArgumentList.Add(input);
        using var p = Process.Start(psi);
        var stdout = p.StandardOutput.ReadToEnd().TrimEnd(); p.WaitForExit();
        Assert.Equal(1, p.ExitCode);
        Assert.Equal($"ERROR {input}: {LuaNorm.Check(File.ReadAllText(input))}", stdout);
    }

    static string Scratch() => Repo.Scratch("exe-norm");

    static byte[] RunExeNorm(string input)
    {
        var outFile = Path.Combine(Scratch(), Guid.NewGuid().ToString("N") + ".norm.lua");
        var psi = new ProcessStartInfo(Repo.LuaNormExe) { UseShellExecute = false, RedirectStandardOutput = true };
        psi.ArgumentList.Add("norm"); psi.ArgumentList.Add(input); psi.ArgumentList.Add(outFile);
        using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
        var bytes = File.ReadAllBytes(outFile);
        File.Delete(outFile);
        return bytes;
    }

    [Fact]
    public void Check_ValidSource_ReturnsNull() => Assert.Null(LuaNorm.Check("function f() return 1 end\n"));

    [Fact]
    public void Check_BrokenSource_ReturnsCompilerMessage()
    {
        var err = LuaNorm.Check("function f( return 1 end\n");
        Assert.NotNull(err);
        Assert.Contains(":1:", err);
    }

    public static IEnumerable<object[]> AiInputs()
    {
        if (!Directory.Exists(Repo.P("work", "ai"))) { yield return new object[] { null, null }; yield break; }   // dev-only: no work ai folder
        foreach (var dir in Directory.GetDirectories(Repo.P("work", "ai")).OrderBy(x => x))
            foreach (var side in new[] { "base", "ev", "mmv" })
                if (File.Exists(Path.Combine(dir, side + ".lua")))
                    yield return new object[] { Path.GetFileName(dir), side };
    }

    [Theory, MemberData(nameof(AiInputs))]
    public void Norm_MatchesCommittedNormFiles(string name, string side)
    {
        if (name == null) return;
        var dir = Repo.P("work", "ai", name);
        var expected = File.ReadAllText(Path.Combine(dir, side + ".norm.lua"), Encoding.UTF8);
        Assert.Equal(expected, LuaNorm.Norm(File.ReadAllBytes(Path.Combine(dir, side + ".lua"))));
    }

    [Theory, MemberData(nameof(AiInputs))]
    public void Norm_MatchesLuaNormExeByteForByte(string name, string side)
    {
        if (name == null || !File.Exists(Repo.LuaNormExe)) return;
        var input = Repo.P("work", "ai", name, side + ".lua");
        Assert.Equal(RunExeNorm(input), new UTF8Encoding(false).GetBytes(LuaNorm.Norm(File.ReadAllBytes(input))));
    }

    [Fact]
    public void Check_MergedAiFiles_AllCompile()
    {
        if (!Directory.Exists(Repo.P("work", "ai"))) return;
        foreach (var f in Directory.GetFiles(Repo.P("work", "ai"), "merged.lua", SearchOption.AllDirectories))
            Assert.True(LuaNorm.Check(LuaNorm.DecodeText(File.ReadAllBytes(f))) == null, f);
    }

    [Fact]
    public void Norm_IsDeterministicUnderConcurrency()
    {
        var inputs = AiInputs().Where(a => a[0] != null).Select(a => Repo.P("work", "ai", (string)a[0], (string)a[1] + ".lua")).ToArray();
        var expected = inputs.ToDictionary(f => f, f => LuaNorm.Norm(File.ReadAllBytes(f)));
        var work = Enumerable.Range(0, 6).SelectMany(_ => inputs).ToArray();
        var bad = 0;
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 16 }, f =>
        {
            if (LuaNorm.Norm(File.ReadAllBytes(f)) != expected[f]) Interlocked.Increment(ref bad);
        });
        Assert.Equal(0, bad);
    }

    [Fact]
    public void Check_IsCorrectUnderConcurrency_WithErrorsAndSuccessesMixed()
    {
        var bad = 0;
        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var r = (i % 2 == 0) ? LuaNorm.Check($"x{i} = {i}\n") : LuaNorm.Check($"function f{i}( end\n");
            if ((i % 2 == 0) != (r == null)) Interlocked.Increment(ref bad);
        });
        Assert.Equal(0, bad);
    }
}
