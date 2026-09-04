using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The Overview's middle column and the shell's collapsible side panel trade the
/// notes panel and the Scratchpad between them when "Display Scratchpad in the
/// center of the display" is on.
///
/// Reading the XAML proves the two panels are declared in the right cell; it does
/// not prove the triggers that swap them actually fire. A first attempt at this
/// feature moved the wrong panel entirely and nothing failed until someone looked
/// at the screen, so these load the real view and read back rendered visibility.
/// </summary>
public sealed class ScratchpadSwapRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOverviewsMiddleColumnHoldsTheNotesPanelUntilTheScratchpadIsCentered(bool centered)
    {
        WpfUiHarness.Run(() =>
        {
            var view = new CaseManagerDashboardContentView
            {
                DataContext = new DashboardStub { IsScratchpadCentered = centered }
            };
            WpfUiHarness.Realize(view);

            var notes = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Notes panel");
            var scratchpad = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Today's Work panel");

            if (centered)
            {
                Assert.Equal(Visibility.Visible, scratchpad.Visibility);
                Assert.Equal(Visibility.Collapsed, notes.Visibility);
            }
            else
            {
                Assert.Equal(Visibility.Visible, notes.Visibility);
                Assert.Equal(Visibility.Collapsed, scratchpad.Visibility);
            }
        });
    }

    /// <summary>
    /// The notes panel has to be a real NotesPanelView in the middle cell, not an
    /// empty host. Extracting it from this view is what let the same control also
    /// render in the side panel, and an empty middle column would look identical to
    /// a broken swap.
    /// </summary>
    [Fact]
    public void TheMiddleColumnHostsTheExtractedNotesPanelAndTheShellsScratchpad()
    {
        WpfUiHarness.Run(() =>
        {
            var scratchpadViewModel = new object();
            var view = new CaseManagerDashboardContentView
            {
                DataContext = new DashboardStub
                {
                    IsScratchpadCentered = true,
                    Scratchpad = scratchpadViewModel
                }
            };
            WpfUiHarness.Realize(view);

            Assert.Single(WpfUiHarness.Descendants(view).OfType<NotesPanelView>());

            // The centered scratchpad must render the instance handed down by the
            // shell. A second, independently constructed one would take typing that
            // the shell never saves.
            var scratchpad = Assert.Single(WpfUiHarness.Descendants(view).OfType<ScratchpadView>());
            Assert.Same(scratchpadViewModel, scratchpad.DataContext);
        });
    }

    /// <summary>
    /// Stands in for CaseManagerDashboardViewModel, which needs most of the data
    /// layer to construct. The bindings under test read exactly these two members,
    /// and WPF resolves them by name rather than by type.
    /// </summary>
    public sealed class DashboardStub
    {
        public bool IsScratchpadCentered { get; set; }
        public object? Scratchpad { get; set; }
    }
}
