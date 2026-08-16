using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Sati.Contracts.V1;
using Sati.ViewModels.Children;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for ATRequestView.xaml.
    ///
    /// Owns the three things a view model must not: the publish confirmation
    /// dialog, the save-file dialog, and rasterizing the rendered form. The view
    /// model raises events and consumes a byte-producing delegate; it never sees
    /// a Window, a Visual, or a MessageBox. Same split as AdminDashboardView.
    /// </summary>
    public partial class ATRequestView : UserControl
    {
        public ATRequestView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private ATRequestViewModel? _viewModel;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ATRequestViewModel viewModel || ReferenceEquals(viewModel, _viewModel))
                return;

            DetachHandlers();
            _viewModel = viewModel;
            viewModel.PublishConfirmationRequested += ConfirmPublish;
            viewModel.PdfReady += SavePdf;
            viewModel.ProblemOccurred += ShowProblem;
            viewModel.FormImageProvider = CaptureFormImage;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => DetachHandlers();

        private void DetachHandlers()
        {
            if (_viewModel is null)
                return;

            _viewModel.PublishConfirmationRequested -= ConfirmPublish;
            _viewModel.PdfReady -= SavePdf;
            _viewModel.ProblemOccurred -= ShowProblem;
            _viewModel.FormImageProvider = null;
            _viewModel = null;
        }

        // The explicit human act. The attestation statement is shown verbatim —
        // the case manager affirms text they have actually read, not a generic
        // "are you sure". Defaults to No so an accidental Enter does not publish.
        private void ConfirmPublish(object? sender, ATRequestPublishConfirmationEventArgs e)
        {
            var answer = MessageBox.Show(
                $"Publish the assistive technology request for {e.ClientName}, totalling {e.TotalCost:C}?\n\n" +
                $"{e.AttestationStatement}\n\n" +
                $"{DescribeScreenshots(e.ScreenshotCount, e.ItemCount)}\n\n" +
                "Publishing records your name and the current date and time on this request, and locks it. " +
                "To change it afterwards you must reopen it for correction, which removes the attestation.",
                "Publish AT Request",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            e.Confirmed = answer == MessageBoxResult.Yes;
        }

        // Stated at the point of no return. A clip that was never pasted is cheap
        // to add now and expensive afterwards, when fixing it means discarding the
        // attestation and publishing again.
        private static string DescribeScreenshots(int screenshotCount, int itemCount)
        {
            if (screenshotCount == 0)
                return "No screenshots are attached, so the request will have no evidence page.";

            var clips = screenshotCount == 1 ? "1 screenshot" : $"{screenshotCount} screenshots";
            return screenshotCount == itemCount
                ? $"{clips} will be included on the evidence page — one for every item."
                : $"{clips} will be included on the evidence page. {itemCount - screenshotCount} of {itemCount} items have none.";
        }

        private async void SavePdf(object? sender, ATRequestPdfReadyEventArgs e) =>
            await PdfFileSaver.SaveAsync(
                "Save AT request", e.SuggestedFileName, e.Content, "The AT request was saved.");

        private void ShowProblem(object? sender, ATRequestProblemEventArgs e) =>
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

        // ---- Pasted evidence clips ----
        //
        // The clipboard is a view concern, and so is decoding whatever arbitrary
        // format an application decided to put on it. Everything below normalises
        // that mess down to PNG bytes and hands them to the item's view model,
        // which knows nothing about WPF imaging.

        private void OnPasteScreenshot(object sender, RoutedEventArgs e) => PasteInto(sender);

        // A focused paste target should also respond to the shortcut people
        // actually reach for. Space and Enter already work through Button's own
        // click handling.
        private void OnPasteScreenshotKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;

            PasteInto(sender);
            e.Handled = true;
        }

        private void PasteInto(object sender)
        {
            if (sender is not FrameworkElement { DataContext: ATRequestItemEditorViewModel item })
                return;

            if (_viewModel?.CurrentEditor is { CanEdit: false })
            {
                MessageBox.Show(
                    "This request has been published and can no longer be edited. Reopen it for correction to change its screenshots.",
                    "Request Published",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var source = ReadClipboardImage();
            if (source is null)
            {
                MessageBox.Show(
                    "There is no image on the clipboard. Copy a screenshot first, then paste it here.",
                    "Nothing to Paste",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            byte[] png;
            try
            {
                png = EncodeAsPng(source);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"That image could not be read.\n\n{ex.Message}",
                    "Paste Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // The shared rule owner has the last word on size and format, and the
            // API applies the same check to whatever finally arrives.
            var problem = AtRequestScreenshot.Describe(png);
            if (problem is not null)
            {
                MessageBox.Show(problem, "Screenshot Not Accepted", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            item.ScreenshotPng = png;
        }

        /// <summary>
        /// Pulls an image off the clipboard. Tries the bitmap the clipboard
        /// advertises first, then falls back to a copied image FILE, because
        /// "copy image" in a file browser puts a path on the clipboard rather
        /// than pixels — and to the person doing it, those are the same act.
        /// </summary>
        private static BitmapSource? ReadClipboardImage()
        {
            try
            {
                if (Clipboard.ContainsImage())
                    return Clipboard.GetImage();

                if (!Clipboard.ContainsFileDropList())
                    return null;

                foreach (var path in Clipboard.GetFileDropList())
                {
                    if (path is null || !File.Exists(path))
                        continue;

                    var extension = Path.GetExtension(path);
                    if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var decoded = new BitmapImage();
                    decoded.BeginInit();
                    decoded.CacheOption = BitmapCacheOption.OnLoad;
                    decoded.UriSource = new Uri(path);
                    decoded.EndInit();
                    decoded.Freeze();
                    return decoded;
                }

                return null;
            }
            catch (Exception)
            {
                // The clipboard is shared mutable state owned by other processes;
                // it can be locked or hold a malformed payload. Treat any of that
                // as "nothing usable here".
                return null;
            }
        }

        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

        /// <summary>
        /// Re-encodes to PNG, downscaling first when the image is larger than the
        /// evidence page can use. A raw screen grab is several megabytes for a
        /// picture of a product listing; storing that per item, per request,
        /// forever, buys nothing a legible 1400px image does not already give.
        /// </summary>
        private static byte[] EncodeAsPng(BitmapSource source)
        {
            var (width, height) = AtRequestScreenshot.ScaleToFit(source.PixelWidth, source.PixelHeight);
            BitmapSource prepared = source;

            if (width != source.PixelWidth || height != source.PixelHeight)
            {
                var scale = new ScaleTransform(
                    (double)width / source.PixelWidth,
                    (double)height / source.PixelHeight);
                var scaled = new TransformedBitmap(source, scale);
                scaled.Freeze();
                prepared = scaled;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(prepared));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private void OnRemoveScreenshot(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ATRequestItemEditorViewModel item })
                item.ScreenshotPng = null;
        }

        /// <summary>
        /// Rasterizes the live form preview to a PNG for the proof-of-document
        /// snapshot. Rendered at 192 DPI so the stored image is legible when a
        /// reviewer opens it rather than a blurry thumbnail of a form.
        ///
        /// Returns null rather than throwing when the preview is not realized —
        /// a collapsed or zero-sized element is a normal state, not an error, and
        /// the caller treats a missing snapshot as acceptable.
        /// </summary>
        private byte[]? CaptureFormImage()
        {
            var form = FormPreview;
            if (form is null || form.ActualWidth <= 0 || form.ActualHeight <= 0)
                return null;

            const double dpi = 192d;
            const double scale = dpi / 96d;

            var target = new RenderTargetBitmap(
                (int)Math.Ceiling(form.ActualWidth * scale),
                (int)Math.Ceiling(form.ActualHeight * scale),
                dpi, dpi, PixelFormats.Pbgra32);

            // Draw through a VisualBrush rather than rendering the element
            // directly: RenderTargetBitmap.Render ignores an element's own layout
            // offset, which would clip the form against the top-left corner.
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    new VisualBrush(form),
                    null,
                    new Rect(new Point(), new Size(form.ActualWidth, form.ActualHeight)));
            }

            target.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
