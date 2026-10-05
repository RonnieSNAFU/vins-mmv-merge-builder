using NRMerge;
using Xunit;

/// <summary>Journal.Dir is process-wide (Paths.Use resets it): run with the other Paths tests, not alongside them.</summary>
[Collection("Paths")]
public class JournalTests
{
    [Fact]
    public void SaveWritesOneFilePerAreaAndOverwritesOnRerun()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nrmerge-journal-" + Guid.NewGuid().ToString("N"));
        Journal.Dir = dir;
        Journal.Clear();
        Journal.Add("regulation", "NpcParam 123 hp", "EV wins");
        Journal.Add("regulation", "NpcParam 124 hp", "EV wins");
        Journal.Save();
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir, "regulation.tsv")).Length); // header + 2

        Journal.Clear();
        Journal.Add("regulation", "NpcParam 999 hp", "MMV wins");
        Journal.Save();
        var lines = File.ReadAllLines(Path.Combine(dir, "regulation.tsv"));
        Assert.Equal(2, lines.Length);
        Assert.Contains("NpcParam 999 hp", lines[1]);
        Directory.Delete(dir, true);
    }
}
