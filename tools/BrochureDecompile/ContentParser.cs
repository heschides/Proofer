using System.Globalization;
using System.Text;

namespace Sati.Tools.BrochureDecompile;

/// <summary>One drawing operation, already converted to SVG's top-left coordinate space.</summary>
internal sealed class El
{
    public string Kind = "";          // rect | path | stroke | image | text | gradient
    public double X, Y, W, H, R;
    public string Color = "#000";
    public string Color2 = "#000";    // gradient end stop
    public double Alpha = 1;
    public string PathData = "";
    public string Stroke = "";        // set when the path is both filled and stroked (B/B*/b/b*)
    public double StrokeWidth;
    public int ObjNum;
    public string Font = "";
    public double Size;
    public string Text = "";
    public int GradientId;
    public double X1, Y1, X2, Y2;     // gradient vector, objectBoundingBox units
}

internal static class ContentParser
{
    private const double PageH = 540;

    private sealed class Seg
    {
        public char Op;               // m | l | c
        public double[] P = [];
    }

    /// <summary>
    /// Parses the subset of the PDF content-stream language ReportLab emits for these slides:
    /// colour, alpha, paths, rectangles, image placement and simple text runs. Anything outside
    /// that subset is ignored rather than guessed at.
    /// </summary>
    public static List<El> Parse(string s, Dictionary<string, double> alphas, Dictionary<string, int> xobjs)
    {
        var els = new List<El>();
        var toks = Tokenize(s);

        var stack = new List<double>();
        string fill = "#000000", stroke = "#000000";
        double alpha = 1, lineWidth = 1;
        var gsStack = new Stack<(string Fill, string Stroke, double Alpha, double[] Cm)>();
        double[] cm = [1, 0, 0, 1, 0, 0];

        var segs = new List<Seg>();
        var pendingRects = new List<El>();

        // text state
        double tx = 0, ty = 0, leading = 0, size = 12;
        string font = "";

        double Pop(int back) => stack.Count > back ? stack[stack.Count - 1 - back] : 0;
        void Clear() => stack.Clear();

        for (int i = 0; i < toks.Count; i++)
        {
            string t = toks[i];

            if (t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '.'))
            {
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                { stack.Add(d); continue; }
            }
            if (t.StartsWith('/') || t.StartsWith('(')) { continue; }

            switch (t)
            {
                case "q":
                    gsStack.Push((fill, stroke, alpha, (double[])cm.Clone()));
                    Clear();
                    break;
                case "Q":
                    if (gsStack.Count > 0) { var g = gsStack.Pop(); fill = g.Fill; stroke = g.Stroke; alpha = g.Alpha; cm = g.Cm; }
                    Clear();
                    break;
                case "cm":
                    if (stack.Count >= 6) cm = [Pop(5), Pop(4), Pop(3), Pop(2), Pop(1), Pop(0)];
                    Clear();
                    break;
                case "gs":
                {
                    string name = PrevName(toks, i);
                    if (name != "" && alphas.TryGetValue(name, out double a)) alpha = a;
                    Clear();
                    break;
                }
                case "rg":
                    if (stack.Count >= 3) fill = Hex(Pop(2), Pop(1), Pop(0));
                    Clear();
                    break;
                case "RG":
                    if (stack.Count >= 3) stroke = Hex(Pop(2), Pop(1), Pop(0));
                    Clear();
                    break;
                case "w":
                    if (stack.Count >= 1) lineWidth = Pop(0);
                    Clear();
                    break;
                case "re":
                    if (stack.Count >= 4)
                    {
                        double x = Pop(3), y = Pop(2), w = Pop(1), h = Pop(0);
                        pendingRects.Add(new El { Kind = "rect", X = x, Y = PageH - y - h, W = w, H = h });
                    }
                    Clear();
                    break;
                case "m":
                    segs.Add(new Seg { Op = 'm', P = [Pop(1), Pop(0)] });
                    Clear();
                    break;
                case "l":
                    segs.Add(new Seg { Op = 'l', P = [Pop(1), Pop(0)] });
                    Clear();
                    break;
                case "c":
                    segs.Add(new Seg { Op = 'c', P = [Pop(5), Pop(4), Pop(3), Pop(2), Pop(1), Pop(0)] });
                    Clear();
                    break;
                case "h":
                    segs.Add(new Seg { Op = 'h' });
                    Clear();
                    break;
                case "n":
                    segs.Clear(); pendingRects.Clear(); Clear();
                    break;
                case "f":
                case "f*":
                    foreach (var r in pendingRects) { r.Color = fill; r.Alpha = alpha; els.Add(r); }
                    pendingRects.Clear();
                    if (segs.Count > 0) els.Add(FromPath(segs, fill, alpha, false, 0));
                    segs.Clear();
                    Clear();
                    break;
                case "S":
                    if (segs.Count > 0) els.Add(FromPath(segs, stroke, alpha, true, lineWidth));
                    segs.Clear(); pendingRects.Clear();
                    Clear();
                    break;
                // Fill AND stroke. ReportLab uses B* for the callout plates: a white box with a
                // pale border sitting over a screenshot. Treating these as unknown drops the box
                // and leaves whatever is behind it showing through.
                case "B":
                case "B*":
                case "b":
                case "b*":
                {
                    if (t[0] == 'b') segs.Add(new Seg { Op = 'h' });
                    foreach (var r in pendingRects)
                    {
                        r.Color = fill; r.Alpha = alpha; r.Stroke = stroke; r.StrokeWidth = lineWidth;
                        els.Add(r);
                    }
                    pendingRects.Clear();
                    if (segs.Count > 0)
                    {
                        var el = FromPath(segs, fill, alpha, false, 0);
                        el.Stroke = stroke;
                        el.StrokeWidth = lineWidth;
                        els.Add(el);
                    }
                    segs.Clear();
                    Clear();
                    break;
                }
                case "Do":
                {
                    string name = PrevName(toks, i);
                    if (name != "" && xobjs.TryGetValue(name, out int obj))
                    {
                        double w = cm[0], h = cm[3], e = cm[4], f = cm[5];
                        els.Add(new El { Kind = "image", ObjNum = obj, X = e, Y = PageH - f - h, W = w, H = h, Alpha = alpha });
                    }
                    Clear();
                    break;
                }
                case "Tm":
                    if (stack.Count >= 6) { tx = Pop(1); ty = Pop(0); }
                    Clear();
                    break;
                case "Td":
                case "TD":
                    if (stack.Count >= 2) { tx += Pop(1); ty += Pop(0); }
                    Clear();
                    break;
                case "TL":
                    if (stack.Count >= 1) leading = Pop(0);
                    Clear();
                    break;
                case "Tf":
                {
                    if (stack.Count >= 1) size = Pop(0);
                    font = PrevName(toks, i);
                    Clear();
                    break;
                }
                case "Tj":
                {
                    string lit = PrevLiteral(toks, i);
                    if (lit.Trim().Length > 0)
                        els.Add(new El { Kind = "text", X = tx, Y = PageH - ty, Size = size, Font = font, Color = fill, Alpha = alpha, Text = lit });
                    Clear();
                    break;
                }
                case "T*":
                    ty -= leading;
                    Clear();
                    break;
                default:
                    // An unrecognised operator must not leave a half-built path behind, or the
                    // next paint operator will emit someone else's geometry.
                    segs.Clear(); pendingRects.Clear();
                    Clear();
                    break;
            }
        }
        return els;
    }

    private static string PrevName(List<string> toks, int i, int back = 1)
    {
        for (int k = i - 1, seen = 0; k >= 0 && k > i - 8; k--)
            if (toks[k].StartsWith('/') && ++seen == back) return toks[k][1..];
        return "";
    }

    private static string PrevLiteral(List<string> toks, int i)
    {
        for (int k = i - 1; k >= 0 && k > i - 4; k--)
            if (toks[k].StartsWith('(')) return Unescape(toks[k][1..^1]);
        return "";
    }

    private static El FromPath(List<Seg> segs, string color, double alpha, bool isStroke, double width)
    {
        // ReportLab draws a rounded rectangle as m + (l,c) x4 + h. Recognising that shape keeps
        // the generated source editable instead of burying panels in bezier soup.
        int lines = segs.Count(s => s.Op == 'l'), curves = segs.Count(s => s.Op == 'c');
        if (!isStroke && lines == 4 && curves == 4 && segs.Any(s => s.Op == 'h'))
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var s in segs)
                for (int k = 0; k + 1 < s.P.Length; k += 2) { xs.Add(s.P[k]); ys.Add(s.P[k + 1]); }
            double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
            var start = segs.First(s => s.Op == 'm');
            double r = Math.Round(Math.Abs(start.P[0] - minX), 3);
            if (r > 0.5)
                return new El
                {
                    Kind = "rect", X = minX, Y = PageH - maxY, W = maxX - minX, H = maxY - minY,
                    R = r, Color = color, Alpha = alpha
                };
        }

        var d = new StringBuilder();
        foreach (var s in segs)
        {
            switch (s.Op)
            {
                case 'm': d.Append($"M{N(s.P[0])} {N(PageH - s.P[1])} "); break;
                case 'l': d.Append($"L{N(s.P[0])} {N(PageH - s.P[1])} "); break;
                case 'c': d.Append($"C{N(s.P[0])} {N(PageH - s.P[1])} {N(s.P[2])} {N(PageH - s.P[3])} {N(s.P[4])} {N(PageH - s.P[5])} "); break;
                case 'h': d.Append('Z'); break;
            }
        }
        return new El { Kind = isStroke ? "stroke" : "path", PathData = d.ToString().Trim(), Color = color, Alpha = alpha, W = width };
    }

    /// <summary>
    /// ReportLab fakes a gradient with dozens of near-identical stripes. Collapse each run back
    /// into a single linear gradient so the source says what it means.
    /// </summary>
    public static List<El> CollapseGradients(List<El> els)
    {
        var outp = new List<El>();
        int gid = 0;
        int i = 0;
        while (i < els.Count)
        {
            int run = RunLength(els, i, out bool vertical);
            if (run >= 8)
            {
                var first = els[i];
                var last = els[i + run - 1];
                double minX = Math.Min(first.X, last.X), minY = Math.Min(first.Y, last.Y);
                double maxX = Math.Max(first.X + first.W, last.X + last.W);
                double maxY = Math.Max(first.Y + first.H, last.Y + last.H);
                // "first" is the stripe drawn first; in SVG space that is the lower/left edge.
                bool firstIsLower = first.Y > last.Y || first.X < last.X;
                outp.Add(new El
                {
                    Kind = "gradient", GradientId = ++gid,
                    X = minX, Y = minY, W = maxX - minX, H = maxY - minY,
                    Color = first.Color, Color2 = last.Color, Alpha = first.Alpha,
                    X1 = vertical ? 0 : (firstIsLower ? 0 : 1),
                    Y1 = vertical ? (firstIsLower ? 1 : 0) : 0,
                    X2 = vertical ? 0 : (firstIsLower ? 1 : 0),
                    Y2 = vertical ? (firstIsLower ? 0 : 1) : 0
                });
                i += run;
                continue;
            }
            outp.Add(els[i]);
            i++;
        }
        return outp;
    }

    private static int RunLength(List<El> els, int start, out bool vertical)
    {
        vertical = true;
        if (els[start].Kind != "rect" || els[start].R > 0) return 0;
        int n = 1;
        var a = els[start];
        while (start + n < els.Count)
        {
            var b = els[start + n];
            if (b.Kind != "rect" || b.R > 0) break;
            bool sameCol = Math.Abs(b.X - a.X) < 0.01 && Math.Abs(b.W - a.W) < 0.01;
            bool sameRow = Math.Abs(b.Y - a.Y) < 0.01 && Math.Abs(b.H - a.H) < 0.01;
            if (!sameCol && !sameRow) break;
            if (n == 1) vertical = sameCol;
            if (vertical != sameCol) break;
            n++;
        }
        return n;
    }

    private static string N(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);

    private static string Hex(double r, double g, double b) =>
        $"#{(int)Math.Round(r * 255):x2}{(int)Math.Round(g * 255):x2}{(int)Math.Round(b * 255):x2}";

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\') { sb.Append(s[i]); continue; }
            if (++i >= s.Length) break;
            char c = s[i];
            if (c is >= '0' and <= '7')
            {
                int v = 0, k = 0;
                while (k < 3 && i < s.Length && s[i] is >= '0' and <= '7') { v = v * 8 + (s[i] - '0'); i++; k++; }
                i--;
                sb.Append((char)v);
            }
            else sb.Append(c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => c });
        }
        return sb.ToString();
    }

    private static List<string> Tokenize(string s)
    {
        var toks = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(')
            {
                int depth = 1, j = i + 1;
                var sb = new StringBuilder("(");
                while (j < s.Length && depth > 0)
                {
                    if (s[j] == '\\') { sb.Append(s[j]); if (j + 1 < s.Length) sb.Append(s[j + 1]); j += 2; continue; }
                    if (s[j] == '(') depth++;
                    if (s[j] == ')') { depth--; if (depth == 0) break; }
                    sb.Append(s[j]); j++;
                }
                sb.Append(')');
                toks.Add(sb.ToString());
                i = j + 1;
                continue;
            }
            int e = i;
            while (e < s.Length && !char.IsWhiteSpace(s[e]) && s[e] != '(') e++;
            toks.Add(s[i..e]);
            i = e;
        }
        return toks;
    }
}
