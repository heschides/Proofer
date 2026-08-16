using Sati.Contracts.V1;
using Sati.Models;
using Sati.Reporting;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Pasted evidence clips: what the shared rule owner accepts, how the editor
/// counts them, and what reaches the generated document.
///
/// The count matters more than it looks. Publishing locks the request, so a clip
/// that silently failed to attach is cheap to notice beforehand and expensive
/// afterwards — correcting it means discarding the attestation and publishing
/// again.
/// </summary>
[Collection(PdfRenderingCollection.Name)]
public sealed class AtRequestScreenshotTests
{
    // Smallest thing that is genuinely a PNG: the 8-byte signature is what
    // AtRequestScreenshot actually inspects.
    private static byte[] PngBytes(int padding = 64) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[padding]];

    private static ATRequest NewRequest()
    {
        var person = Person.Rehydrate(1, 1);
        person.FirstName = "Test";
        person.LastName = "Client";
        var caseManager = User.Create(1, "cm", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 1);

        var request = ATRequest.CreateForClient(person, caseManager);
        request.VendorName = "Maine AT Solutions";
        request.VendorBillingLocation = "1992205249-007";
        return request;
    }

    private static ATRequestEditorViewModel NewEditor(ATRequest request) =>
        new(request, 0.15m, 0.055m, [], null);

    // ---- What the rule owner accepts ----

    [Fact]
    public void NoScreenshotIsAcceptable()
    {
        // An item without evidence is normal, not an error.
        Assert.Null(AtRequestScreenshot.Describe(null));
        Assert.Null(AtRequestScreenshot.Describe([]));
    }

    [Fact]
    public void APngIsAccepted()
    {
        Assert.True(AtRequestScreenshot.IsAcceptable(PngBytes()));
    }

    [Fact]
    public void SomethingThatIsNotAPngIsRefused()
    {
        // A JPEG's leading bytes, which would sail through a length-only check.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0];

        var problem = AtRequestScreenshot.Describe(jpeg);

        Assert.NotNull(problem);
        Assert.Contains("PNG", problem);
    }

    [Fact]
    public void AnOversizedScreenshotIsRefused()
    {
        var oversized = PngBytes(AtRequestScreenshot.MaxBytes + 1);

        var problem = AtRequestScreenshot.Describe(oversized);

        Assert.NotNull(problem);
        Assert.Contains("too large", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AShortBufferIsRefusedRatherThanReadPastItsEnd()
    {
        // Fewer bytes than the signature. The guard has to check length before
        // comparing, or this is an out-of-range read.
        Assert.False(AtRequestScreenshot.IsAcceptable([0x89, 0x50]));
    }

    // ---- Downscaling ----

    [Theory]
    [InlineData(800, 600, 800, 600)]                                  // already small enough
    [InlineData(2800, 1400, AtRequestScreenshot.MaxEdgePixels, 700)]   // landscape
    [InlineData(1400, 2800, 700, AtRequestScreenshot.MaxEdgePixels)]   // portrait
    public void ScalingPreservesAspectRatioAndOnlyShrinks(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), AtRequestScreenshot.ScaleToFit(width, height));
    }

    [Fact]
    public void ScalingNeverProducesAZeroDimension()
    {
        // A pathologically wide image would round its short edge to zero, and a
        // zero-dimension render target throws.
        var (_, height) = AtRequestScreenshot.ScaleToFit(20000, 3);

        Assert.True(height >= 1);
        Assert.Equal((0, 0), AtRequestScreenshot.ScaleToFit(0, 0));
    }

    // ---- The editor's count ----

    [Fact]
    public void TheEditorCountsPastedClips()
    {
        var editor = NewEditor(NewRequest());
        editor.AddItemCommand.Execute(null);
        editor.AddItemCommand.Execute(null);

        Assert.Equal(0, editor.ScreenshotCount);

        editor.Items[0].ScreenshotPng = PngBytes();

        Assert.Equal(1, editor.ScreenshotCount);
        Assert.Contains("1 of 2", editor.ScreenshotSummary);

        editor.Items[1].ScreenshotPng = PngBytes();

        Assert.Equal(2, editor.ScreenshotCount);
        Assert.Contains("every item", editor.ScreenshotSummary);
    }

    [Fact]
    public void RemovingAClipUpdatesTheCount()
    {
        var editor = NewEditor(NewRequest());
        editor.AddItemCommand.Execute(null);
        editor.Items[0].ScreenshotPng = PngBytes();

        editor.Items[0].ScreenshotPng = null;

        Assert.Equal(0, editor.ScreenshotCount);
        Assert.False(editor.Items[0].HasScreenshot);
    }

    [Fact]
    public void RemovingAnItemRemovesItsClipFromTheCount()
    {
        var editor = NewEditor(NewRequest());
        editor.AddItemCommand.Execute(null);
        editor.Items[0].ScreenshotPng = PngBytes();

        editor.RemoveItemCommand.Execute(editor.Items[0]);

        Assert.Equal(0, editor.ScreenshotCount);
    }

    [Fact]
    public void APublishedRequestRefusesAPastedClip()
    {
        var request = NewRequest();
        request.Items.Add(new ATRequestItem { Name = "Pie cutter", ItemCost = 25m, Quantity = 1 });
        var editor = NewEditor(request);
        request.Publish(
            User.Create(1, "cm", "Case Manager", string.Empty, string.Empty, UserRole.CaseManager, null, 1),
            DateTime.UtcNow, 0.15m);

        Assert.False(editor.CanEdit);
    }

    // ---- The generated document ----

    [Fact]
    public void TheEvidencePageIsOmittedWhenNothingWasPasted()
    {
        // Not an assertion about page count — MigraDoc does not expose that
        // cheaply — but the document must still render, and the exporter must not
        // trip over items with no clip.
        var request = NewRequest();
        request.Items.Add(new ATRequestItem { Name = "Pie cutter", ItemCost = 25m, Quantity = 1 });

        var pdf = new ATRequestPdfExporter().Generate(request, 0.15m, DateTime.UtcNow);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void ARealScreenshotIsEmbeddedInTheDocument()
    {
        // Rendering successfully is not evidence that the image arrived — an
        // exporter that silently skipped every clip would also render. So this
        // compares the same request with and without the screenshot and requires
        // the picture's worth of bytes to actually show up in the output.
        var withoutClip = NewRequest();
        withoutClip.Items.Add(new ATRequestItem
        {
            Name = "Pie cutter",
            ItemCost = 25m,
            Quantity = 1,
            Url = "https://example.test/pie-cutter"
        });

        var withClip = NewRequest();
        var screenshot = NoisyPng(240, 180);
        withClip.Items.Add(new ATRequestItem
        {
            Name = "Pie cutter",
            ItemCost = 25m,
            Quantity = 1,
            Url = "https://example.test/pie-cutter",
            ScreenshotPng = screenshot
        });

        var exporter = new ATRequestPdfExporter();
        var bare = exporter.Generate(withoutClip, 0.15m, DateTime.UtcNow);
        var documented = exporter.Generate(withClip, 0.15m, DateTime.UtcNow);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(documented, 0, 4));
        // Noise compresses poorly, so most of the screenshot's bytes survive into
        // the PDF. Half its size is a floor no amount of stream compression or
        // page furniture accounts for.
        Assert.True(
            documented.Length > bare.Length + (screenshot.Length / 2),
            $"Expected the embedded screenshot to enlarge the PDF. Bare {bare.Length}, " +
            $"documented {documented.Length}, screenshot {screenshot.Length} bytes.");
    }

    // An incompressible image, so its presence in the PDF is measurable. A flat
    // colour would compress to almost nothing and prove nothing.
    private static byte[] NoisyPng(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        new Random(20260815).NextBytes(pixels);

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null, pixels, width * 4);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void AnUndecodableScreenshotDoesNotCostTheWholeDocument()
    {
        // Bytes that pass the signature gate but are not a decodable image. The
        // exporter must report the gap on the page rather than throw, or one bad
        // row makes the request unpublishable.
        var request = NewRequest();
        request.Items.Add(new ATRequestItem
        {
            Name = "Corrupt clip",
            ItemCost = 25m,
            Quantity = 1,
            ScreenshotPng = PngBytes(200)
        });

        var pdf = new ATRequestPdfExporter().Generate(request, 0.15m, DateTime.UtcNow);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // A genuine 1x1 red PNG, so the renderer has something it can actually decode.
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
