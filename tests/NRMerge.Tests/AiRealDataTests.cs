using System.Text;
using NRMerge;
using Xunit;
using Xunit.Abstractions;

namespace NRMerge.Tests;

/// <summary>
/// Dev-only: the in-process AI merge with the shipped rules (merge/ai/*.rules.json) reproduces the pipeline's work\ai
/// outputs byte-for-byte. Each case returns early (noted in the test output) when work\ai is absent.
/// </summary>
public class AiRealDataTests
{
    static readonly string WorkAi = Path.Combine(BuildConfig.DevRepo, "work", "ai");
    static readonly string RulesDir = Path.Combine(BuildConfig.DevRepo, "merge", "ai");
    public static readonly TheoryData<string> Scripts = new() { "473000_battle", "516000_battle", "525010_battle", "goal_list", "goal_list_dlc1" };

    readonly ITestOutputHelper output;
    public AiRealDataTests(ITestOutputHelper output) => this.output = output;

    static string Norm(string dir, string side) => PyText.Read(Path.Combine(dir, side + ".norm.lua"));
    static string Bytes(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    [Theory]
    [MemberData(nameof(Scripts))]
    public void FuncMerge_WithRules_EqualsPipelineOutput(string name)
    {
        var dir = Path.Combine(WorkAi, name);
        if (!File.Exists(Path.Combine(dir, "merged.lua"))) { output.WriteLine($"SKIPPED: {dir} missing"); return; }
        var rules = AiRules.Load(Path.Combine(RulesDir, name + ".lua.rules.json"));
        var text = LuaFuncMerge.Merge(Norm(dir, "base"), Norm(dir, "ev"), Norm(dir, "mmv"), rules, out var report, out var unresolved);
        Assert.Equal(Bytes(Path.Combine(dir, "merged.lua")), text);
        Assert.Equal(Bytes(Path.Combine(dir, "merged.lua.report.txt")), LuaFuncMerge.ReportText(report));
        Assert.Empty(unresolved);
    }

    [Fact]
    public void ShippedRules_LoadAndCoverTheHandResolvedFunctions()
    {
        Assert.Equal(new[] { "Goal.Activate" }, AiRules.Load(Path.Combine(RulesDir, "473000_battle.lua.rules.json")).Rules.Keys);
        Assert.Equal(6, AiRules.Load(Path.Combine(RulesDir, "525010_battle.lua.rules.json")).Rules.Count);
    }
}
