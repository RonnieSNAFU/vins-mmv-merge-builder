using NRMerge;
using SoulsFormats;
using Xunit;

public class JudgeRemapTests
{
    static TAE.Event Atk(int judge, float start = 0)
    {
        var p = new byte[16];
        BitConverter.GetBytes(judge).CopyTo(p, 8);         // event 1: Attack Type, Attack Index, Behavior Judge ID
        return new TAE.Event(start, start + 0.1f, 1, 0, p, false);
    }

    static TAE.Event Common(int judge)
    {
        var p = new byte[16];
        BitConverter.GetBytes(judge).CopyTo(p, 4);         // event 5: Attack Index, Behavior Judge ID
        return new TAE.Event(0, 0.1f, 5, 0, p, false);
    }

    static TAE Tae(params (long id, TAE.Event[] ev)[] anims)
    {
        var t = new TAE { Format = TAE.TAEFormat.SDT, Flags = new byte[8], Animations = new() };
        foreach (var (id, ev) in anims)
        {
            var a = new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.Standard(), $"a{id}.hkt");
            a.Events.AddRange(ev);
            t.Animations.Add(a);
        }
        return t;
    }

    [Fact]
    public void NeededJudgesGetTheLowestFreeJudges()
    {
        var map = JudgeRemap.Assign(new[] { 310, 300 }, new HashSet<int>(Enumerable.Range(0, 1000).Except(new[] { 29, 39, 49 })));
        Assert.Equal(29, map[300]);
        Assert.Equal(39, map[310]);
    }

    [Fact]
    public void OnlyEventsMmvAddedAreRewritten()
    {
        var map = new Dictionary<int, int> { [300] = 29 };
        var b = Tae((1000, new[] { Atk(300) }));
        var e = Tae((1000, new[] { Atk(300), Atk(300, 0.5f) }));                  // EV added an identical event at 0.5
        var m = Tae((1000, new[] { Atk(300), Atk(300, 0.5f), Atk(300, 0.7f) }),   // MMV: same as EV + its own at 0.7
                    (2000, new[] { Common(300), Common(301) }));                  // MMV-only animation
        var r = JudgeRemap.RewriteMmv(b, e, m, map, out var changed);
        int J(TAE.Event x) => BitConverter.ToInt32(x.GetParameterBytes(false), x.Type == 5 ? 4 : 8);
        Assert.Equal(new[] { 300, 300, 29 }, r.Animations[0].Events.Select(J));
        Assert.Equal(new[] { 29, 301 }, r.Animations[1].Events.Select(J));
        Assert.Equal(2, changed);
    }
}
