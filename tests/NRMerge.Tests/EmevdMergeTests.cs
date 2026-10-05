using NRMerge;
using SoulsFormats;
using Xunit;

public class EmevdMergeTests
{
    static EMEVD.Instruction I(int bank, int id, params object[] args) => new(bank, id, args);

    [Fact]
    public void InstructionsInsertedByBothSidesAreKeptAndParametersFollowTheirInstruction()
    {
        if (!File.Exists(Paths.Emedf)) return;   // needs DarkScript3's nr-common.emedf.json (fetched at build time, not in the repo)
        var b = new EMEVD.Event(0);
        b.Instructions.Add(I(2000, 0, 0, 100u, 0));
        b.Instructions.Add(I(2000, 0, 1, 200u, 0));

        var e = new EMEVD.Event(0);
        e.Instructions.Add(I(2000, 0, 0, 100u, 0));
        e.Instructions.Add(I(2004, 8, 1000u, 5));          // EV inserts SetSpEffect, parameterised
        e.Parameters.Add(new EMEVD.Parameter(1, 4, 0, 4));
        e.Instructions.Add(I(2000, 0, 1, 200u, 0));

        var m = new EMEVD.Event(0);
        m.Instructions.Add(I(2000, 0, 0, 100u, 0));
        m.Instructions.Add(I(2000, 0, 1, 200u, 0));
        m.Instructions.Add(I(2000, 0, 2, 300u, 0));        // MMV appends an InitializeEvent

        var r = EmevdMerge.MergeEvent(b, e, m, out var conflicts);
        Assert.Equal(0, conflicts);
        Assert.Equal(4, r.Instructions.Count);
        Assert.Equal(2004, r.Instructions[1].Bank);
        Assert.Equal(300u, BitConverter.ToUInt32(r.Instructions[3].ArgData, 4));
        Assert.Single(r.Parameters);
        Assert.Equal(1, r.Parameters[0].InstructionIndex);
    }
}

public class EmevdSkipFixTests
{
    static EMEVD.Instruction I(int bank, int id, params object[] args) => new(bank, id, args);

    [Fact]
    public void SkipCountShrinksWhenTheOtherSideDeletedSkippedLines()
    {
        if (!File.Exists(Paths.Emedf)) return;   // needs DarkScript3's nr-common.emedf.json (fetched at build time, not in the repo)
        // base: skip2 A B C ; EV deletes skip,A,B ; MMV: skip4 A B X Y C
        var b = new EMEVD.Event(0); var e = new EMEVD.Event(0); var m = new EMEVD.Event(0);
        b.Instructions.AddRange(new[] { I(1003, 1, (byte)2, (byte)0, (byte)0, 7604u), I(2000, 6, 0, 1u), I(2000, 6, 0, 2u), I(2000, 6, 0, 9u) });
        e.Instructions.AddRange(new[] { I(2000, 6, 0, 9u) });
        m.Instructions.AddRange(new[] { I(1003, 1, (byte)4, (byte)0, (byte)0, 7604u), I(2000, 6, 0, 1u), I(2000, 6, 0, 2u), I(2000, 6, 0, 3u), I(2000, 6, 0, 4u), I(2000, 6, 0, 9u) });
        var r = EmevdMerge.MergeEvent(b, e, m, out _);
        // expected: skip2 X Y C   (A,B deleted by EV; skip keeps covering X,Y)
        Assert.Equal(4, r.Instructions.Count);
        Assert.Equal(1003, r.Instructions[0].Bank);
        Assert.Equal(2, r.Instructions[0].ArgData[0]);
        Assert.Equal(9u, BitConverter.ToUInt32(r.Instructions[3].ArgData, 4));
    }
}

public class EmevdSubsumeTests
{
    static EMEVD.Instruction I(int bank, int id, params object[] args) => new(bank, id, args);

    [Fact]
    public void WhenEvChangesAreContainedInMmvTheMmvEventIsTakenWhole()
    {
        var b = new EMEVD.Event(1); var e = new EMEVD.Event(1); var m = new EMEVD.Event(1);
        b.Instructions.AddRange(new[] { I(1000, 3, (byte)1, (byte)0, (byte)0, (byte)0), I(2003, 102, 0, -1, 0), I(2000, 6, 0, 5u) });
        e.Instructions.AddRange(new[] { I(1000, 3, (byte)1, (byte)0, (byte)0, (byte)0), I(2003, 102, 0, 0, 0), I(2000, 6, 0, 5u) });
        m.Instructions.AddRange(new[] { I(3, 40, 7), I(2003, 102, 0, 0, 0), I(2000, 6, 0, 5u), I(2000, 6, 0, 6u) });
        var r = EmevdMerge.MergeEvent(b, e, m, out var conflicts);
        Assert.Equal(0, conflicts);
        Assert.Equal(4, r.Instructions.Count);
        Assert.Equal(3, r.Instructions[0].Bank);
    }
}

public class EmevdSkipReplacementTests
{
    static EMEVD.Instruction I(int bank, int id, params object[] args) => new(bank, id, args);
    static EMEVD.Instruction Skip(byte n) => I(1000, 3, n, (byte)0, (byte)0, (byte)0);

    [Fact]
    public void StableSkipStillCoversALineTheOtherSideReplaced()
    {
        if (!File.Exists(Paths.Emedf)) return;   // needs DarkScript3's nr-common.emedf.json (fetched at build time, not in the repo)
        // base: skip3 A B skip2 C D E ... far below another skip2
        // EV changes A,C (values); MMV replaces skip2 by skip4 and inserts X Y after D
        var b = new EMEVD.Event(0); var e = new EMEVD.Event(0); var m = new EMEVD.Event(0);
        b.Instructions.AddRange(new[] { Skip(3), I(2000, 6, 0, 1u), I(2000, 6, 0, 2u), Skip(2), I(2000, 6, 0, 3u), I(2000, 6, 0, 4u), I(2000, 6, 0, 5u), Skip(2), I(2000, 6, 0, 6u) });
        e.Instructions.AddRange(new[] { Skip(3), I(2000, 6, 0, 11u), I(2000, 6, 0, 2u), Skip(2), I(2000, 6, 0, 13u), I(2000, 6, 0, 4u), I(2000, 6, 0, 5u), Skip(2), I(2000, 6, 0, 16u) });
        m.Instructions.AddRange(new[] { Skip(3), I(2000, 6, 0, 11u), I(2000, 6, 0, 2u), Skip(4), I(2000, 6, 0, 13u), I(2000, 6, 0, 4u), I(2000, 6, 0, 7u), I(2000, 6, 0, 8u), I(2000, 6, 0, 5u), Skip(2), I(2000, 6, 0, 6u) });
        var r = EmevdMerge.MergeEvent(b, e, m, out _);
        Assert.Equal(3, r.Instructions[0].ArgData[0]);
        Assert.Equal(4, r.Instructions[3].ArgData[0]);
    }
}
