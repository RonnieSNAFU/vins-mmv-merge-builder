using Xunit;

namespace NRMerge.Tests;

public class LuaFuncMergeTests
{
    static string L(params string[] lines) => string.Join("\n", lines) + "\n";

    static string Merge(string b, string e, string m, out List<string> report, out List<string> unresolved, AiRules rules = null)
        => LuaFuncMerge.Merge(b, e, m, rules ?? AiRules.Empty, out report, out unresolved);

    const string A0 = "function A()\n  return 1\nend";
    const string B0 = "function B()\n  return 2\nend";

    [Fact]
    public void Unchanged_KeepsEvAndEmptyReport()
    {
        var t = L(A0, "", B0);
        Assert.Equal(A0 + "\n\n" + B0 + "\n", Merge(t, t, t, out var r, out var u));
        Assert.Empty(r); Assert.Empty(u);
        Assert.Equal("\n", LuaFuncMerge.ReportText(r));
    }

    [Fact]
    public void OneSidedChunks_TakeTheChangingSide()
    {
        const string aEv = "function A()\n  return 10\nend";
        const string bMmv = "function B()\n  return 20\nend";
        var outText = Merge(L(A0, B0), L(aEv, B0), L(A0, bMmv), out var r, out var u);
        Assert.Equal(aEv + "\n\n" + bMmv + "\n", outText);
        Assert.Equal(new[] { "MMV B" }, r);
        Assert.Empty(u);
    }

    [Fact]
    public void BothChanged_DifferentLines_LineMerged()
    {
        var b = L("function A()", "  local x = 1", "  local y = 2", "  local z = 3", "  return x", "end");
        var e = L("function A()", "  local x = 100", "  local y = 2", "  local z = 3", "  return x", "end");
        var m = L("function A()", "  local x = 1", "  local y = 2", "  local z = 3", "  return z", "end");
        Assert.Equal(L("function A()", "  local x = 100", "  local y = 2", "  local z = 3", "  return z", "end"), Merge(b, e, m, out var r, out var u));
        Assert.Equal(new[] { "LINE-MERGED A" }, r);
        Assert.Empty(u);
    }

    [Fact]
    public void BothChanged_SameLine_ConflictKeepsEvAndIsUnresolved()
    {
        var b = L("function A()", "  return 1", "end");
        var e = L("function A()", "  return 2", "end");
        var m = L("function A()", "  return 3", "end");
        Assert.Equal(e, Merge(b, e, m, out var r, out var u));
        Assert.Equal(new[] { "CONFLICT A (EV kept until resolved)" }, r);
        Assert.Equal(new[] { "A" }, u);
    }

    [Fact]
    public void BothChanged_Conflict_RuleResolves()
    {
        var b = L("function A()", "  return 1", "end");
        var e = L("function A()", "  return 2", "end");
        var m = L("function A()", "  return 3", "end");
        const string resolved = "function A()\n  return 2 + 3\nend";
        var rules = new AiRules();
        rules.Rules["A"] = AiRules.Make("function A()\n  return 2\nend", "function A()\n  return 3\nend", resolved);
        Assert.Equal(resolved + "\n", Merge(b, e, m, out var r, out var u, rules));
        Assert.Equal(new[] { "RESOLVED A" }, r);
        Assert.Empty(u);
    }

    [Fact]
    public void BothChanged_RuleTakesPrecedenceOverCleanLineMerge()
    {
        var b = L("function A()", "  local x = 1", "  local y = 2", "  local z = 3", "  return x", "end");
        var e = L("function A()", "  local x = 100", "  local y = 2", "  local z = 3", "  return x", "end");
        var m = L("function A()", "  local x = 1", "  local y = 2", "  local z = 3", "  return z", "end");
        var evSide = e.TrimEnd('\n');
        var rules = new AiRules();
        rules.Rules["A"] = AiRules.Make(evSide, m.TrimEnd('\n'), evSide);
        Assert.Equal(e, Merge(b, e, m, out var r, out _, rules));
        Assert.Equal(new[] { "RESOLVED A" }, r);
    }

    [Fact]
    public void RuleHashMismatch_IsUnresolvedWithMessage()
    {
        var b = L("function A()", "  return 1", "end");
        var e = L("function A()", "  return 2", "end");
        var m = L("function A()", "  return 3", "end");
        var rules = new AiRules();
        rules.Rules["A"] = AiRules.Make("function A()\n  return 2 -- older EV\nend", "function A()\n  return 3 -- older MMV\nend", "function A()\n  return 5\nend");
        Assert.Equal(e, Merge(b, e, m, out var r, out var u, rules));
        Assert.Equal(new[] { "A" }, u);
        Assert.Single(r);
        Assert.Contains("hand resolution for A no longer applies (input changed)", r[0]);
    }

    [Fact]
    public void RulesOnlyConsultedInBothChangedBranch()
    {
        // EV-only change: a rule for the key is ignored.
        var b = L(A0); var e = L("function A()\n  return 9\nend");
        var rules = new AiRules();
        rules.Rules["A"] = AiRules.Make(A0, A0, "function A()\n  return 42\nend");
        Assert.Equal(e, Merge(b, e, b, out var r, out _, rules));
        Assert.Empty(r);
    }

    [Fact]
    public void MmvOnlyChunks_InsertedAfterPreviousKnownKey_OrAtStart()
    {
        const string c = "function C()\n  return 3\nend";
        const string z = "function Z()\n  return 0\nend";
        var outText = Merge(L(A0, B0), L(A0, B0), L(z, A0, c, B0), out var r, out _);
        Assert.Equal(string.Join("\n\n", z, A0, c, B0) + "\n", outText);
        Assert.Equal(new[] { "MMV-added Z", "MMV-added C" }, r);
    }

    [Fact]
    public void EvDeleted_MmvUnchanged_Dropped_MmvChanged_AppendedAtEnd()
    {
        const string bMmv = "function B()\n  return 22\nend";
        const string c0 = "function C()\n  return 3\nend";
        // EV deleted B and C; MMV changed B, left C unchanged.
        var outText = Merge(L(A0, B0, c0), L(A0), L(A0, bMmv, c0), out var r, out _);
        Assert.Equal(A0 + "\n\n" + bMmv + "\n", outText);
        Assert.Equal(new[] { "MMV (EV deleted) B" }, r);
    }

    [Fact]
    public void MmvDeleted_EvUnchanged_Dropped_EvChanged_Kept()
    {
        const string bEv = "function B()\n  return 22\nend";
        var outText = Merge(L(A0, B0), L(A0, bEv), L(), out var r, out _);
        // MMV deleted both; A unchanged by EV -> dropped, B changed by EV -> kept.
        Assert.Equal(bEv + "\n", outText);
        Assert.Equal(new[] { "drop (MMV deleted) A" }, r);
    }

    [Fact]
    public void RenumberOnly_IsNotAChange()
    {
        var b = L("function A()", "  local f3_local0 = 1", "  return f3_local0", "end");
        var m = L("function A()", "  local f7_local2 = 1", "  return f7_local2", "end");
        Assert.Equal(b, Merge(b, b, m, out var r, out _));
        Assert.Empty(r);
    }

    [Fact]
    public void BothChanged_PrefixAlignedToEv_ThenLineMerged()
    {
        var b = L("function A()", "  local f1_local0 = 1", "  local f1_local1 = 2", "  local f1_local2 = 3", "  return f1_local0", "end");
        var e = L("function A()", "  local f2_local0 = 100", "  local f2_local1 = 2", "  local f2_local2 = 3", "  return f2_local0", "end");
        var m = L("function A()", "  local f5_local0 = 1", "  local f5_local1 = 2", "  local f5_local2 = 30", "  return f5_local0", "end");
        Assert.Equal(L("function A()", "  local f2_local0 = 100", "  local f2_local1 = 2", "  local f2_local2 = 30", "  return f2_local0", "end"), Merge(b, e, m, out var r, out _));
        Assert.Equal(new[] { "LINE-MERGED A" }, r);
    }

    [Fact]
    public void BothChanged_LocalRenumberingWithinFunction_IsConflict()
    {
        var b = L("function A()", "  local f1_local0 = 1", "  local f1_local1 = 2", "  local f1_local2 = 3", "  return 0", "end");
        var e = L("function A()", "  local f1_local0 = 9", "  local f1_local1 = 2", "  local f1_local2 = 3", "  return 0", "end");
        var m = L("function A()", "  local f1_local0 = 1", "  local f1_local5 = 2", "  local f1_local2 = 3", "  return 1", "end");
        Assert.Equal(e, Merge(b, e, m, out var r, out var u));
        Assert.Equal(new[] { "CONFLICT A (EV kept until resolved)" }, r);
        Assert.Equal(new[] { "A" }, u);
    }

    [Fact]
    public void Statements_KeyedByMaskedText_DuplicatesNumbered_BlankLinesDropped()
    {
        var b = L("x = 1", "", "x = 1", "  ", "Goal.A = function(f1_arg0)", "  return f1_arg0", "end");
        var m = L("x = 1", "x = 1", "x = 1", "Goal.A = function(f1_arg0)", "  return f1_arg0", "end");
        var outText = Merge(b, b, m, out var r, out _);
        Assert.Equal("x = 1\n\nx = 1\n\nx = 1\n\nGoal.A = function(f1_arg0)\n  return f1_arg0\nend\n", outText);
        Assert.Equal(new[] { "MMV-added stmt:x = 1#2" }, r);
    }

    [Fact]
    public void UniversalNewlines_CrLfAndLoneCrBecomeLf()
    {
        var b = "function A()\r\n  return 1\rend\r\n";
        Assert.Equal(A0 + "\n", Merge(b, b, b, out _, out _));
    }

    [Fact]
    public void BothAddedDifferently_WithoutBase_ThrowsLikePython()
    {
        // luafuncmerge.py: align_prefix(None, ...) raises TypeError.
        Assert.ThrowsAny<Exception>(() => Merge(L(), L("function A()\n  return 1\nend"), L("function A()\n  return 2\nend"), out _, out _));
    }

    [Fact]
    public void FunctionWithoutEnd_RunsToNextDefinition()
    {
        var t = L("function A()", "  if x then", "end_of_nothing", "function B()", "  return 2", "end", "y = 1");
        Assert.Equal("function A()\n  if x then\nend_of_nothing\n\nfunction B()\n  return 2\nend\n\ny = 1\n", Merge(t, t, t, out _, out _));
    }
}
