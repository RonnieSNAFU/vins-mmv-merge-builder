using SoulsFormats;

namespace NRMerge;

/// <summary>Section-level three-way merge of FLVER2 models (header, dummies, bones, materials+GX lists, layouts, meshes).</summary>
public static class FlverMerge
{
    static readonly (string name, Func<FLVER2, object> get, Action<FLVER2, FLVER2> take)[] Sections =
    {
        ("header", f => f.Header, (dst, src) => dst.Header = src.Header),
        ("dummies", f => f.Dummies, (dst, src) => dst.Dummies = src.Dummies),
        ("bones", f => f.Nodes, (dst, src) => dst.Nodes = src.Nodes),
        ("materials", f => (f.Materials, f.GXLists), (dst, src) => { dst.Materials = src.Materials; dst.GXLists = src.GXLists; }),
        ("meshes", f => (f.BufferLayouts, f.Meshes), (dst, src) => { dst.BufferLayouts = src.BufferLayouts; dst.Meshes = src.Meshes; }),
    };

    /// <summary>
    /// Takes each section MMV changed and EV did not into EV's model. Returns null when a section was changed by both
    /// (or when meshes and materials come from different sides while the material count differs).
    /// </summary>
    public static FLVER2 MergeSections(FLVER2 b, FLVER2 e, FLVER2 m, out List<string> notes)
    {
        notes = new List<string>();
        var take = new List<int>();
        for (int i = 0; i < Sections.Length; i++)
        {
            var (name, get, _) = Sections[i];
            string sb = ObjMerge.Sig(get(b)), se = ObjMerge.Sig(get(e)), sm = ObjMerge.Sig(get(m));
            if (sm == sb || sm == se) continue;
            if (se != sb && name == "dummies")
            {
                // dummy polys are looked up by reference ID, not index: union (EV's first where both add the same spot)
                e.Dummies = Seq3.Merge(b.Dummies, e.Dummies, m.Dummies, d => ObjMerge.Sig(d), out var cs);
                notes.Add($"dummies: both changed, united ({e.Dummies.Count}; {cs.Count} insertion hunk(s) EV-then-MMV)");
                continue;
            }
            if (se != sb) { notes.Add($"{name}: changed by both"); return null; }
            take.Add(i);
        }
        bool mats = take.Contains(3), meshes = take.Contains(4);
        if (mats != meshes && e.Materials.Count != m.Materials.Count) { notes.Add("materials and meshes from different sides with different material counts"); return null; }
        foreach (var i in take) { Sections[i].take(e, m); notes.Add($"{Sections[i].name}: MMV's"); }
        return e;
    }

    /// <summary>Entry-merge adapter for binders: merged FLVER bytes, or null to fall back to the owner rule.</summary>
    public static byte[] Merge(byte[] b, byte[] e, byte[] m, out List<string> notes)
    {
        var r = MergeSections(FLVER2.Read(b), FLVER2.Read(e), FLVER2.Read(m), out notes);
        if (r == null) return null;
        var bytes = r.Write();
        FLVER2.Read(bytes);
        return bytes;
    }
}
