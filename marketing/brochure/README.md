# Workflow promotional brochure

`brochure.html` is the source of truth for
`output/pdf/Sati_Workflow_Promotional_Brochure.pdf`. Edit the HTML, run the build, review
the PDF. Nothing else regenerates it.

Before 2026-08-22 the PDF was a ReportLab output whose generator script had been lost, so
every change meant editing the binary in place. `tools/BrochureDecompile` was written once
to recover that file into this source; see `DECISIONS.md` for why the recovered form is
HTML+SVG rather than a new generator script.

## Building

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-brochure.ps1
```

Add `-Publish` to also copy the result into the OneDrive `Marketing` folder that the sales
copies live in. Without it, nothing outside `output/` is touched.

## Viewing it on screen

Opening `brochure.html` straight from Explorer works: the browser resolves `assets/…`
relative to the file and every screenshot appears.

Some viewers do not. Anything that loads the page from a `data:` URL or a sandboxed frame —
including the Claude Code browser pane — cannot resolve those relative paths, so every slide
renders with its text and shapes but **no screenshots at all**. That looks like missing
artwork; it is missing path context. For those, build a self-contained copy:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-brochure.ps1 -Preview
```

That writes `output/pdf/brochure-preview.html` with every screenshot inlined as a data URI —
around 6 MB, viewable anywhere, and safe to hand to someone who just wants to look. It is
generated on every run; never edit it, and never treat it as the source.

Images are deliberately *not* inlined into `brochure.html` itself. Several megabytes of
base64 in the middle of the source would make the file unopenable in an editor and unusable
for the hand-editing it exists to support.

The renderer is headless Edge (or Chrome if installed). Text stays real text: the build
embeds Segoe UI, Segoe UI Bold and Georgia Italic as subsets with `ToUnicode` maps, so the
PDF remains selectable and searchable.

## How a slide is laid out

Each slide is one `<svg class="slide" viewBox="0 0 960 540">`. That is the PDF's own
coordinate system in points, so a number here is the number the PDF gets — the deck is
960x540pt, or 16:9 at slide scale.

The only difference from PDF coordinates is that **y runs downward from the top**, as SVG
requires. A caption 56pt above the bottom edge is `y="484"`.

- `<text x y>` positions by **baseline**, matching how the original PDF placed type.
- `<rect rx>` is a panel; the decompiler recognised ReportLab's rounded-rectangle bezier
  runs and turned them back into real rectangles.
- `<linearGradient>` replaces the 40-to-80 stacked stripes ReportLab used to fake each
  background wash. Two stops now say what dozens of rectangles used to.
- `<image>` uses `preserveAspectRatio="none"` because the source PDF stretched images to
  their placement box; keep it unless you have re-cropped the asset to match.

To move something, change its coordinates. There is no layout engine to fight.

## Assets

`assets/img-NN.jpg|png` are the images recovered from the original PDF, named for the PDF
object they came from. Most are product screenshots. `img-04.png` and `img-35.png` are the
watercolour bodhi leaf with alpha.

`slide1-backdrop.jpg` is the exception and is worth knowing about. Slide 1's background was
originally a screenshot of the Sati login screen on its desktop wallpaper, which meant the
leaf and the sign-in dialog were baked into the JPEG and could not be moved — the leaf ended
up wherever the wallpaper happened to put it, hard against the panel edge. `slide1-backdrop.jpg`
is that plate with both removed by inpainting, so it is now only the gradient. Regenerate it
with:

```powershell
dotnet run --project tools/BrochureBackdrop -- <original-cover.jpg> marketing/brochure/assets/slide1-backdrop.jpg
```

The leaf is now placed by slide 1 itself, centred in the space right of the panel. That
centring is two numbers in the markup, not a property of the artwork.

## Limits worth knowing

- SVG `<text>` does not wrap. Multi-line copy is one `<text>` per line, which is what the
  original did too. If a line gets longer, check it still clears the panel edge.
- Only the fonts installed on the build machine are available. All three current faces ship
  with Windows.
- The decompiler was written for this one file and handles the subset of the PDF content
  stream language ReportLab emitted. It is kept for provenance, not as a general tool, and
  should not be pointed at arbitrary PDFs.
