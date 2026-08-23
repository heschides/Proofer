using System.Globalization;
using System.Text;
using Sati.Tools.BrochureDecompile;

// One-shot decompiler: turns the ReportLab brochure PDF into editable HTML + SVG source.
// Run once to seed marketing/brochure/. After that the HTML is the source of truth and this
// tool is kept only so the provenance of that file stays reproducible.
//
//   dotnet run --project tools/BrochureDecompile -- <input.pdf> <output-dir>

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: BrochureDecompile <input.pdf> <output-dir>");
    return 1;
}

string inPdf = args[0];
string outDir = args[1];
string assetDir = Path.Combine(outDir, "assets");
Directory.CreateDirectory(assetDir);

byte[] file = File.ReadAllBytes(inPdf);
string lat = Pdf.Latin1(file);
var objs = Pdf.IndexObjects(lat);

string Body(int n) => objs.TryGetValue(n, out var v) ? lat.Substring(v.Start, v.End - v.Start) : "";
byte[] Stream(int n) => objs.TryGetValue(n, out var v) ? Pdf.DecodeStream(file, lat, v) : [];

static string Rx(string src, string pattern, int group = 1)
{
    var m = System.Text.RegularExpressions.Regex.Match(src, pattern,
        System.Text.RegularExpressions.RegexOptions.Singleline);
    return m.Success ? m.Groups[group].Value : "";
}

// ------------------------------------------------------------------ images

var imageFile = new Dictionary<int, string>();

foreach (int n in objs.Keys.OrderBy(k => k))
{
    string b = Body(n);
    if (!b.Contains("/Subtype /Image")) continue;

    int w = int.Parse(Rx(b, @"/Width (\d+)"));
    int h = int.Parse(Rx(b, @"/Height (\d+)"));
    byte[] data = Stream(n);

    if (b.Contains("DCTDecode"))
    {
        string name = $"img-{n:D2}.jpg";
        File.WriteAllBytes(Path.Combine(assetDir, name), data);
        imageFile[n] = name;
        Console.WriteLine($"  asset {name,-14} {w}x{h} jpeg");
    }
    else if (b.Contains("/SMask"))
    {
        int smask = int.Parse(Rx(b, @"/SMask (\d+)"));
        string name = $"img-{n:D2}.png";
        WritePng(Path.Combine(assetDir, name), w, h, data, Stream(smask));
        imageFile[n] = name;
        Console.WriteLine($"  asset {name,-14} {w}x{h} rgba");
    }
}

// ------------------------------------------------------------------ fonts

var fonts = new Dictionary<string, (string Css, string Weight, string Style)>();
foreach (int n in objs.Keys)
{
    string b = Body(n);
    if (!b.Contains("/Type /Font")) continue;
    string name = Rx(b, @"/Name /([^\s/]+)");
    string baseFont = Rx(b, @"/BaseFont /([^\s/]+)");
    if (name.Length == 0) continue;
    fonts[name] = (
        baseFont.Contains("Georgia") ? "Georgia, 'Times New Roman', serif" : "'Segoe UI', system-ui, sans-serif",
        baseFont.Contains("Bold") ? "700" : "400",
        baseFont.Contains("Italic") ? "italic" : "normal");
}

// ------------------------------------------------------------------ pages

string kidsRaw = Rx(lat, @"/Kids\s*\[([^\]]*)\]");
var pageObjs = System.Text.RegularExpressions.Regex.Matches(kidsRaw, @"(\d+)\s+0\s+R")
    .Select(m => int.Parse(m.Groups[1].Value)).ToList();

var html = new StringBuilder();
EmitHeader(html);

for (int pi = 0; pi < pageObjs.Count; pi++)
{
    string pb = Body(pageObjs[pi]);

    var alphas = new Dictionary<string, double>();
    foreach (System.Text.RegularExpressions.Match m in
             System.Text.RegularExpressions.Regex.Matches(pb, @"/(gRLs\d+)\s*<<\s*/ca\s*([\d.]+)"))
        alphas[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

    var xobjs = new Dictionary<string, int>();
    foreach (System.Text.RegularExpressions.Match m in
             System.Text.RegularExpressions.Regex.Matches(Rx(pb, @"/XObject\s*<<(.*?)>>"), @"/(\S+)\s+(\d+)\s+0\s+R"))
        xobjs[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

    int contents = int.Parse(Rx(pb, @"/Contents (\d+) 0 R"));
    var els = ContentParser.CollapseGradients(
        ContentParser.Parse(Pdf.Latin1(Stream(contents)), alphas, xobjs));

    Console.WriteLine($"page {pi + 1,2}: {els.Count} elements");
    EmitPage(html, pi + 1, els, imageFile, fonts);
}

html.AppendLine("</body>");
File.WriteAllText(Path.Combine(outDir, "brochure.html"), html.ToString(), new UTF8Encoding(false));
Console.WriteLine($"wrote {Path.Combine(outDir, "brochure.html")}");
return 0;

// ------------------------------------------------------------------ helpers

static void WritePng(string path, int w, int h, byte[] rgb, byte[] alpha)
{
#pragma warning disable CA1416
    using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
        System.Drawing.Imaging.ImageLockMode.WriteOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var buf = new byte[bd.Stride * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int s = (y * w + x) * 3;
            int i = y * bd.Stride + x * 4;
            buf[i + 2] = rgb[s];
            buf[i + 1] = rgb[s + 1];
            buf[i] = rgb[s + 2];
            buf[i + 3] = alpha.Length == w * h ? alpha[y * w + x] : (byte)255;
        }
    System.Runtime.InteropServices.Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
    bmp.UnlockBits(bd);
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
#pragma warning restore CA1416
}

static string F(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);

static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

static void EmitHeader(StringBuilder h)
{
    h.Append("<!doctype html>\n<meta charset=\"utf-8\">\n");
    h.Append("<title>Sati — Workflow Promotional Brochure</title>\n");
    h.Append("<!--\n");
    h.Append("  SOURCE OF TRUTH for output/pdf/Sati_Workflow_Promotional_Brochure.pdf.\n");
    h.Append("  Each slide is one 960x540pt <svg> using the PDF's own coordinate system, so a number\n");
    h.Append("  here is the number the PDF gets. Text is real text and stays selectable.\n");
    h.Append("  Build: powershell -ExecutionPolicy Bypass -File scripts/build-brochure.ps1\n");
    h.Append("-->\n");
    h.Append("<style>\n");
    h.Append("  @page { size: 960pt 540pt; margin: 0; }\n");
    h.Append("  html, body { margin: 0; padding: 0; background: #fff; }\n");
    h.Append("  .slide { display: block; width: 960pt; height: 540pt; break-after: page; }\n");
    h.Append("  .slide:last-of-type { break-after: auto; }\n");
    h.Append("  text { white-space: pre; }\n");
    h.Append("  @media screen {\n");
    h.Append("    body { background: #45464e; padding: 24px; }\n");
    h.Append("    .slide { margin: 0 auto 24px; box-shadow: 0 6px 28px rgba(0,0,0,.45); background: #fff; }\n");
    h.Append("  }\n");
    h.Append("</style>\n<body>\n");
}

static void EmitPage(StringBuilder h, int page, List<El> els,
    Dictionary<int, string> imageFile,
    Dictionary<string, (string Css, string Weight, string Style)> fonts)
{
    h.Append($"\n<!-- =============== slide {page} =============== -->\n");
    h.Append($"<svg class=\"slide\" viewBox=\"0 0 960 540\" xmlns=\"http://www.w3.org/2000/svg\" data-slide=\"{page}\">\n");

    var grads = els.Where(e => e.Kind == "gradient").ToList();
    if (grads.Count > 0)
    {
        h.Append("  <defs>\n");
        foreach (var g in grads)
        {
            h.Append($"    <linearGradient id=\"g{page}-{g.GradientId}\" x1=\"{F(g.X1)}\" y1=\"{F(g.Y1)}\" x2=\"{F(g.X2)}\" y2=\"{F(g.Y2)}\">\n");
            h.Append($"      <stop offset=\"0\" stop-color=\"{g.Color}\"/>\n");
            h.Append($"      <stop offset=\"1\" stop-color=\"{g.Color2}\"/>\n");
            h.Append("    </linearGradient>\n");
        }
        h.Append("  </defs>\n");
    }

    foreach (var e in els)
    {
        string op = e.Alpha < 0.999 ? $" opacity=\"{F(e.Alpha)}\"" : "";
        switch (e.Kind)
        {
            case "gradient":
                h.Append($"  <rect x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.W)}\" height=\"{F(e.H)}\" fill=\"url(#g{page}-{e.GradientId})\"{op}/>\n");
                break;
            case "rect":
                string rx = e.R > 0 ? $" rx=\"{F(e.R)}\"" : "";
                string rstroke = e.Stroke.Length > 0 ? $" stroke=\"{e.Stroke}\" stroke-width=\"{F(e.StrokeWidth)}\"" : "";
                h.Append($"  <rect x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.W)}\" height=\"{F(e.H)}\"{rx} fill=\"{e.Color}\"{rstroke}{op}/>\n");
                break;
            case "path":
                string pstroke = e.Stroke.Length > 0 ? $" stroke=\"{e.Stroke}\" stroke-width=\"{F(e.StrokeWidth)}\"" : "";
                h.Append($"  <path d=\"{e.PathData}\" fill=\"{e.Color}\"{pstroke}{op}/>\n");
                break;
            case "stroke":
                h.Append($"  <path d=\"{e.PathData}\" fill=\"none\" stroke=\"{e.Color}\" stroke-width=\"{F(e.W)}\"{op}/>\n");
                break;
            case "image":
                if (!imageFile.TryGetValue(e.ObjNum, out string f)) break;
                h.Append($"  <image href=\"assets/{f}\" x=\"{F(e.X)}\" y=\"{F(e.Y)}\" width=\"{F(e.W)}\" height=\"{F(e.H)}\" preserveAspectRatio=\"none\"{op}/>\n");
                break;
            case "text":
                (string Css, string Weight, string Style) ff = fonts.TryGetValue(e.Font, out var got)
                    ? got
                    : ("'Segoe UI', system-ui, sans-serif", "400", "normal");
                string italic = ff.Style == "italic" ? " font-style=\"italic\"" : "";
                h.Append($"  <text x=\"{F(e.X)}\" y=\"{F(e.Y)}\" font-family=\"{ff.Css}\" font-size=\"{F(e.Size)}\" font-weight=\"{ff.Weight}\"{italic} fill=\"{e.Color}\"{op}>{Esc(e.Text)}</text>\n");
                break;
        }
    }
    h.Append("</svg>\n");
}
