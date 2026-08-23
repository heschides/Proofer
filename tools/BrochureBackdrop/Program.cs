using System.Drawing;
using System.Drawing.Imaging;

#pragma warning disable CA1416

// Produces the slide-1 backdrop for marketing/brochure from the original cover art.
//
// The brochure's first slide originally used a screenshot of the Sati login screen as its
// full-bleed background, which meant the bodhi leaf and the sign-in dialog were baked into
// the JPEG and could not be positioned. This tool removes both and leaves only the gradient,
// so brochure.html can place the leaf as a real element with real coordinates.
//
// Removal is a multigrid Laplace inpaint over the masked region. That is only sound because
// the underlying wallpaper is a smooth gradient; do not reuse this on textured art.
//
//   dotnet run --project tools/BrochureBackdrop -- <cover-with-leaf.jpg> <backdrop-out.jpg>

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: BrochureBackdrop <cover-with-leaf.jpg> <backdrop-out.jpg>");
    return 1;
}

const double PxPerPt = 2.2;      // the cover plate is 2112x1188 for a 960x540pt slide
const int DialogPadX0 = 650, DialogPadX1 = 1190, DialogPadY0 = 270, DialogPadY1 = 1075;

using var src = new Bitmap(args[0]);
int W = src.Width, H = src.Height;

var sd = src.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
int stride = sd.Stride;
var px = new byte[stride * H];
System.Runtime.InteropServices.Marshal.Copy(sd.Scan0, px, 0, px.Length);
src.UnlockBits(sd);
int At(int x, int y) => y * stride + x * 4;

// The leaf is the only strongly saturated thing on a pastel background, so the largest
// saturated connected component is it.
var saturated = new bool[W * H];
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        int i = At(x, y);
        int b = px[i], g = px[i + 1], r = px[i + 2];
        if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 40) saturated[y * W + x] = true;
    }

var label = new int[W * H];
int current = 0, best = 0, bestSize = 0;
var stack = new Stack<int>();
for (int s = 0; s < W * H; s++)
{
    if (!saturated[s] || label[s] != 0) continue;
    current++;
    int size = 0;
    stack.Push(s);
    label[s] = current;
    while (stack.Count > 0)
    {
        int p = stack.Pop();
        size++;
        int x = p % W, y = p / W;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                int q = ny * W + nx;
                if (saturated[q] && label[q] == 0) { label[q] = current; stack.Push(q); }
            }
    }
    if (size > bestSize) { bestSize = size; best = current; }
}

var mask = new bool[W * H];
int lx = W, rx = -1, ty = H, by = -1;
for (int s = 0; s < W * H; s++)
    if (label[s] == best)
    {
        mask[s] = true;
        int x = s % W, y = s / W;
        if (x < lx) lx = x;
        if (x > rx) rx = x;
        if (y < ty) ty = y;
        if (y > by) by = y;
    }

Console.WriteLine($"leaf removed from x {lx}..{rx}, y {ty}..{by}  ({rx - lx + 1}x{by - ty + 1}px)");
Console.WriteLine($"  in slide points: {(rx - lx + 1) / PxPerPt:F2} x {(by - ty + 1) / PxPerPt:F2} pt " +
                  $"at x {lx / PxPerPt:F2}, y {(H - 1 - by) / PxPerPt:F2} (from bottom)");

// The leaf's pale interior is not saturated, so flood the outside and keep whatever it cannot reach.
var outside = new bool[W * H];
var queue = new Queue<int>();
void Seed(int s) { if (!mask[s] && !outside[s]) { outside[s] = true; queue.Enqueue(s); } }
for (int x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
for (int y = 0; y < H; y++) { Seed(y * W); Seed(y * W + W - 1); }
while (queue.Count > 0)
{
    int p = queue.Dequeue();
    int x = p % W, y = p / W;
    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
    {
        int nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
        Seed(ny * W + nx);
    }
}
for (int s = 0; s < W * H; s++)
    if (!mask[s] && !outside[s]) mask[s] = true;

// Dilate so the leaf's soft watercolour edge goes with it.
const int R = 14;
var hole = new bool[W * H];
{
    var tmp = new bool[W * H];
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            bool v = false;
            for (int k = -R; k <= R && !v; k++) { int nx = x + k; if (nx >= 0 && nx < W && mask[y * W + nx]) v = true; }
            tmp[y * W + x] = v;
        }
    for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            bool v = false;
            for (int k = -R; k <= R && !v; k++) { int ny = y + k; if (ny >= 0 && ny < H && tmp[ny * W + x]) v = true; }
            hole[y * W + x] = v;
        }
}

// The sign-in dialog and its drop shadow are screenshot furniture; a backdrop should not carry them.
for (int y = DialogPadY0; y <= DialogPadY1 && y < H; y++)
    for (int x = DialogPadX0; x <= DialogPadX1 && x < W; x++)
        hole[y * W + x] = true;

Console.WriteLine($"inpainting {hole.Count(v => v):N0} px ({100.0 * hole.Count(v => v) / (W * H):F1}% of the plate)");

var channel = new float[3][];
for (int c = 0; c < 3; c++)
{
    channel[c] = new float[W * H];
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            channel[c][y * W + x] = px[At(x, y) + (2 - c)];
}

const int Levels = 6;
var sizes = new List<(int W, int H)> { (W, H) };
for (int l = 1; l < Levels; l++) sizes.Add((Math.Max(4, sizes[l - 1].W / 2), Math.Max(4, sizes[l - 1].H / 2)));

for (int c = 0; c < 3; c++)
{
    var data = new List<float[]> { channel[c] };
    var masks = new List<bool[]> { hole };

    for (int l = 1; l < Levels; l++)
    {
        var (pw, ph) = sizes[l - 1];
        var (w2, h2) = sizes[l];
        var d2 = new float[w2 * h2];
        var m2 = new bool[w2 * h2];
        for (int y = 0; y < h2; y++)
            for (int x = 0; x < w2; x++)
            {
                float sum = 0;
                int n = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(pw - 1, x * 2 + dx), sy = Math.Min(ph - 1, y * 2 + dy);
                        if (!masks[l - 1][sy * pw + sx]) { sum += data[l - 1][sy * pw + sx]; n++; }
                    }
                d2[y * w2 + x] = n > 0 ? sum / n : 0;
                m2[y * w2 + x] = n == 0;
            }
        data.Add(d2);
        masks.Add(m2);
    }

    for (int l = Levels - 1; l >= 0; l--)
    {
        var (w, h) = sizes[l];
        if (l < Levels - 1)
        {
            var (cw, ch) = sizes[l + 1];
            var coarse = data[l + 1];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = y * w + x;
                    if (!masks[l][s]) continue;
                    data[l][s] = coarse[Math.Min(ch - 1, y / 2) * cw + Math.Min(cw - 1, x / 2)];
                }
        }
        Relax(data[l], masks[l], w, h, l == 0 ? 60 : 200);
    }
    channel[c] = data[0];
}

using var outB = new Bitmap(W, H, PixelFormat.Format32bppArgb);
var od = outB.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
var ob = new byte[od.Stride * H];
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        int i = y * od.Stride + x * 4;
        ob[i + 2] = (byte)Math.Clamp(channel[0][y * W + x], 0, 255);
        ob[i + 1] = (byte)Math.Clamp(channel[1][y * W + x], 0, 255);
        ob[i] = (byte)Math.Clamp(channel[2][y * W + x], 0, 255);
        ob[i + 3] = 255;
    }
System.Runtime.InteropServices.Marshal.Copy(ob, 0, od.Scan0, ob.Length);
outB.UnlockBits(od);

var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
var parameters = new EncoderParameters(1);
parameters.Param[0] = new EncoderParameter(Encoder.Quality, 94L);
outB.Save(args[1], encoder, parameters);
Console.WriteLine($"wrote {args[1]}");
return 0;

static void Relax(float[] f, bool[] hole, int w, int h, int iterations)
{
    var next = new float[w * h];
    for (int it = 0; it < iterations; it++)
    {
        Array.Copy(f, next, f.Length);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = y * w + x;
                if (!hole[s]) continue;
                float sum = 0;
                int n = 0;
                if (x > 0) { sum += f[s - 1]; n++; }
                if (x < w - 1) { sum += f[s + 1]; n++; }
                if (y > 0) { sum += f[s - w]; n++; }
                if (y < h - 1) { sum += f[s + w]; n++; }
                next[s] = sum / n;
            }
        Array.Copy(next, f, f.Length);
    }
}
