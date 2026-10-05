using System.Text;

namespace NRMerge.Tests;

/// <summary>Writes a minimal, valid .7z archive (one folder, "Copy" coder = stored, no compression) so archive tests need no
/// 7-Zip install. Layout per the 7z format: signature header, packed stream, then the (unencoded) header.</summary>
public static class SevenZipFixture
{
    public static void Write(string path, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        var packed = files.SelectMany(f => f.Data).ToArray();
        var h = new MemoryStream();
        void B(int b) => h.WriteByte((byte)b);
        void N(ulong v) => Number(h, v);

        B(0x01);                       // kHeader
        B(0x04);                       // kMainStreamsInfo
        B(0x06); N(0); N(1);           // kPackInfo: packPos 0, 1 pack stream
        B(0x09); N((ulong)packed.Length); B(0x00);
        B(0x07);                       // kUnPackInfo
        B(0x0B); N(1); B(0x00);        // kFolder: 1 folder, not external
        N(1); B(0x01); B(0x00);        // 1 coder: id size 1, simple; id 00 = Copy
        B(0x0C); N((ulong)packed.Length);
        B(0x00);
        B(0x08);                       // kSubStreamsInfo
        if (files.Count != 1) { B(0x0D); N((ulong)files.Count); }
        if (files.Count > 1) { B(0x09); foreach (var f in files.Take(files.Count - 1)) N((ulong)f.Data.Length); }
        B(0x00);
        B(0x00);                       // end of streams info
        B(0x05); N((ulong)files.Count); // kFilesInfo
        var names = new MemoryStream();
        names.WriteByte(0);            // not external
        foreach (var f in files)
        {
            var n = Encoding.Unicode.GetBytes(f.Name);
            names.Write(n);
            names.WriteByte(0); names.WriteByte(0);
        }
        B(0x11); N((ulong)names.Length); h.Write(names.ToArray());
        B(0x00);                       // end of files info
        B(0x00);                       // end of header
        var header = h.ToArray();

        var start = new byte[20];
        BitConverter.GetBytes((ulong)packed.Length).CopyTo(start, 0);   // next header offset (after the packed data)
        BitConverter.GetBytes((ulong)header.Length).CopyTo(start, 8);
        BitConverter.GetBytes(Crc32(header)).CopyTo(start, 16);
        using var o = File.Create(path);
        o.Write(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04 });
        o.Write(BitConverter.GetBytes(Crc32(start)));
        o.Write(start);
        o.Write(packed);
        o.Write(header);
    }

    static void Number(Stream s, ulong v)
    {
        for (int i = 0; i < 8; i++)
            if (v < 1UL << (7 * (i + 1)))
            {
                int first = (0xFF00 >> i) & 0xFF;                 // i leading one bits
                s.WriteByte((byte)(first | (int)(v >> (8 * i))));
                for (int k = 0; k < i; k++) s.WriteByte((byte)(v >> (8 * k)));
                return;
            }
        s.WriteByte(0xFF);
        s.Write(BitConverter.GetBytes(v));
    }

    static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>Every file under <paramref name="dir"/>, named relative to <paramref name="baseDir"/> with '/' separators.</summary>
    public static List<(string, byte[])> FilesOf(string dir, string baseDir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(baseDir, f).Replace('\\', '/'), File.ReadAllBytes(f))).ToList();
}
