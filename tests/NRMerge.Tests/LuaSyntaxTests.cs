using System.Text;
using Xunit;

namespace NRMerge.Tests;

public class LuaSyntaxTests
{
    // ---- accepts ----
    [Theory]
    [InlineData("")]
    [InlineData("﻿x = 1")]
    [InlineData("local a, b = 1, 2; a, b = b, a")]
    [InlineData("local function f(...) local t = {...} return #t, ... end")]
    [InlineData("function a.b.c:d(x, y) return self, x end")]
    [InlineData("t = { 1, 2; x = 3, [\"k\"] = 4, f = function() end, }")]
    [InlineData("print \"s\" print [[long]] f{1} a.b:c'x' a[1][2].c(3)(4)")]
    [InlineData("for i = 1, 10, 2 do if i % 2 == 0 then break end end")]
    [InlineData("for k, v in pairs(t) do repeat local z = k until z ~= nil end")]
    [InlineData("while not x do x = -y ^ 2 .. 'a' .. \"b\\n\\65\\\"\" end")]
    [InlineData("if a then elseif b and c or d then else end")]
    [InlineData("x = 0x1F + 1e10 + .5 + 3. + 1.5E-3")]
    [InlineData("--[==[ long\ncomment ]==] x = [==[ a ]] b ]==] -- tail")]
    [InlineData("do return end")]
    [InlineData("return")]
    [InlineData("x = a <= b and a >= b and a < b and a > b and a == b and a ~= b")]
    [InlineData("local s = 'line1\\\nline2'")]
    [InlineData("f(function(a, ...) return ... end)")]
    [InlineData("x = (f())")]
    [InlineData("(f or g)(1)")]
    public void Accepts(string src) => Assert.Null(LuaSyntax.Check(src));

    // ---- rejects ----
    [Theory]
    [InlineData("function f()\n  if x then\nend\n", 4)]           // unbalanced end
    [InlineData("function x( end", 1)]
    [InlineData("x = 1\n<<<<<<< ours\nx = 2\n=======\nx = 3\n>>>>>>> theirs\n", 2)]
    [InlineData("x = 1\ny = \"abc\nz = 2", 2)]                       // unterminated string
    [InlineData("x = [[abc", 1)]                                     // unterminated long string
    [InlineData("--[[ open comment", 1)]
    [InlineData("end", 1)]
    [InlineData("x = ", 1)]
    [InlineData("f() = 1", 1)]                                       // cannot assign to a call
    [InlineData("x", 1)]                                             // expression is not a statement
    [InlineData("return 1\nx = 2", 2)]                               // return must be last
    [InlineData("break", 1)]                                         // no loop to break
    [InlineData("function f() return ... end", 1)]                   // ... outside vararg function
    [InlineData("t = {1 2}", 1)]
    [InlineData("local function a.b() end", 1)]
    [InlineData("x = 1 +", 1)]
    [InlineData("x = 3 @ 4", 1)]
    [InlineData("for i = 1 do end", 1)]
    public void Rejects(string src, int line)
    {
        var err = LuaSyntax.Check(src);
        Assert.NotNull(err);
        Assert.StartsWith($"line {line}:", err);
    }

    [Fact]
    public void Lua51_AmbiguousCallAcrossNewline_IsRejected()
    {
        var err = LuaSyntax.Check("local a = f\n(g)()");
        Assert.NotNull(err);
        Assert.Contains("ambiguous", err);
    }

    // ---- real files ----
    static string Installed(string rel) => Path.Combine(Repo.Installed, rel);

    public static IEnumerable<object[]> KnownGood()
    {
        var files = new List<string>();
        var merged = Repo.P("out", "mod", "action", "script", "c0000.hks");
        files.Add(File.Exists(merged) ? merged : Installed(@"mod\action\script\c0000.hks"));
        files.Add(Installed(@"mod\action\script\c9997.hks"));
        if (Directory.Exists(Repo.P("work", "hks"))) files.AddRange(Directory.GetFiles(Repo.P("work", "hks"), "*.hks").Where(f => !f.Contains("diff3")));
        if (Directory.Exists(Repo.P("work", "ai"))) files.AddRange(Directory.GetFiles(Repo.P("work", "ai"), "merged.lua", SearchOption.AllDirectories));
        files.RemoveAll(f => !File.Exists(f));   // dev-only real files
        if (files.Count == 0) files.Add(null);
        return files.Select(f => new object[] { f });
    }

    [Theory, MemberData(nameof(KnownGood))]
    public void Accepts_KnownGoodScripts(string file)
    {
        if (file == null) return;   // no real scripts on this machine
        Assert.Null(LuaSyntax.Check(File.ReadAllText(file)));
    }

    [Fact]
    public void Rejects_Diff3WithConflictMarkers()
    {
        var diff3 = Repo.P("work", "hks", "c0000.diff3.hks");
        if (!File.Exists(diff3)) return;   // dev-only
        var err = LuaSyntax.Check(File.ReadAllText(diff3));
        Assert.NotNull(err);
        Assert.Matches(@"^line \d+: ", err);
    }

    /// <summary>Differential corpus: TestData/luaparser-verdicts.tsv = luaparser 4.2.0 verdict (BOM stripped) for every
    /// text .hks/.lua in the maintainer's corpus (<Installed> = the installed merged mod folder) (installed mod, work/hks, analysis/hks, delivered-v1, work/ai). The checker must agree on each.</summary>
    [Fact]
    public void AgreesWithLuaparserOnCorpus()
    {
        var tsv = Path.Combine(AppContext.BaseDirectory, "TestData", "luaparser-verdicts.tsv");
        var mismatches = new List<string>();
        var n = 0;
        foreach (var line in File.ReadAllLines(tsv))
        {
            var parts = line.Split('\t');
            var rel = parts[1];
            var path = rel.StartsWith("<Installed>/") ? Path.Combine(Repo.Installed, rel.Substring("<Installed>/".Length))
                : Path.IsPathRooted(rel) ? rel : Path.Combine(Repo.Root, rel);
            if (!File.Exists(path)) continue;
            n++;
            var err = LuaSyntax.Check(File.ReadAllText(path));
            if ((err == null) != (parts[0] == "OK")) mismatches.Add($"{parts[0]} {parts[1]}: {err ?? "accepted"}");
        }
        // dev-only: with the maintainer's work folders all 69 corpus files exist; elsewhere the absent ones are skipped
        Assert.True(n == 0 || !File.Exists(Path.Combine(Repo.Installed, "mod", "action", "script", "c0000.hks")) || !Directory.Exists(Repo.P("work", "ai")) || n >= 60, $"only {n} corpus files found");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }
}
