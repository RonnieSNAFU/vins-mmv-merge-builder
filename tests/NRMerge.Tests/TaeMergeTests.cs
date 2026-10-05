using NRMerge;
using SoulsFormats;
using Xunit;

public class TaeMergeTests
{
    static TAE.Event Ev(int type, float start, float end, params byte[] p) => new(start, end, type, 0, p.Length == 0 ? new byte[4] : p, false);

    static TAE.Animation Anim(long id, params TAE.Event[] events)
    {
        var a = new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.Standard(), $"a{id}.hkt");
        a.Events.AddRange(events);
        return a;
    }

    static TAE Tae(params TAE.Animation[] anims) => new() { Format = TAE.TAEFormat.SDT, Animations = anims.ToList(), Flags = new byte[8] };

    static List<string> Sigs(TAE.Animation a) => a.Events.Select(e => TaeSig.Event(e, false)).ToList();

    [Fact]
    public void AnimationsAddedByEachSideArePresentInIdOrder()
    {
        var b = Tae(Anim(1000, Ev(1, 0, 1)));
        var e = Tae(Anim(1000, Ev(1, 0, 1)), Anim(3000, Ev(2, 0, 1)));
        var m = Tae(Anim(1000, Ev(1, 0, 1)), Anim(2000, Ev(3, 0, 1)));
        var r = TaeMerge.Merge(b, e, m, out var conflicts);
        Assert.Equal(new long[] { 1000, 2000, 3000 }, r.Animations.Select(a => a.ID));
        Assert.Empty(conflicts);
    }

    [Fact]
    public void EventsAddedByEachSideToTheSameAnimationAreUnited()
    {
        var b = Tae(Anim(1000, Ev(1, 0, 1)));
        var e = Tae(Anim(1000, Ev(1, 0, 1), Ev(2, 0.2f, 0.5f)));
        var m = Tae(Anim(1000, Ev(1, 0, 1), Ev(3, 0.3f, 0.6f), Ev(2, 0.2f, 0.5f)));   // MMV adds the same event as EV too
        var r = TaeMerge.Merge(b, e, m, out var conflicts);
        var a = Assert.Single(r.Animations);
        Assert.Equal(3, a.Events.Count);
        Assert.Contains(a.Events, x => x.Type == 3);
        Assert.Single(a.Events, x => x.Type == 2);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void EventChangedByBothKeepsEvsVersionAndIsReported()
    {
        var b = Tae(Anim(1000, Ev(1, 0.1f, 1, 1, 0, 0, 0)));
        var e = Tae(Anim(1000, Ev(1, 0.1f, 1, 2, 0, 0, 0)));
        var m = Tae(Anim(1000, Ev(1, 0.1f, 1, 3, 0, 0, 0)));
        var r = TaeMerge.Merge(b, e, m, out var conflicts);
        var x = Assert.Single(Assert.Single(r.Animations).Events);
        Assert.Equal(2, x.GetParameterBytes(false)[0]);
        Assert.Single(conflicts);
    }

    [Fact]
    public void MmvsOneSidedEventEditAndDeletionApplyWhenEvLeftThoseEventsAlone()
    {
        var b = Tae(Anim(1000, Ev(1, 0.1f, 1, 1, 0, 0, 0), Ev(5, 0.4f, 0.8f), Ev(7, 0, 2)));
        var e = Tae(Anim(1000, Ev(1, 0.1f, 1, 1, 0, 0, 0), Ev(5, 0.4f, 0.8f), Ev(7, 0, 2), Ev(9, 0, 1)));   // EV adds an event
        var m = Tae(Anim(1000, Ev(1, 0.1f, 1, 4, 0, 0, 0), Ev(7, 0, 2)));                                 // MMV edits type 1, deletes type 5
        var r = TaeMerge.Merge(b, e, m, out var conflicts);
        var a = Assert.Single(r.Animations);
        Assert.DoesNotContain(a.Events, x => x.Type == 5);
        Assert.Equal(4, Assert.Single(a.Events, x => x.Type == 1).GetParameterBytes(false)[0]);
        Assert.Contains(a.Events, x => x.Type == 9);
        Assert.Equal(3, a.Events.Count);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void AnimationAddedByBothUsesTheFallbackBaseForEventLevelMerge()
    {
        var b = Tae();
        var er = Tae(Anim(5000, Ev(1, 0.1f, 1, 1, 0, 0, 0), Ev(2, 0, 1)));
        var e = Tae(Anim(5000, Ev(1, 0.05f, 1, 1, 0, 0, 0), Ev(2, 0, 1)));      // EV speeds up the type-1 event
        var m = Tae(Anim(5000, Ev(1, 0.1f, 1, 1, 0, 0, 0), Ev(2, 0, 1), Ev(8, 0, 1)));   // MMV ports it and adds an event
        var r = TaeMerge.Merge(b, e, m, out var conflicts, er);
        var a = Assert.Single(r.Animations);
        Assert.Equal(3, a.Events.Count);
        Assert.Equal(0.05f, Assert.Single(a.Events, x => x.Type == 1).StartTime);
        Assert.Contains(a.Events, x => x.Type == 8);
    }
}

public class EnemyRestoreTests
{
    [Fact]
    public void ClipAnimationsMissingFromTheMergedTaeAreRestoredFromTheDonor()
    {
        TAE T(params long[] ids) { var t = new TAE { Format = TAE.TAEFormat.SDT, Flags = new byte[8], Animations = new() }; foreach (var id in ids) t.Animations.Add(new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.Standard(), "x")); return t; }
        var merged = new Dictionary<string, TAE> { ["c2190.tae"] = T(3000) };
        var donor = new Dictionary<string, TAE> { ["c2190.tae"] = T(3000, 1003017) };
        var restored = EnemyMerge.RestoreClipAnimations(merged, donor, new[] { "a000_003000", "a001_003017", "a005_000001" });
        Assert.Equal(new long[] { 3000, 1003017 }, merged["c2190.tae"].Animations.Select(a => a.ID));
        Assert.Equal(new[] { "c2190.tae 1003017" }, restored);
    }
}
