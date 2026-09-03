using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sati.Data;
using Sati.ViewModels.ClientDocuments;
using Sati.Views.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AnnualDocumentViewRenderTests
{
    [Fact]
    public void SafetyPlanCommandsAndAccessibleCycleReachTheViewModel()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new SafetyPlanViewModel(null!, new SessionService());
            var view = new SafetyPlanWorkspace { DataContext = model };
            WpfUiHarness.Realize(view, 900, 1200);
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            var submit = buttons.Single(x => Equals(x.Content, "Submit for review"));
            Assert.Same(model.SubmitCommand, submit.Command); Assert.False(submit.IsEnabled);
            Assert.Same(model.ApproveCommand, buttons.Single(x => Equals(x.Content, "Approve submitted plan")).Command);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Safety plan cycle beginning"));
            SavePreview(view, "safety-workspace.png");
        });
    }

    [Fact]
    public void PacketAndReceiptCommandsBindAndRemainDisabledWithoutAConsumer()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new AnnualDocumentsViewModel(null!, null!, null!, new SessionService());
            var view = new AnnualDocumentsWorkspace { DataContext = model };
            WpfUiHarness.Realize(view, 900, 1200);
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();
            var save = buttons.Single(x => Equals(x.Content, "Save Annual Documents Locally"));
            Assert.Same(model.SavePacketCommand, save.Command); Assert.False(save.IsEnabled);
            var receipt = buttons.Single(x => Equals(x.Content, "Record receipt or effort"));
            Assert.Same(model.AcknowledgeCommand, receipt.Command); Assert.False(receipt.IsEnabled);
            Assert.NotNull(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Privacy notice received on"));
            SavePreview(view, "annual-workspace.png");
        });
    }

    private static void SavePreview(FrameworkElement view, string fileName)
    {
        if (Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT") is not { Length: > 0 } directory) return;
        var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName)); encoder.Save(output);
    }
}
