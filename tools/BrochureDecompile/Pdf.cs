using System.IO.Compression;
using System.Text;

namespace Sati.Tools.BrochureDecompile;

/// <summary>Just enough PDF plumbing to read a ReportLab-generated file: ASCII85 and Flate.</summary>
internal static class Pdf
{
    public static byte[] A85Decode(byte[] data)
    {
        var outp = new MemoryStream();
        uint tuple = 0;
        int count = 0;
        foreach (byte t in data)
        {
            char c = (char)t;
            if (c == '~') break;
            if (char.IsWhiteSpace(c)) continue;
            if (c == 'z' && count == 0) { outp.Write(new byte[4], 0, 4); continue; }
            if (c < '!' || c > 'u') continue;
            tuple = tuple * 85 + (uint)(c - '!');
            if (++count != 5) continue;
            outp.WriteByte((byte)(tuple >> 24));
            outp.WriteByte((byte)(tuple >> 16));
            outp.WriteByte((byte)(tuple >> 8));
            outp.WriteByte((byte)tuple);
            tuple = 0;
            count = 0;
        }
        if (count > 0)
        {
            for (int i = count; i < 5; i++) tuple = tuple * 85 + 84;
            outp.Write([(byte)(tuple >> 24), (byte)(tuple >> 16), (byte)(tuple >> 8), (byte)tuple], 0, count - 1);
        }
        return outp.ToArray();
    }

    public static byte[] Inflate(byte[] raw)
    {
        using var ms = new MemoryStream(raw);
        using var z = new ZLibStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    /// <summary>
    /// Indexes "N 0 obj ... endobj" spans. Matches that fall inside an already-claimed object are
    /// skipped, so digit runs inside stream data cannot be mistaken for object headers.
    /// </summary>
    public static Dictionary<int, (int Start, int End)> IndexObjects(string latin1)
    {
        var rx = new System.Text.RegularExpressions.Regex(@"(?<=[\r\n>\]\s])(\d+)\s+(\d+)\s+obj\b");
        var objs = new Dictionary<int, (int, int)>();
        int lastEnd = 0;
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(latin1))
        {
            if (m.Index < lastEnd) continue;
            int end = latin1.IndexOf("endobj", m.Index, StringComparison.Ordinal);
            if (end < 0) continue;
            objs[int.Parse(m.Groups[1].Value)] = (m.Index + m.Length, end);
            lastEnd = end + 6;
        }
        return objs;
    }

    public static byte[] DecodeStream(byte[] file, string latin1, (int Start, int End) span)
    {
        string body = latin1.Substring(span.Start, span.End - span.Start);
        int si = body.IndexOf("stream", StringComparison.Ordinal);
        if (si < 0) return [];
        int ds = si + 6;
        if (body[ds] == '\r') ds++;
        if (body[ds] == '\n') ds++;
        int de = body.LastIndexOf("endstream", StringComparison.Ordinal);
        int abs = span.Start + ds;
        int len = span.Start + de - abs;
        if (len <= 0) return [];
        var raw = new byte[len];
        Array.Copy(file, abs, raw, 0, len);
        if (body.Contains("ASCII85Decode")) raw = A85Decode(raw);
        if (body.Contains("FlateDecode")) raw = Inflate(raw);
        return raw;
    }

    public static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);
}
