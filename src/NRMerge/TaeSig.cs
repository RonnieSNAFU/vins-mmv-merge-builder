using SoulsFormats;
using System.Text;

namespace NRMerge;

/// <summary>Semantic signature of a TAE (everything the game reads), used to compare TAEs independent of byte layout.</summary>
public static class TaeSig
{
    public static string Header(TAE t) =>
        $"{t.Format}|{t.ID}|{Convert.ToHexString(t.Flags ?? Array.Empty<byte>())}|{t.SkeletonName}|{t.SibName}|{t.EventBank}|{t.BigEndian}";

    public static string MiniHeader(TAE.Animation a) => a.MiniHeader switch
    {
        TAE.Animation.AnimMiniHeader.Standard s => $"S:{s.IsLoopByDefault}:{s.ImportsHKX}:{s.AllowDelayLoad}:{s.ImportHKXSourceAnimID}",
        TAE.Animation.AnimMiniHeader.ImportOtherAnim i => $"I:{i.ImportFromAnimID}:{i.Unknown}",
        null => "null",
        var o => o.GetType().Name,
    };

    public static string Event(TAE.Event e, bool bigEndian) =>
        $"{e.Type}:{e.Unk04}:{e.StartTime:R}:{e.EndTime:R}:{Convert.ToHexString(e.GetParameterBytes(bigEndian))}:{e.Group?.GroupType}:{(e.Group == null ? "" : $"{e.Group.GroupData.DataType}/{e.Group.GroupData.CutsceneEntityType}/{e.Group.GroupData.CutsceneEntityIDPart1}/{e.Group.GroupData.CutsceneEntityIDPart2}")}";

    public static string Animation(TAE.Animation a, bool bigEndian)
    {
        var sb = new StringBuilder();
        sb.Append(a.ID).Append('|').Append(MiniHeader(a)).Append('|').Append(a.AnimFileName).Append('|');
        foreach (var e in a.Events) sb.Append(Event(e, bigEndian)).Append(';');
        return sb.ToString();
    }

    public static string Full(TAE t)
    {
        var sb = new StringBuilder(Header(t)).Append('\n');
        foreach (var a in t.Animations) sb.Append(Animation(a, t.BigEndian)).Append('\n');
        return sb.ToString();
    }
}
