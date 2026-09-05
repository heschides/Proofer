using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class NoteEntryAdaptiveRenderTests
{
    [Fact]
    public void ShortEditorSwitchesBetweenDetailsAndTheSameLiveNarrative()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = new object() };
            WpfUiHarness.Realize(view, 440, 700);

            Assert.True(view.IsShortEditor);
            Assert.Equal("Details", view.ActiveSectionName);

            var narrative = WpfUiHarness.FindByAutomationName<TextBox>(view, "Note narrative");
            var write = WpfUiHarness.FindByAutomationName<Button>(view, "Show note writing section");
            write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("Write", view.ActiveSectionName);
            Assert.Same(
                narrative,
                WpfUiHarness.FindByAutomationName<TextBox>(view, "Note narrative"));
            Assert.True(narrative.MinHeight >= 200);

            WpfUiHarness.Realize(view, 440, 900);

            Assert.False(view.IsShortEditor);
            Assert.Same(
                narrative,
                WpfUiHarness.FindByAutomationName<TextBox>(view, "Note narrative"));
            Assert.True(narrative.MinHeight >= 240);
        });
    }
}
