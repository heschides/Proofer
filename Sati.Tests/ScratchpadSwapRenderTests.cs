using Sati.Services;
using Sati.Views;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Exercises the real Overview grid. Structurally valid XAML can still leave a
/// panel clipped, hidden, or in the wrong slot, so these inspect rendered state.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ScratchpadSwapRenderTests
{
    [Theory]
    [InlineData(2200, OverviewLayoutTier.Wide)]
    [InlineData(1600, OverviewLayoutTier.Balanced)]
    [InlineData(1200, OverviewLayoutTier.Compact)]
    public void DesktopTiersKeepAgendaCenteredAndDueDatesOnTheRight(
        double width,
        OverviewLayoutTier tier)
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            WpfUiHarness.Realize(view, width, 900);

            var note = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Current note panel");
            var deadlines = WpfUiHarness.FindByAutomationName<Border>(view, "Upcoming due dates panel");
            var productivity = WpfUiHarness.FindByAutomationName<Border>(view, "Monthly productivity panel");

            Assert.Equal(tier, view.CurrentLayout?.Tier);
            Assert.Equal(Visibility.Visible, note.Visibility);
            Assert.Equal(Visibility.Visible, view.WorkAgendaHost.Visibility);
            Assert.Equal(Visibility.Visible, deadlines.Visibility);
            Assert.Equal(Visibility.Visible, productivity.Visibility);
            Assert.Equal(0, Grid.GetColumn(note));
            Assert.Equal(2, Grid.GetColumn(view.WorkAgendaHost));
            Assert.Equal(4, Grid.GetColumn(deadlines));
            Assert.Equal(2, Grid.GetColumn(productivity));
        });
    }

    [Fact]
    public void NarrowTierStacksTheThreeEssentialPanesWithoutASelector()
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            WpfUiHarness.Realize(view, 900, 900);

            var note = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Current note panel");
            var deadlines = WpfUiHarness.FindByAutomationName<Border>(view, "Upcoming due dates panel");
            var productivity = WpfUiHarness.FindByAutomationName<Border>(view, "Monthly productivity panel");

            Assert.Equal(OverviewLayoutTier.NarrowStack, view.CurrentLayout?.Tier);
            Assert.Equal(Visibility.Visible, note.Visibility);
            Assert.Equal(Visibility.Visible, view.WorkAgendaHost.Visibility);
            Assert.Equal(Visibility.Visible, deadlines.Visibility);
            Assert.Equal(Visibility.Collapsed, productivity.Visibility);
            Assert.Equal(0, Grid.GetRow(note));
            Assert.Equal(1, Grid.GetRow(view.WorkAgendaHost));
            Assert.Equal(2, Grid.GetRow(deadlines));
        });
    }

    [Fact]
    public void AgendaControlKeepsItsIdentityAcrossResponsivePlacement()
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            var agenda = new ScratchpadView { DataContext = new object() };
            view.WorkAgendaHost.Content = agenda;

            WpfUiHarness.Realize(view, 2200, 900);
            WpfUiHarness.Realize(view, 900, 700);
            WpfUiHarness.Realize(view, 1600, 900);

            Assert.Same(agenda, view.WorkAgendaHost.Content);
            Assert.Single(WpfUiHarness.Descendants(view).OfType<ScratchpadView>());
        });
    }

    [Fact]
    public void ShortOverviewUsesTheAgendaHeightAndDoesNotRestoreWorkspaceControls()
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            WpfUiHarness.Realize(view, 2200, 650);

            var productivity = WpfUiHarness.FindByAutomationName<Border>(view, "Monthly productivity panel");
            var automationNames = WpfUiHarness.Descendants(view)
                .OfType<DependencyObject>()
                .Select(AutomationProperties.GetName)
                .ToList();

            Assert.False(view.CurrentLayout?.ShowsLowerSummaryBand);
            Assert.Equal(Visibility.Collapsed, productivity.Visibility);
            Assert.DoesNotContain("Overview workspace", automationNames);
            Assert.DoesNotContain("Focus note", automationNames);
        });
    }

    private static CaseManagerDashboardContentView NewView() => new()
    {
        DataContext = new DashboardStub()
    };

    public sealed class DashboardStub
    {
        public object? NoteEntry { get; set; }
    }
}
