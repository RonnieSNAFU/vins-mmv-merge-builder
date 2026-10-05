using System.Text;
using NRMerge;
using Xunit;
using Xunit.Abstractions;

namespace NRMerge.Tests;

/// <summary>Dev-only: HksResolve on the pipeline's real c0000 diff3 (work\hks). Returns early when work\hks is absent.</summary>
public class HksRealDataTests
{
    static readonly string HksDir = Path.Combine(BuildConfig.DevRepo, "work", "hks");
    readonly ITestOutputHelper output;
    public HksRealDataTests(ITestOutputHelper output) => this.output = output;

    bool Skip(params string[] paths)
    {
        foreach (var p in paths)
            if (!File.Exists(p)) { output.WriteLine($"SKIPPED: {p} missing"); return true; }
        return false;
    }

    [Fact]
    public void Hks_EqualsPipelineOutput()
    {
        var diff3 = Path.Combine(HksDir, "c0000.diff3.hks");
        var resolved = Path.Combine(HksDir, "c0000.resolved.hks");
        if (Skip(diff3, resolved)) return;
        Assert.Equal(Encoding.UTF8.GetString(File.ReadAllBytes(resolved)), HksResolve.Resolve(PyText.Read(diff3)));
    }

    /// <summary>Removing one real hunk from today's diff3 trips the hunk-count guard.</summary>
    [Fact]
    public void Hks_GuardTripsWhenAHunkDisappears()
    {
        var diff3 = Path.Combine(HksDir, "c0000.diff3.hks");
        if (Skip(diff3)) return;
        var text = PyText.Read(diff3);
        int s = text.IndexOf("<<<<<<< EV"), e = text.IndexOf(">>>>>>> MMV\n", s) + ">>>>>>> MMV\n".Length;
        var ex = Assert.Throws<BuildException>(() => HksResolve.Resolve(text.Remove(s, e - s)));
        Assert.Equal("c0000.hks: expected 14 conflict hunks, found 13; Elden Vins or MMV changed, update the HKS rules in HksResolve.cs", ex.Message);
    }
}
