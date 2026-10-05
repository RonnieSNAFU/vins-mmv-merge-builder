using SoulsFormats;
using System.Buffers.Binary;
using System.IO.Compression;

namespace NRMerge;

/// <summary>DCX helpers with a lenient fallback for DFLT variants SoulsFormats does not recognise.</summary>
public static class Dcx
{
    public static byte[] Decompress(byte[] data) => Decompress(data, out _);

    public static byte[] Decompress(byte[] data, out DCX.Type type)
    {
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual("DCX\0"u8)) { type = DCX.Type.None; return data; }
        try
        {
            var r = DCX.Decompress(data, out type);
            return r.ToArray();
        }
        catch (Exception)
        {
            // Manual parse: DCX header, DCS sizes, DCP format, DCA header length.
            int dcs = IndexOf(data, "DCS\0"u8);
            int dcp = IndexOf(data, "DCP\0"u8);
            int dca = IndexOf(data, "DCA\0"u8);
            if (dcs < 0 || dcp < 0 || dca < 0) throw;
            var fmt = System.Text.Encoding.ASCII.GetString(data, dcp + 4, 4);
            int usize = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dcs + 4));
            int csize = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dcs + 8));
            int dcaLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dca + 4));
            int start = dca + dcaLen;
            if (fmt != "DFLT") throw;
            using var ms = new MemoryStream(data, start, data.Length - start);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            var outp = new byte[usize];
            int read = 0;
            while (read < usize)
            {
                int n = z.Read(outp, read, usize - read);
                if (n == 0) break;
                read += n;
            }
            type = DCX.Type.DCX_DFLT_11000_44_9;
            return outp;
        }
    }

    static int IndexOf(byte[] d, ReadOnlySpan<byte> pat)
    {
        var i = d.AsSpan(0, Math.Min(d.Length, 0x100)).IndexOf(pat);
        return i;
    }
}
