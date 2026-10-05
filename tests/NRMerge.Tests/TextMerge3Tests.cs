using System.Diagnostics;
using System.Text;
using NRMerge;
using Xunit;
using M = NRMerge.TextMerge3;

namespace NRMerge.Tests;

/// <summary>TextMerge3 = in-process <c>git merge-file -p [--diff3]</c>. The expected outputs in <see cref="GitCases"/> were produced by git 2.50.1.</summary>
public class TextMerge3Tests
{
    [Fact]
    public void Clean_OneSideChange_TakesIt()
    {
        var r = M.Merge("a\nX\nc\n", "a\nb\nc\n", "a\nb\nc\n", TextMergeStyle.Merge, "A", "O", "B", out var n);
        Assert.Equal("a\nX\nc\n", r);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Clean_BothSidesDisjoint()
    {
        var r = M.Merge("X\nb\nc\nd\ne\n", "a\nb\nc\nd\ne\n", "a\nb\nc\nd\nY\n", TextMergeStyle.Merge, "A", "O", "B", out var n);
        Assert.Equal("X\nb\nc\nd\nY\n", r);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Conflict_MergeStyle_HasLabelledMarkers()
    {
        var r = M.Merge("a\nX\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Merge, "EV", "BASE", "MMV", out var n);
        Assert.Equal("a\n<<<<<<< EV\nX\n=======\nY\n>>>>>>> MMV\nc\n", r);
        Assert.Equal(1, n);
    }

    [Fact]
    public void Conflict_Diff3Style_ShowsBase()
    {
        var r = M.Merge("a\nX\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Diff3, "EV", "BASE", "MMV", out var n);
        Assert.Equal("a\n<<<<<<< EV\nX\n||||||| BASE\nb\n=======\nY\n>>>>>>> MMV\nc\n", r);
        Assert.Equal(1, n);
    }

    [Fact]
    public void Conflict_Crlf_MarkersUseCrlf()
    {
        var r = M.Merge("a\r\nX\r\nc\r\n", "a\r\nb\r\nc\r\n", "a\r\nY\r\nc\r\n", TextMergeStyle.Merge, "A", "O", "B", out var n);
        Assert.Equal("a\r\n<<<<<<< A\r\nX\r\n=======\r\nY\r\n>>>>>>> B\r\nc\r\n", r);
        Assert.Equal(1, n);
    }

    /// <summary>git's exit code caps at 127; our count is the true number of conflict hunks.</summary>
    [Fact]
    public void ConflictCount_NotCappedAt127()
    {
        StringBuilder a = new(), o = new(), b = new();
        for (int i = 0; i < 200; i++)
        {
            foreach (var s in new[] { a, o, b }) for (int k = 0; k < 4; k++) s.Append("keep alnum ").Append(i).Append('_').Append(k).Append('\n');
            a.Append("A").Append(i).Append('\n'); o.Append("O").Append(i).Append('\n'); b.Append("B").Append(i).Append('\n');
        }
        var r = M.Merge(a.ToString(), o.ToString(), b.ToString(), TextMergeStyle.Merge, "A", "O", "B", out var n);
        Assert.Equal(200, r.Split('\n').Count(l => l == "<<<<<<< A"));
        Assert.Equal(200, n);
    }

    public static IEnumerable<object[]> GitCases() => new[]
    {
        new object[] { "identical", "a\nb\n", "a\nb\n", "a\nb\n", TextMergeStyle.Merge, "a\nb\n", 0 },
        new object[] { "identical (swapped)", "a\nb\n", "a\nb\n", "a\nb\n", TextMergeStyle.Merge, "a\nb\n", 0 },
        new object[] { "identical", "a\nb\n", "a\nb\n", "a\nb\n", TextMergeStyle.Diff3, "a\nb\n", 0 },
        new object[] { "identical (swapped)", "a\nb\n", "a\nb\n", "a\nb\n", TextMergeStyle.Diff3, "a\nb\n", 0 },
        new object[] { "empty all", "", "", "", TextMergeStyle.Merge, "", 0 },
        new object[] { "empty all (swapped)", "", "", "", TextMergeStyle.Merge, "", 0 },
        new object[] { "empty all", "", "", "", TextMergeStyle.Diff3, "", 0 },
        new object[] { "empty all (swapped)", "", "", "", TextMergeStyle.Diff3, "", 0 },
        new object[] { "empty base add both same", "x\n", "", "x\n", TextMergeStyle.Merge, "x\n", 0 },
        new object[] { "empty base add both same (swapped)", "x\n", "", "x\n", TextMergeStyle.Merge, "x\n", 0 },
        new object[] { "empty base add both same", "x\n", "", "x\n", TextMergeStyle.Diff3, "x\n", 0 },
        new object[] { "empty base add both same (swapped)", "x\n", "", "x\n", TextMergeStyle.Diff3, "x\n", 0 },
        new object[] { "empty base add both diff", "x\n", "", "y\n", TextMergeStyle.Merge, "<<<<<<< A\nx\n=======\ny\n>>>>>>> B\n", 1 },
        new object[] { "empty base add both diff (swapped)", "y\n", "", "x\n", TextMergeStyle.Merge, "<<<<<<< A\ny\n=======\nx\n>>>>>>> B\n", 1 },
        new object[] { "empty base add both diff", "x\n", "", "y\n", TextMergeStyle.Diff3, "<<<<<<< A\nx\n||||||| O\n=======\ny\n>>>>>>> B\n", 1 },
        new object[] { "empty base add both diff (swapped)", "y\n", "", "x\n", TextMergeStyle.Diff3, "<<<<<<< A\ny\n||||||| O\n=======\nx\n>>>>>>> B\n", 1 },
        new object[] { "one side", "a\nX\nc\n", "a\nb\nc\n", "a\nb\nc\n", TextMergeStyle.Merge, "a\nX\nc\n", 0 },
        new object[] { "one side (swapped)", "a\nb\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Merge, "a\nX\nc\n", 0 },
        new object[] { "one side", "a\nX\nc\n", "a\nb\nc\n", "a\nb\nc\n", TextMergeStyle.Diff3, "a\nX\nc\n", 0 },
        new object[] { "one side (swapped)", "a\nb\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Diff3, "a\nX\nc\n", 0 },
        new object[] { "other side", "a\nb\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Merge, "a\nY\nc\n", 0 },
        new object[] { "other side (swapped)", "a\nY\nc\n", "a\nb\nc\n", "a\nb\nc\n", TextMergeStyle.Merge, "a\nY\nc\n", 0 },
        new object[] { "other side", "a\nb\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Diff3, "a\nY\nc\n", 0 },
        new object[] { "other side (swapped)", "a\nY\nc\n", "a\nb\nc\n", "a\nb\nc\n", TextMergeStyle.Diff3, "a\nY\nc\n", 0 },
        new object[] { "both same change", "a\nX\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Merge, "a\nX\nc\n", 0 },
        new object[] { "both same change (swapped)", "a\nX\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Merge, "a\nX\nc\n", 0 },
        new object[] { "both same change", "a\nX\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Diff3, "a\nX\nc\n", 0 },
        new object[] { "both same change (swapped)", "a\nX\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Diff3, "a\nX\nc\n", 0 },
        new object[] { "conflict", "a\nX\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "conflict (swapped)", "a\nY\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\nc\n", 1 },
        new object[] { "conflict", "a\nX\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\nb\n=======\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "conflict (swapped)", "a\nY\nc\n", "a\nb\nc\n", "a\nX\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nY\n||||||| O\nb\n=======\nX\n>>>>>>> B\nc\n", 1 },
        new object[] { "adjacent", "a\nX\nc\nd\n", "a\nb\nc\nd\n", "a\nb\nY\nd\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\nc\n=======\nb\nY\n>>>>>>> B\nd\n", 1 },
        new object[] { "adjacent (swapped)", "a\nb\nY\nd\n", "a\nb\nc\nd\n", "a\nX\nc\nd\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nb\nY\n=======\nX\nc\n>>>>>>> B\nd\n", 1 },
        new object[] { "adjacent", "a\nX\nc\nd\n", "a\nb\nc\nd\n", "a\nb\nY\nd\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\nc\n||||||| O\nb\nc\n=======\nb\nY\n>>>>>>> B\nd\n", 1 },
        new object[] { "adjacent (swapped)", "a\nb\nY\nd\n", "a\nb\nc\nd\n", "a\nX\nc\nd\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nb\nY\n||||||| O\nb\nc\n=======\nX\nc\n>>>>>>> B\nd\n", 1 },
        new object[] { "adjacent2", "X\nb\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Merge, "<<<<<<< A\nX\nb\n=======\na\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "adjacent2 (swapped)", "a\nY\nc\n", "a\nb\nc\n", "X\nb\nc\n", TextMergeStyle.Merge, "<<<<<<< A\na\nY\n=======\nX\nb\n>>>>>>> B\nc\n", 1 },
        new object[] { "adjacent2", "X\nb\nc\n", "a\nb\nc\n", "a\nY\nc\n", TextMergeStyle.Diff3, "<<<<<<< A\nX\nb\n||||||| O\na\nb\n=======\na\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "adjacent2 (swapped)", "a\nY\nc\n", "a\nb\nc\n", "X\nb\nc\n", TextMergeStyle.Diff3, "<<<<<<< A\na\nY\n||||||| O\na\nb\n=======\nX\nb\n>>>>>>> B\nc\n", 1 },
        new object[] { "delete vs modify", "a\nc\n", "a\nb\nc\n", "a\nB\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\n=======\nB\n>>>>>>> B\nc\n", 1 },
        new object[] { "delete vs modify (swapped)", "a\nB\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nB\n=======\n>>>>>>> B\nc\n", 1 },
        new object[] { "delete vs modify", "a\nc\n", "a\nb\nc\n", "a\nB\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\n||||||| O\nb\n=======\nB\n>>>>>>> B\nc\n", 1 },
        new object[] { "delete vs modify (swapped)", "a\nB\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nB\n||||||| O\nb\n=======\n>>>>>>> B\nc\n", 1 },
        new object[] { "both delete", "a\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Merge, "a\nc\n", 0 },
        new object[] { "both delete (swapped)", "a\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Merge, "a\nc\n", 0 },
        new object[] { "both delete", "a\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Diff3, "a\nc\n", 0 },
        new object[] { "both delete (swapped)", "a\nc\n", "a\nb\nc\n", "a\nc\n", TextMergeStyle.Diff3, "a\nc\n", 0 },
        new object[] { "eof no newline a", "a\nb\nX", "a\nb\nc", "a\nb\nc", TextMergeStyle.Merge, "a\nb\nX", 0 },
        new object[] { "eof no newline a (swapped)", "a\nb\nc", "a\nb\nc", "a\nb\nX", TextMergeStyle.Merge, "a\nb\nX", 0 },
        new object[] { "eof no newline a", "a\nb\nX", "a\nb\nc", "a\nb\nc", TextMergeStyle.Diff3, "a\nb\nX", 0 },
        new object[] { "eof no newline a (swapped)", "a\nb\nc", "a\nb\nc", "a\nb\nX", TextMergeStyle.Diff3, "a\nb\nX", 0 },
        new object[] { "eof no newline conflict", "a\nb\nX", "a\nb\nc", "a\nb\nY", TextMergeStyle.Merge, "a\nb\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof no newline conflict (swapped)", "a\nb\nY", "a\nb\nc", "a\nb\nX", TextMergeStyle.Merge, "a\nb\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "eof no newline conflict", "a\nb\nX", "a\nb\nc", "a\nb\nY", TextMergeStyle.Diff3, "a\nb\n<<<<<<< A\nX\n||||||| O\nc\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof no newline conflict (swapped)", "a\nb\nY", "a\nb\nc", "a\nb\nX", TextMergeStyle.Diff3, "a\nb\n<<<<<<< A\nY\n||||||| O\nc\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "eof add newline vs change", "a\nb\nc\n", "a\nb\nc", "a\nb\nY", TextMergeStyle.Merge, "a\nb\n<<<<<<< A\nc\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof add newline vs change (swapped)", "a\nb\nY", "a\nb\nc", "a\nb\nc\n", TextMergeStyle.Merge, "a\nb\n<<<<<<< A\nY\n=======\nc\n>>>>>>> B\n", 1 },
        new object[] { "eof add newline vs change", "a\nb\nc\n", "a\nb\nc", "a\nb\nY", TextMergeStyle.Diff3, "a\nb\n<<<<<<< A\nc\n||||||| O\nc\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof add newline vs change (swapped)", "a\nb\nY", "a\nb\nc", "a\nb\nc\n", TextMergeStyle.Diff3, "a\nb\n<<<<<<< A\nY\n||||||| O\nc\n=======\nc\n>>>>>>> B\n", 1 },
        new object[] { "eof newline both sides diff", "a\nX", "a\nb\n", "a\nY", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof newline both sides diff (swapped)", "a\nY", "a\nb\n", "a\nX", TextMergeStyle.Merge, "a\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "eof newline both sides diff", "a\nX", "a\nb\n", "a\nY", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\nb\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "eof newline both sides diff (swapped)", "a\nY", "a\nb\n", "a\nX", TextMergeStyle.Diff3, "a\n<<<<<<< A\nY\n||||||| O\nb\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "crlf conflict", "a\r\nX\r\nc\r\n", "a\r\nb\r\nc\r\n", "a\r\nY\r\nc\r\n", TextMergeStyle.Merge, "a\r\n<<<<<<< A\r\nX\r\n=======\r\nY\r\n>>>>>>> B\r\nc\r\n", 1 },
        new object[] { "crlf conflict (swapped)", "a\r\nY\r\nc\r\n", "a\r\nb\r\nc\r\n", "a\r\nX\r\nc\r\n", TextMergeStyle.Merge, "a\r\n<<<<<<< A\r\nY\r\n=======\r\nX\r\n>>>>>>> B\r\nc\r\n", 1 },
        new object[] { "crlf conflict", "a\r\nX\r\nc\r\n", "a\r\nb\r\nc\r\n", "a\r\nY\r\nc\r\n", TextMergeStyle.Diff3, "a\r\n<<<<<<< A\r\nX\r\n||||||| O\r\nb\r\n=======\r\nY\r\n>>>>>>> B\r\nc\r\n", 1 },
        new object[] { "crlf conflict (swapped)", "a\r\nY\r\nc\r\n", "a\r\nb\r\nc\r\n", "a\r\nX\r\nc\r\n", TextMergeStyle.Diff3, "a\r\n<<<<<<< A\r\nY\r\n||||||| O\r\nb\r\n=======\r\nX\r\n>>>>>>> B\r\nc\r\n", 1 },
        new object[] { "crlf noeol conflict", "a\r\nX", "a\r\nb", "a\r\nY", TextMergeStyle.Merge, "a\r\n<<<<<<< A\r\nX\r\n=======\r\nY\r\n>>>>>>> B\r\n", 1 },
        new object[] { "crlf noeol conflict (swapped)", "a\r\nY", "a\r\nb", "a\r\nX", TextMergeStyle.Merge, "a\r\n<<<<<<< A\r\nY\r\n=======\r\nX\r\n>>>>>>> B\r\n", 1 },
        new object[] { "crlf noeol conflict", "a\r\nX", "a\r\nb", "a\r\nY", TextMergeStyle.Diff3, "a\r\n<<<<<<< A\r\nX\r\n||||||| O\r\nb\r\n=======\r\nY\r\n>>>>>>> B\r\n", 1 },
        new object[] { "crlf noeol conflict (swapped)", "a\r\nY", "a\r\nb", "a\r\nX", TextMergeStyle.Diff3, "a\r\n<<<<<<< A\r\nY\r\n||||||| O\r\nb\r\n=======\r\nX\r\n>>>>>>> B\r\n", 1 },
        new object[] { "mixed eol", "a\nX\r\nc\n", "a\r\nb\r\nc\r\n", "a\r\nY\nc\r\n", TextMergeStyle.Merge, "<<<<<<< A\na\nX\r\nc\n=======\na\r\nY\nc\r\n>>>>>>> B\n", 1 },
        new object[] { "mixed eol (swapped)", "a\r\nY\nc\r\n", "a\r\nb\r\nc\r\n", "a\nX\r\nc\n", TextMergeStyle.Merge, "<<<<<<< A\na\r\nY\nc\r\n=======\na\nX\r\nc\n>>>>>>> B\n", 1 },
        new object[] { "mixed eol", "a\nX\r\nc\n", "a\r\nb\r\nc\r\n", "a\r\nY\nc\r\n", TextMergeStyle.Diff3, "<<<<<<< A\na\nX\r\nc\n||||||| O\na\r\nb\r\nc\r\n=======\na\r\nY\nc\r\n>>>>>>> B\n", 1 },
        new object[] { "mixed eol (swapped)", "a\r\nY\nc\r\n", "a\r\nb\r\nc\r\n", "a\nX\r\nc\n", TextMergeStyle.Diff3, "<<<<<<< A\na\r\nY\nc\r\n||||||| O\na\r\nb\r\nc\r\n=======\na\nX\r\nc\n>>>>>>> B\n", 1 },
        new object[] { "zealous common", "a\n1\n2\n3\nc\n", "a\nb\nc\n", "a\n1\nZ\n3\nc\n", TextMergeStyle.Merge, "a\n1\n<<<<<<< A\n2\n=======\nZ\n>>>>>>> B\n3\nc\n", 1 },
        new object[] { "zealous common (swapped)", "a\n1\nZ\n3\nc\n", "a\nb\nc\n", "a\n1\n2\n3\nc\n", TextMergeStyle.Merge, "a\n1\n<<<<<<< A\nZ\n=======\n2\n>>>>>>> B\n3\nc\n", 1 },
        new object[] { "zealous common", "a\n1\n2\n3\nc\n", "a\nb\nc\n", "a\n1\nZ\n3\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\n1\n2\n3\n||||||| O\nb\n=======\n1\nZ\n3\n>>>>>>> B\nc\n", 1 },
        new object[] { "zealous common (swapped)", "a\n1\nZ\n3\nc\n", "a\nb\nc\n", "a\n1\n2\n3\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\n1\nZ\n3\n||||||| O\nb\n=======\n1\n2\n3\n>>>>>>> B\nc\n", 1 },
        new object[] { "zealous identical", "a\n1\n2\nc\n", "a\nb\nc\n", "a\n1\n2\nc\n", TextMergeStyle.Merge, "a\n1\n2\nc\n", 0 },
        new object[] { "zealous identical (swapped)", "a\n1\n2\nc\n", "a\nb\nc\n", "a\n1\n2\nc\n", TextMergeStyle.Merge, "a\n1\n2\nc\n", 0 },
        new object[] { "zealous identical", "a\n1\n2\nc\n", "a\nb\nc\n", "a\n1\n2\nc\n", TextMergeStyle.Diff3, "a\n1\n2\nc\n", 0 },
        new object[] { "zealous identical (swapped)", "a\n1\n2\nc\n", "a\nb\nc\n", "a\n1\n2\nc\n", TextMergeStyle.Diff3, "a\n1\n2\nc\n", 0 },
        new object[] { "alnum simplify", "a\nX\n}\n}\n}\nY\nc\n", "a\nb\n}\n}\n}\nd\nc\n", "a\nP\n}\n}\n}\nQ\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n}\n}\n}\nY\n=======\nP\n}\n}\n}\nQ\n>>>>>>> B\nc\n", 1 },
        new object[] { "alnum simplify (swapped)", "a\nP\n}\n}\n}\nQ\nc\n", "a\nb\n}\n}\n}\nd\nc\n", "a\nX\n}\n}\n}\nY\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nP\n}\n}\n}\nQ\n=======\nX\n}\n}\n}\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "alnum simplify", "a\nX\n}\n}\n}\nY\nc\n", "a\nb\n}\n}\n}\nd\nc\n", "a\nP\n}\n}\n}\nQ\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\nb\n=======\nP\n>>>>>>> B\n}\n}\n}\n<<<<<<< A\nY\n||||||| O\nd\n=======\nQ\n>>>>>>> B\nc\n", 2 },
        new object[] { "alnum simplify (swapped)", "a\nP\n}\n}\n}\nQ\nc\n", "a\nb\n}\n}\n}\nd\nc\n", "a\nX\n}\n}\n}\nY\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nP\n||||||| O\nb\n=======\nX\n>>>>>>> B\n}\n}\n}\n<<<<<<< A\nQ\n||||||| O\nd\n=======\nY\n>>>>>>> B\nc\n", 2 },
        new object[] { "alnum keep", "a\nX\nk\nl\nm\nn\nY\nc\n", "a\nb\nk\nl\nm\nn\nd\nc\n", "a\nP\nk\nl\nm\nn\nQ\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n=======\nP\n>>>>>>> B\nk\nl\nm\nn\n<<<<<<< A\nY\n=======\nQ\n>>>>>>> B\nc\n", 2 },
        new object[] { "alnum keep (swapped)", "a\nP\nk\nl\nm\nn\nQ\nc\n", "a\nb\nk\nl\nm\nn\nd\nc\n", "a\nX\nk\nl\nm\nn\nY\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nP\n=======\nX\n>>>>>>> B\nk\nl\nm\nn\n<<<<<<< A\nQ\n=======\nY\n>>>>>>> B\nc\n", 2 },
        new object[] { "alnum keep", "a\nX\nk\nl\nm\nn\nY\nc\n", "a\nb\nk\nl\nm\nn\nd\nc\n", "a\nP\nk\nl\nm\nn\nQ\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\nb\n=======\nP\n>>>>>>> B\nk\nl\nm\nn\n<<<<<<< A\nY\n||||||| O\nd\n=======\nQ\n>>>>>>> B\nc\n", 2 },
        new object[] { "alnum keep (swapped)", "a\nP\nk\nl\nm\nn\nQ\nc\n", "a\nb\nk\nl\nm\nn\nd\nc\n", "a\nX\nk\nl\nm\nn\nY\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nP\n||||||| O\nb\n=======\nX\n>>>>>>> B\nk\nl\nm\nn\n<<<<<<< A\nQ\n||||||| O\nd\n=======\nY\n>>>>>>> B\nc\n", 2 },
        new object[] { "short gap simplify", "a\nX\nk\nY\nc\n", "a\nb\nk\nd\nc\n", "a\nP\nk\nQ\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\nk\nY\n=======\nP\nk\nQ\n>>>>>>> B\nc\n", 1 },
        new object[] { "short gap simplify (swapped)", "a\nP\nk\nQ\nc\n", "a\nb\nk\nd\nc\n", "a\nX\nk\nY\nc\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nP\nk\nQ\n=======\nX\nk\nY\n>>>>>>> B\nc\n", 1 },
        new object[] { "short gap simplify", "a\nX\nk\nY\nc\n", "a\nb\nk\nd\nc\n", "a\nP\nk\nQ\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\nb\n=======\nP\n>>>>>>> B\nk\n<<<<<<< A\nY\n||||||| O\nd\n=======\nQ\n>>>>>>> B\nc\n", 2 },
        new object[] { "short gap simplify (swapped)", "a\nP\nk\nQ\nc\n", "a\nb\nk\nd\nc\n", "a\nX\nk\nY\nc\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nP\n||||||| O\nb\n=======\nX\n>>>>>>> B\nk\n<<<<<<< A\nQ\n||||||| O\nd\n=======\nY\n>>>>>>> B\nc\n", 2 },
        new object[] { "insert same place", "a\nX\nb\n", "a\nb\n", "a\nY\nb\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\nb\n", 1 },
        new object[] { "insert same place (swapped)", "a\nY\nb\n", "a\nb\n", "a\nX\nb\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\nb\n", 1 },
        new object[] { "insert same place", "a\nX\nb\n", "a\nb\n", "a\nY\nb\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\n=======\nY\n>>>>>>> B\nb\n", 1 },
        new object[] { "insert same place (swapped)", "a\nY\nb\n", "a\nb\n", "a\nX\nb\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nY\n||||||| O\n=======\nX\n>>>>>>> B\nb\n", 1 },
        new object[] { "insert top", "X\na\n", "a\n", "Y\na\n", TextMergeStyle.Merge, "<<<<<<< A\nX\n=======\nY\n>>>>>>> B\na\n", 1 },
        new object[] { "insert top (swapped)", "Y\na\n", "a\n", "X\na\n", TextMergeStyle.Merge, "<<<<<<< A\nY\n=======\nX\n>>>>>>> B\na\n", 1 },
        new object[] { "insert top", "X\na\n", "a\n", "Y\na\n", TextMergeStyle.Diff3, "<<<<<<< A\nX\n||||||| O\n=======\nY\n>>>>>>> B\na\n", 1 },
        new object[] { "insert top (swapped)", "Y\na\n", "a\n", "X\na\n", TextMergeStyle.Diff3, "<<<<<<< A\nY\n||||||| O\n=======\nX\n>>>>>>> B\na\n", 1 },
        new object[] { "insert end", "a\nX\n", "a\n", "a\nY\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "insert end (swapped)", "a\nY\n", "a\n", "a\nX\n", TextMergeStyle.Merge, "a\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "insert end", "a\nX\n", "a\n", "a\nY\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nX\n||||||| O\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "insert end (swapped)", "a\nY\n", "a\n", "a\nX\n", TextMergeStyle.Diff3, "a\n<<<<<<< A\nY\n||||||| O\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "unicode", "\u00e4\nX\n", "\u00e4\nb\n", "\u00e4\nY\n", TextMergeStyle.Merge, "\u00e4\n<<<<<<< A\nX\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "unicode (swapped)", "\u00e4\nY\n", "\u00e4\nb\n", "\u00e4\nX\n", TextMergeStyle.Merge, "\u00e4\n<<<<<<< A\nY\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "unicode", "\u00e4\nX\n", "\u00e4\nb\n", "\u00e4\nY\n", TextMergeStyle.Diff3, "\u00e4\n<<<<<<< A\nX\n||||||| O\nb\n=======\nY\n>>>>>>> B\n", 1 },
        new object[] { "unicode (swapped)", "\u00e4\nY\n", "\u00e4\nb\n", "\u00e4\nX\n", TextMergeStyle.Diff3, "\u00e4\n<<<<<<< A\nY\n||||||| O\nb\n=======\nX\n>>>>>>> B\n", 1 },
        new object[] { "empty side", "", "a\nb\n", "a\nB\n", TextMergeStyle.Merge, "<<<<<<< A\n=======\na\nB\n>>>>>>> B\n", 1 },
        new object[] { "empty side (swapped)", "a\nB\n", "a\nb\n", "", TextMergeStyle.Merge, "<<<<<<< A\na\nB\n=======\n>>>>>>> B\n", 1 },
        new object[] { "empty side", "", "a\nb\n", "a\nB\n", TextMergeStyle.Diff3, "<<<<<<< A\n||||||| O\na\nb\n=======\na\nB\n>>>>>>> B\n", 1 },
        new object[] { "empty side (swapped)", "a\nB\n", "a\nb\n", "", TextMergeStyle.Diff3, "<<<<<<< A\na\nB\n||||||| O\na\nb\n=======\n>>>>>>> B\n", 1 },
    };

    [Theory]
    [MemberData(nameof(GitCases))]
    public void MatchesGitOutput(string name, string a, string o, string b, TextMergeStyle style, string expected, int expectedConflicts)
    {
        var r = M.Merge(a, o, b, style, "A", "O", "B", out var n);
        Assert.True(expected == r, name);
        Assert.Equal(expectedConflicts, n);
    }

    // ---- git parity (dev-only: returns early when git is not on PATH) ----

    static readonly UTF8Encoding Utf8 = new(false);

    static bool GitAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p.StandardOutput.ReadToEnd(); p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    static (string output, int exit) Git(string dir, string a, string o, string b, TextMergeStyle style)
    {
        var id = Guid.NewGuid().ToString("N");
        string pa = Path.Combine(dir, id + ".a"), po = Path.Combine(dir, id + ".o"), pb = Path.Combine(dir, id + ".b");
        File.WriteAllBytes(pa, Utf8.GetBytes(a)); File.WriteAllBytes(po, Utf8.GetBytes(o)); File.WriteAllBytes(pb, Utf8.GetBytes(b));
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        psi.ArgumentList.Add("merge-file"); psi.ArgumentList.Add("-p");
        if (style == TextMergeStyle.Diff3) psi.ArgumentList.Add("--diff3");
        foreach (var l in new[] { "A", "O", "B" }) { psi.ArgumentList.Add("-L"); psi.ArgumentList.Add(l); }
        psi.ArgumentList.Add(pa); psi.ArgumentList.Add(po); psi.ArgumentList.Add(pb);
        using var p = Process.Start(psi);
        var err = new Thread(() => p.StandardError.ReadToEnd());
        err.Start();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        err.Join(); p.WaitForExit();
        return (Utf8.GetString(ms.ToArray()), p.ExitCode);
    }

    static readonly string[] Alphabet = { "a", "b", "c", "d", "{", "}", "", "  x = 1", "end", "--", "e", "f" };

    static List<string> Mutate(Random r, List<string> src, int alpha)
    {
        var l = new List<string>(src);
        int edits = Math.Max(1, (int)(src.Count * 0.4 * r.NextDouble()) + r.Next(3));
        for (int e = 0; e < edits; e++)
        {
            int op = r.Next(3), pos = r.Next(l.Count + 1);
            if (op == 0 || l.Count == 0) l.Insert(pos, Alphabet[r.Next(alpha)] + (r.Next(4) == 0 ? "!" : ""));
            else if (op == 1) l.RemoveAt(Math.Min(pos, l.Count - 1));
            else l[Math.Min(pos, l.Count - 1)] = Alphabet[r.Next(alpha)] + "?";
        }
        return l;
    }

    static string Join(Random r, List<string> lines, bool crlf)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (!(i == lines.Count - 1 && r.Next(5) == 0)) sb.Append(r.Next(40) == 0 ? (crlf ? "\n" : "\r\n") : (crlf ? "\r\n" : "\n"));
        }
        return sb.ToString();
    }

    /// <summary>200 random line triples (mixed EOLs, missing final newline), alternating styles, against real git.</summary>
    [Fact]
    public void RandomTriples_MatchGit()
    {
        if (!GitAvailable()) return;
        var dir = Path.Combine(Path.GetTempPath(), "nrmerge_textmerge3_" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            var failures = new System.Collections.Concurrent.ConcurrentBag<int>();
            // Dedicated threads, not the thread pool: each case blocks on a git process.
            int workers = Math.Clamp(Environment.ProcessorCount, 2, 16), next = -1;
            void Worker()
            {
                for (int seed; (seed = Interlocked.Increment(ref next)) < 200;)
                    if (!SameAsGit(dir, seed)) failures.Add(seed);
            }
            var threads = Enumerable.Range(0, workers).Select(_ => new Thread(Worker)).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            Assert.True(failures.IsEmpty, "seeds differing from git: " + string.Join(",", failures.OrderBy(x => x)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static bool SameAsGit(string dir, int seed)
    {
        var r = new Random(seed);
        int alpha = 2 + r.Next(Alphabet.Length - 1);
        var o = new List<string>(); int len = r.Next(0, 25);
        for (int i = 0; i < len; i++) o.Add(Alphabet[r.Next(alpha)]);
        var a = Mutate(r, o, alpha);
        var b = r.Next(6) == 0 ? new List<string>(a) : Mutate(r, o, alpha);
        bool crlf = r.Next(4) == 0;
        string so = Join(r, o, crlf), sa = Join(r, a, crlf), sb = Join(r, b, crlf);
        var style = seed % 2 == 0 ? TextMergeStyle.Merge : TextMergeStyle.Diff3;
        var (exp, code) = Git(dir, sa, so, sb, style);
        var got = M.Merge(sa, so, sb, style, "A", "O", "B", out var n);
        return exp == got && code == n;
    }

    // ---- real data (dev-only: returns early when the work files are absent) ----

    /// <summary>The c0000.hks diff3 PlayerScripts now produces in-process equals the one git produced (work\hks\c0000.diff3.hks).</summary>
    [Fact]
    public void C0000Hks_Diff3_EqualsGitOutput()
    {
        var cfg = BuildConfig.Dev();
        var work = Path.Combine(cfg.WorkDir, "work", "hks");
        string ev = Path.Combine(work, "ev.hks"), mmv = Path.Combine(work, "mmv.hks"), expected = Path.Combine(work, "c0000.diff3.hks");
        var cached = Path.Combine(cfg.CacheDir, Fetch.Catalog(cfg)["c0000-hks-base-197b182"].File);
        if (!File.Exists(ev) || !File.Exists(mmv) || !File.Exists(expected) || !File.Exists(cached)) return;
        var bas = Fetch.Get("c0000-hks-base-197b182", cfg);

        var sw = Stopwatch.StartNew();
        var bytes = PlayerScripts.C0000Diff3(ev, bas, mmv, out var n);
        sw.Stop();
        Assert.Equal(14, n);
        Assert.True(File.ReadAllBytes(expected).AsSpan().SequenceEqual(bytes), "c0000 diff3 differs from git's");
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
