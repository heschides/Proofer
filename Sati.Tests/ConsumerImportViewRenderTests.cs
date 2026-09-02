using Sati.Data;
using Sati.Services;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The import review panel loaded for real, with a DataContext.
///
/// <para>
/// Its failure modes are silent: a DynamicResource naming a brush the themes do not define
/// renders in the default colour and logs nothing, and a mistyped command binding leaves a
/// button that looks wired and does nothing. The caseload distribution view shipped exactly the
/// first of those, which is why this file exists alongside the view-model tests.
/// </para>
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ConsumerImportViewRenderTests
{
    [Fact]
    public void TheActionButtonsReachTheViewModelCommands()
    {
        var model = new ConsumerImportViewModel(new CredibleExportReader(), new StubPicker());

        Render(model, view =>
        {
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();

            Assert.Same(model.AcceptAllCommand,
                buttons.Single(b => Equals(b.Content, "Accept all")).Command);
            Assert.Same(model.ClearAllCommand,
                buttons.Single(b => Equals(b.Content, "Clear")).Command);
            Assert.Same(model.ApplyCommand,
                buttons.Single(b => Equals(b.Content, "Fill the form")).Command);
            Assert.Same(model.CancelCommand,
                buttons.Single(b => Equals(b.Content, "Cancel")).Command);
        });
    }

    // Nothing has been accepted, so the button that fills the form must be inert.
    [Fact]
    public void FillingTheFormIsInertUntilSomethingIsAccepted()
    {
        var model = new ConsumerImportViewModel(new CredibleExportReader(), new StubPicker());

        Render(model, view =>
        {
            var fill = WpfUiHarness.Descendants(view).OfType<Button>()
                .Single(b => Equals(b.Content, "Fill the form"));

            Assert.False(fill.IsEnabled);
        });
    }

    [Fact]
    public void TheFieldListIsReachableByItsAccessibleName()
    {
        var model = new ConsumerImportViewModel(new CredibleExportReader(), new StubPicker());

        Render(model, view =>
        {
            var list = WpfUiHarness.FindByAutomationName<ItemsControl>(
                view, "Fields found in the export");

            Assert.NotNull(list);
        });
    }

    private static void Render(ConsumerImportViewModel model, Action<ConsumerImportView> assert)
    {
        WpfUiHarness.Run(() =>
        {
            var view = new ConsumerImportView { DataContext = model };
            WpfUiHarness.Realize(view);
            assert(view);
        });
    }

    private sealed class StubPicker : IExportFilePicker
    {
        public string? PickExportFile() => null;
        public string? PickExportFolder() => null;
    }
}
