using Sati.Services;
using Sati.Views;
using System.Windows;
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
    [InlineData(2200, OverviewLayoutTier.Wide, 2, 4, 6)]
    [InlineData(1600, OverviewLayoutTier.Balanced, 2, 4, 4)]
    [InlineData(1200, OverviewLayoutTier.CompactTwoPane, 2, 2, 2)]
    [InlineData(900, OverviewLayoutTier.CompactOnePane, 0, 0, 0)]
    public void OverviewPlacesWorkspacesForAvailableWidth(
        double width,
        OverviewLayoutTier tier,
        int agendaColumn,
        int deadlinesColumn,
        int notesColumn)
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            WpfUiHarness.Realize(view, width, 900);

            Assert.Equal(tier, view.CurrentLayout?.Tier);
            Assert.Equal(agendaColumn, Grid.GetColumn(view.WorkAgendaHost));

            var deadlines = WpfUiHarness.FindByAutomationName<Border>(view, "Deadlines panel");
            var notes = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Notes list panel");
            Assert.Equal(Visibility.Visible, view.WorkAgendaHost.Visibility);

            if (tier is OverviewLayoutTier.CompactOnePane or OverviewLayoutTier.CompactTwoPane)
            {
                Assert.Equal(Visibility.Collapsed, deadlines.Visibility);
                Assert.Equal(Visibility.Collapsed, notes.Visibility);

                SelectWorkspace(view, "Deadlines");
                Assert.Equal(Visibility.Visible, deadlines.Visibility);
                Assert.Equal(deadlinesColumn, Grid.GetColumn(deadlines));

                SelectWorkspace(view, "Notes");
                Assert.Equal(Visibility.Visible, notes.Visibility);
                Assert.Equal(notesColumn, Grid.GetColumn(notes));
            }
            else
            {
                Assert.Equal(deadlinesColumn, Grid.GetColumn(deadlines));
                Assert.Equal(notesColumn, Grid.GetColumn(notes));
            }
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
    public void ShortWideOverviewKeepsSummaryChoicesInTheSupportingSelector()
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            WpfUiHarness.Realize(view, 2200, 800);

            Assert.False(view.CurrentLayout?.ShowsLowerSummaryBand);
            var selector = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Overview workspace");
            Assert.Equal(Visibility.Visible, selector.Visibility);
            Assert.Contains(selector.Items.OfType<ComboBoxItem>(), item => Equals(item.Content, "Forms"));
            Assert.Contains(selector.Items.OfType<ComboBoxItem>(), item => Equals(item.Content, "Productivity"));
        });
    }

    [Fact]
    public void FocusNoteAndAgendaToggleKeepTheSameLiveWorkspaces()
    {
        WpfUiHarness.Run(() =>
        {
            var view = NewView();
            var agenda = new ScratchpadView { DataContext = new object() };
            view.WorkAgendaHost.Content = agenda;
            WpfUiHarness.Realize(view, 1200, 760);

            var note = WpfUiHarness.FindByAutomationName<ContentControl>(view, "Current note panel");
            var focus = WpfUiHarness.FindByAutomationName<Button>(view, "Focus note");
            focus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(view.IsFocusNote);
            Assert.Equal(Visibility.Visible, note.Visibility);
            Assert.Equal(Visibility.Collapsed, view.WorkAgendaHost.Visibility);

            var showAgenda = WpfUiHarness.FindByAutomationName<Button>(
                view,
                "Show Work Agenda while focusing on note");
            showAgenda.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Collapsed, note.Visibility);
            Assert.Equal(Visibility.Visible, view.WorkAgendaHost.Visibility);
            Assert.Same(agenda, view.WorkAgendaHost.Content);

            showAgenda.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var returnButton = WpfUiHarness.FindByAutomationName<Button>(view, "Return to overview");
            returnButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(view.IsFocusNote);
            Assert.Same(agenda, view.WorkAgendaHost.Content);
        });
    }

    private static CaseManagerDashboardContentView NewView() => new()
    {
        DataContext = new DashboardStub()
    };

    private static void SelectWorkspace(CaseManagerDashboardContentView view, string tag)
    {
        var selector = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Overview workspace");
        selector.SelectedItem = selector.Items
            .OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, tag));
        view.UpdateLayout();
    }

    public sealed class DashboardStub
    {
        public object? NoteEntry { get; set; }
    }
}
