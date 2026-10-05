using Xunit;

namespace NRMerge.Tests;

public class AiRulesTests
{
    const string Ev = "function A()\n  local a = 1\n  local b = 2\n  return a\nend";
    const string Mmv = "function A()\n  local a = 1\n  local b = 2\n  local c = 3\n  local d = 4\n  return c\nend";

    [Fact]
    public void Make_ChoosesSideWithFewerInsertedLines_AndRoundTrips()
    {
        // Resolved = MMV plus one EV-ish edit: from MMV 1 inserted line, from EV 3.
        const string resolved = "function A()\n  local a = 1\n  local b = 2\n  local c = 3\n  local d = 4\n  return a + c\nend";
        var rule = AiRules.Make(Ev, Mmv, resolved);
        Assert.Equal("mmv", rule.Take);
        Assert.Equal(AiRules.Sha256(Mmv), rule.SideSha256);
        Assert.Equal(1, rule.InsertedLines);
        Assert.Equal(resolved, AiRules.ApplyEdits(Mmv, rule.Edits));
    }

    [Fact]
    public void Make_TiePrefersEv()
    {
        var rule = AiRules.Make("x\ny", "x\nz", "x\nw");
        Assert.Equal("ev", rule.Take);
        Assert.Single(rule.Edits);
        Assert.Equal(1, rule.Edits[0].At);
        Assert.Equal(1, rule.Edits[0].Delete);
        Assert.Equal(new[] { "w" }, rule.Edits[0].Insert);
    }

    [Fact]
    public void Make_IdenticalSide_HasNoEdits()
    {
        var rule = AiRules.Make(Ev, Mmv, Ev);
        Assert.Equal("ev", rule.Take);
        Assert.Empty(rule.Edits);
    }

    [Fact]
    public void Diff_IsMinimal()
    {
        var a = "a\nb\nc\na\nb\nb\na";
        var b = "c\nb\na\nb\na\nc";
        var edits = AiRules.Diff(a, b);
        Assert.Equal(5, edits.Sum(e => e.Delete + e.Insert.Count)); // Myers paper example: D = 5
        Assert.Equal(b, AiRules.ApplyEdits(a, edits));
    }

    [Fact]
    public void TryResolve_HashMismatch_ReportsError()
    {
        var rules = new AiRules();
        rules.Rules["A"] = AiRules.Make(Ev, Mmv, Ev + "\n-- x");
        Assert.True(rules.TryResolve("A", Ev, Mmv, out var text, out var error));
        Assert.Null(error);
        Assert.Equal(Ev + "\n-- x", text);
        Assert.True(rules.TryResolve("A", Ev + " ", Mmv, out text, out error));
        Assert.Null(text);
        Assert.Equal("hand resolution for A no longer applies (input changed)", error);
        Assert.False(rules.TryResolve("B", Ev, Mmv, out _, out _));
    }

    [Fact]
    public void Json_RoundTrip_PreservesOrderAndContent()
    {
        var rules = new AiRules();
        rules.Rules["Goal.Interrupt"] = AiRules.Make(Ev, Mmv, Mmv + "\n\"quoted\" \\ \t tab");
        rules.Rules["Goal.Activate"] = AiRules.Make(Ev, Mmv, Ev);
        var json = rules.ToJson();
        var back = AiRules.FromJson(json);
        Assert.Equal(new[] { "Goal.Interrupt", "Goal.Activate" }, back.Rules.Keys);
        Assert.Equal(json, back.ToJson());
        Assert.True(back.TryResolve("Goal.Interrupt", Ev, Mmv, out var t, out _));
        Assert.Equal(Mmv + "\n\"quoted\" \\ \t tab", t);
        Assert.Contains("\"at\"", json);
        Assert.Contains("\"sideSha256\"", json);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty() => Assert.Empty(AiRules.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Rules);

    [Fact]
    public void Generate_FromInputsAndResolvedTexts()
    {
        string L(params string[] l) => string.Join("\n", l) + "\n";
        var b = L("function A()", "  local f1_local0 = 1", "  return 1", "end");
        var e = L("function A()", "  local f2_local0 = 1", "  return 2", "end");
        var m = L("function A()", "  local f3_local0 = 1", "  return 3", "end");
        const string resolved = "function A()\n  local f2_local0 = 1\n  return 3 -- merged\nend";
        var rules = AiRules.Generate(b, e, m, new Dictionary<string, string> { ["A"] = resolved }, out var log);
        Assert.Single(rules.Rules);
        Assert.Single(log);
        var outText = LuaFuncMerge.Merge(b, e, m, rules, out var report, out var unresolved);
        Assert.Equal(resolved + "\n", outText);
        Assert.Equal(new[] { "RESOLVED A" }, report);
        Assert.Empty(unresolved);
    }

    [Fact]
    public void SanitizedFileName_MatchesPython() => Assert.Equal("Goal.Activate#1_x_y_.lua", AiRules.OverrideFileName("Goal.Activate#1 x[y]"));
}
