using SoulsFormats;
using System.Text.RegularExpressions;

namespace NRMerge;

/// <summary>Copies an MMV weapon model to a new model ID (file, entry, texture and texture-path names) so it can coexist with EV's model.</summary>
public static class ModelClone
{
    public static string RenameModelRefs(string s, int oldId, int newId) =>
        s == null ? null : Regex.Replace(s, $@"(?i)(wp_a_){oldId:D4}(?!\d)", m => m.Groups[1].Value + newId.ToString("D4"));

    public static void Clone(string srcPartsPath, int oldId, int newId, string outPath)
    {
        var data = Dcx.Decompress(File.ReadAllBytes(srcPartsPath), out var type);
        var bnd = BND4.Read(data);
        foreach (var f in bnd.Files)
        {
            f.Name = RenameModelRefs(f.Name, oldId, newId);
            if (f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
            {
                var fl = FLVER2.Read(f.Bytes);
                foreach (var m in fl.Materials)
                    foreach (var t in m.Textures) t.Path = RenameModelRefs(t.Path, oldId, newId);
                f.Bytes = fl.Write();
            }
            else if (f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
            {
                var tpf = TPF.Read(f.Bytes);
                foreach (var t in tpf.Textures) t.Name = RenameModelRefs(t.Name, oldId, newId);
                f.Bytes = tpf.Write();
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, bnd.Write(type).ToArray());
    }
}
