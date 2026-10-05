using Xunit;

namespace NRMerge.Tests;

public class HksResolveTests
{
    record Hunk(string[] Ev, string[] Base, string[] Mmv);

    static Hunk H(string[] ev, string[] bs, string[] mmv) => new(ev, bs, mmv);

    static Dictionary<int, Hunk> Defaults() => new()
    {
        [1] = H(new[] { "function EvNew()", "end" }, new string[0], new[] { "function MmvNew()", "end" }),
        [2] = H(new[] { "    local aow_list = {", "        100,", "        200", "    }" },
                new[] { "    if arts_id == 100 or arts_id == 200 then" },
                new[] { "    if arts_id == 100 or arts_id == 200 or arts_id == 300 or arts_id == 400 then" }),
        [3] = H(new[] { "    if arts_id == 1 or", "       arts_id == 2 then" }, new[] { "    if arts_id == 1 then" }, new[] { "    if arts_id == 1 or arts_id == 3 then" }),
        [4] = H(new[] { "    if deflect_wide then" }, new[] { "    if deflect then" }, new[] { "    if kind == 60 then return end", "    if deflect then" }),
        [5] = H(new[] { "    if a and b then" }, new[] { "    if a then" }, new[] { "    if b and a then" }),
        [6] = H(new[] { "    if kind == 1 or kind == 2 then" }, new[] { "    if kind == 1 then" }, new[] { "    if kind == 1 or kind == WEAPON_CATEGORY_WHI then" }),
        [7] = H(new[] { "    if IsKind(kind, 1, 2) == TRUE then" }, new[] { "    if IsKind(kind, 1) == TRUE then" }, new[] { "    if IsKind(kind, 1, 3) == TRUE then" }),
        [8] = H(new[] { "    elseif x == 1 then", "        A()" }, new string[0], new[] { "    elseif x == 2 then", "        B()" }),
        [9] = H(new[] { "    if not deflect then", "        if ev_inner then" }, new[] { "    if deflect then", "        if base_inner then" }, new[] { "    if deflect then", "        if mmv_inner then" }),
        [10] = Body(), [11] = Body(), [12] = Body(), [13] = Body(), [14] = Body(),
    };

    static Hunk Body() => H(new[] { "    -- EV comment", "    if ev_cond then", "        EvBody()" },
                            new[] { "    if base_cond then", "        BaseBody()" },
                            new[] { "    elseif mmv_cond then", "        MmvBody1()", "        MmvBody2()" });

    static string Build(Dictionary<int, Hunk> hunks, int count = 14)
    {
        var lines = new List<string> { "ctx0" };
        for (int n = 1; n <= count; n++)
        {
            var h = hunks[n];
            lines.Add("<<<<<<< EV"); lines.AddRange(h.Ev);
            lines.Add("||||||| BASE"); lines.AddRange(h.Base);
            lines.Add("======="); lines.AddRange(h.Mmv);
            lines.Add(">>>>>>> MMV");
            lines.Add("ctx" + n);
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Lines between ctx(n-1) and ctx(n) of the resolved text.</summary>
    static string[] Segment(string resolved, int n)
    {
        var lines = resolved.Split('\n').ToList();
        int s = lines.IndexOf("ctx" + (n - 1)), e = lines.IndexOf("ctx" + n);
        return lines.Skip(s + 1).Take(e - s - 1).ToArray();
    }

    static string[] Resolve(Dictionary<int, Hunk> h, int n) => Segment(HksResolve.Resolve(Build(h)), n);

    [Fact]
    public void Rule_1_8_KeepBoth_EvFirst()
    {
        Assert.Equal(new[] { "function EvNew()", "end", "function MmvNew()", "end" }, Resolve(Defaults(), 1));
        Assert.Equal(new[] { "    elseif x == 1 then", "        A()", "    elseif x == 2 then", "        B()" }, Resolve(Defaults(), 8));
    }

    [Fact]
    public void Rule_2_AppendsMmvSkillIdsToEvList()
        => Assert.Equal(new[] { "    local aow_list = {", "        100,", "        200,", "        300, -- MMV", "        400, -- MMV", "    }" }, Resolve(Defaults(), 2));

    [Fact]
    public void Rule_2_IdsAlreadyInEvList_KeepsEv()
    {
        var h = Defaults();
        h[2] = H(new[] { "    local aow_list = {", "        100,", "        300", "    }" }, new[] { "    if arts_id == 100 then" }, new[] { "    if arts_id == 100 or arts_id == 300 then" });
        Assert.Equal(h[2].Ev, Resolve(h, 2));
    }

    [Fact]
    public void Rule_2_LastNumberLineAlreadyHasComma_NotDoubled()
    {
        var h = Defaults();
        h[2] = H(new[] { "    local aow_list = {", "        100, -- x", "    }" }, new[] { "    if arts_id == 1 then" }, new[] { "    if arts_id == 1 or arts_id == 7 then" });
        Assert.Equal(new[] { "    local aow_list = {", "        100, -- x", "        7, -- MMV", "    }" }, Resolve(h, 2));
    }

    [Fact]
    public void Rule_3_AppendsOnlyMmvIdsToEvCondition()
        => Assert.Equal(new[] { "    if arts_id == 1 or", "       arts_id == 2 or arts_id == 3 then" }, Resolve(Defaults(), 3));

    [Fact]
    public void Rule_4_MmvGuardsThenEvCondition()
        => Assert.Equal(new[] { "    if kind == 60 then return end", "    if deflect_wide then" }, Resolve(Defaults(), 4));

    [Fact]
    public void Rule_5_KeepsEv() => Assert.Equal(new[] { "    if a and b then" }, Resolve(Defaults(), 5));

    [Fact]
    public void Rule_6_AddsKind61() => Assert.Equal(new[] { "    if kind == 1 or kind == 2 or kind == 61 then" }, Resolve(Defaults(), 6));

    [Fact]
    public void Rule_7_AddsCategory53() => Assert.Equal(new[] { "    if IsKind(kind, 1, 2, 53) == TRUE then" }, Resolve(Defaults(), 7));

    [Fact]
    public void Rule_9_EvConditionMmvInnerCheck()
        => Assert.Equal(new[] { "    if not deflect then", "        if mmv_inner then" }, Resolve(Defaults(), 9));

    [Fact]
    public void Rule_10_to_14_EvConditionMmvBody()
    {
        for (int n = 10; n <= 14; n++)
            Assert.Equal(new[] { "    -- EV comment", "    if ev_cond then", "        MmvBody1()", "        MmvBody2()" }, Resolve(Defaults(), n));
    }

    [Fact]
    public void ContextOutsideHunksIsKept_AndNoMarkersRemain()
    {
        var text = HksResolve.Resolve(Build(Defaults()));
        Assert.StartsWith("ctx0\n", text);
        Assert.EndsWith("ctx14\n", text);
        Assert.DoesNotContain("<<<<<<<", text);
        Assert.DoesNotContain("|||||||", text);
    }

    [Fact]
    public void CrLfInputIsNormalized()
        => Assert.Equal(HksResolve.Resolve(Build(Defaults())), HksResolve.Resolve(Build(Defaults()).Replace("\n", "\r\n")));

    [Theory]
    [InlineData(13)]
    [InlineData(0)]
    public void WrongHunkCount_Throws(int count)
    {
        var h = Defaults();
        var ex = Assert.Throws<BuildException>(() => HksResolve.Resolve(Build(h, count)));
        Assert.Equal($"c0000.hks: expected 14 conflict hunks, found {count}; Elden Vins or MMV changed, update the HKS rules in HksResolve.cs", ex.Message);
    }

    [Fact]
    public void FifteenHunks_Throws()
    {
        var text = Build(Defaults()) + "<<<<<<< EV\na\n||||||| BASE\n=======\nb\n>>>>>>> MMV\n";
        Assert.Contains("found 15", Assert.Throws<BuildException>(() => HksResolve.Resolve(text)).Message);
    }

    [Fact]
    public void FullDefaultOutput_MatchesPythonResolver()
    {
        // Produced by the former Python resolver (resolve_c0000) on Build(Defaults()).
        const string expected = "ctx0\nfunction EvNew()\nend\nfunction MmvNew()\nend\nctx1\n    local aow_list = {\n        100,\n        200,\n        300, -- MMV\n        400, -- MMV\n    }\nctx2\n    if arts_id == 1 or\n       arts_id == 2 or arts_id == 3 then\nctx3\n    if kind == 60 then return end\n    if deflect_wide then\nctx4\n    if a and b then\nctx5\n    if kind == 1 or kind == 2 or kind == 61 then\nctx6\n    if IsKind(kind, 1, 2, 53) == TRUE then\nctx7\n    elseif x == 1 then\n        A()\n    elseif x == 2 then\n        B()\nctx8\n    if not deflect then\n        if mmv_inner then\nctx9\n    -- EV comment\n    if ev_cond then\n        MmvBody1()\n        MmvBody2()\nctx10\n    -- EV comment\n    if ev_cond then\n        MmvBody1()\n        MmvBody2()\nctx11\n    -- EV comment\n    if ev_cond then\n        MmvBody1()\n        MmvBody2()\nctx12\n    -- EV comment\n    if ev_cond then\n        MmvBody1()\n        MmvBody2()\nctx13\n    -- EV comment\n    if ev_cond then\n        MmvBody1()\n        MmvBody2()\nctx14\n";
        Assert.Equal(expected, HksResolve.Resolve(Build(Defaults())));
    }
}
