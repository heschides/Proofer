using Sati.Models;
using Sati.Services;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The notes page loaded for real, with the application's resource dictionary.
/// <see cref="StabilizationTests"/> reads the XAML as XML, which proves a panel is
/// declared in the right grid cell but cannot prove that
/// <c>{StaticResource {x:Type TextBox}}</c> resolves, that a <c>RelativeSource</c>
/// binding finds the property it names, or that a locked note is actually
/// read-only rather than merely marked up as if it were. These tests load the
/// views on a real UI thread and read the resulting element state.
/// </summary>
/// <remarks>
/// WPF permits one <see cref="Application"/> per process, so these run on the
/// single shared <see cref="WpfUiHarness"/> thread rather than building their own.
/// </remarks>
[Collection(WpfViewCollection.Name)]
public sealed class NotePanelRenderTests
{
    private static Note ExistingNote(int personId, string narrative = "The saved narrative.") =>
        Note.Create(narrative, new DateTime(2026, 8, 20), NoteStatus.Pending, 15,
            personId, null, NoteType.Contact);

    // -------------------------------------------------------------------------
    // Layout, as loaded
    // -------------------------------------------------------------------------

    // That these views construct at all is already covered for every view in
    // Sati.Views by the feature-view smoke test in StabilizationTests. What is
    // asserted here is the state a loaded view actually ends up in.

    [Fact]
    public async Task TheNotesLogLoadsWithItsPanelsInTheIntendedCells()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();

        WpfUiHarness.Run(() =>
        {
            var view = new NotesLogView { DataContext = log };
            WpfUiHarness.Realize(view);

            // Read back from the loaded elements, not from the markup: this is the
            // layout that a case manager would actually be looking at.
            AssertCell(view, "NoteEntryPanel", row: 0, column: 0);
            AssertCell(view, "NotesFilterPanel", row: 0, column: 2);
            AssertCell(view, "NotesDataGridPanel", row: 2, column: 2);

            // Filters directly above the grid means they share a column and the
            // grid is the row below them.
            var filters = FindNamed<FrameworkElement>(view, "NotesFilterPanel");
            var grid = FindNamed<FrameworkElement>(view, "NotesDataGridPanel");
            Assert.Equal(Grid.GetColumn(filters), Grid.GetColumn(grid));
            Assert.True(Grid.GetRow(grid) > Grid.GetRow(filters));
        });
    }

    // -------------------------------------------------------------------------
    // Locked really is read-only, and still readable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ALockedNoteIsReadOnlyButStaysSelectableAndScrollable()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var narrative = WpfUiHarness.FindByAutomationName<TextBox>(view, "Note narrative");
            Assert.True(narrative.IsReadOnly);
            Assert.True(TextShortcutTarget.GetIsEnabled(narrative));

            // The whole point of choosing IsReadOnly over IsEnabled=False: the
            // record stays legible, focusable, selectable and copyable while locked.
            Assert.True(narrative.IsEnabled);
            Assert.True(narrative.Focusable);

            // Anything that would change the record is off.
            Assert.False(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Person").IsEnabled);
            Assert.False(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Status").IsEnabled);
            Assert.False(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Event date").IsEnabled);
            Assert.All(
                WpfUiHarness.Descendants(view).OfType<RadioButton>(),
                radio => Assert.False(radio.IsEnabled));
        });
    }

    [Fact]
    public async Task UnlockingRestoresEveryFieldAndTheSaveButton()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var narrative = WpfUiHarness.FindByAutomationName<TextBox>(view, "Note narrative");
            var person = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Person");
            Assert.True(narrative.IsReadOnly);

            panel.ToggleLockCommand.Execute(null);
            WpfUiHarness.Realize(view);

            Assert.False(narrative.IsReadOnly);
            Assert.True(person.IsEnabled);
            Assert.All(
                WpfUiHarness.Descendants(view).OfType<RadioButton>(),
                radio => Assert.True(radio.IsEnabled));
        });
    }

    // -------------------------------------------------------------------------
    // Client picker name format — a MultiBinding crossing back out of the
    // ComboBox's own item template to a sibling property on its DataContext.
    // Structural XAML reading cannot prove this resolves; only a realized view can.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(false, "Journal Person")]
    [InlineData(true, "Person, Journal")]
    public async Task TheSelectedClientNameFormatFollowsTheSortPreference(
        bool sortsByLastName, string expectedText)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.SetSortsPickersByLastName(sortsByLastName);
        panel.SelectedPerson = person;

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var picker = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Person");
            var rendered = WpfUiHarness.Descendants(picker)
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .FirstOrDefault(text => text == expectedText);

            Assert.Equal(expectedText, rendered);
        });
    }

    [Fact]
    public async Task FutureServiceWorkKeepsItsTypeAndExplainsTheScheduledOutcome()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        panel.SelectedPerson = await fixture.PersonOneAsync();
        panel.SelectedNoteType = NoteType.Email;
        panel.Minutes = 30;
        panel.EventDate = DateTime.Today.AddDays(5);

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            Assert.Equal(NoteType.Email, panel.SelectedNoteType);
            Assert.Equal(NoteStatus.Scheduled, panel.Status);
            Assert.True(WpfUiHarness.FindByAutomationName<DatePicker>(view, "Event date").IsEnabled);
            Assert.False(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Status").IsEnabled);
            Assert.False(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Service start time").IsEnabled);
            Assert.Contains("planned work", panel.StatusGuidance, StringComparison.OrdinalIgnoreCase);
            var save = WpfUiHarness.Descendants(view)
                .OfType<Button>()
                .Single(button => button.Command == panel.SubmitNoteCommand);
            Assert.Equal("Schedule Work", save.Content);
        });
    }

    [Fact]
    public async Task TheSaveButtonIsHiddenWhileLockedAndBackWhenUnlocked()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var save = WpfUiHarness.Descendants(view)
                .OfType<Button>()
                .Single(button => button.Command == panel.SubmitNoteCommand);
            Assert.Equal(Visibility.Collapsed, save.Visibility);

            panel.ToggleLockCommand.Execute(null);
            WpfUiHarness.Realize(view);

            Assert.Equal(Visibility.Visible, save.Visibility);
        });
    }

    /// <summary>
    /// The attendee checkboxes live inside an <c>ItemsControl</c> whose items are
    /// attendee view models, so a plain <c>{Binding IsLocked}</c> in the read-only
    /// style would silently find nothing and leave them editable. This is the
    /// reason those styles bind through the UserControl's DataContext, and the
    /// only assertion that would notice if that were undone.
    /// </summary>
    [Fact]
    public async Task AttendeeCheckboxesInsideTheItemsControlAreLockedToo()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry(contacts: new StubPersonContactService("Dana"));
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        var visit = ExistingNote(person.Id);
        visit.NoteType = NoteType.Visit;
        panel.EnterViewMode(visit);

        // The roster loads asynchronously off the note load.
        await WaitForAsync(() => panel.VisitAttendees.Count == 1);

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var attendee = WpfUiHarness.FindByAutomationName<CheckBox>(view, "Dana Contact — Guardian");
            Assert.False(attendee.IsEnabled);

            panel.ToggleLockCommand.Execute(null);
            WpfUiHarness.Realize(view);
            Assert.True(attendee.IsEnabled);
        });
    }

    [Fact]
    public async Task VisitSettingAllowsSeveralCheckedChoices()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        panel.SelectedNoteType = NoteType.Visit;

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var home = WpfUiHarness.FindByAutomationName<CheckBox>(
                view, "Meeting setting: Consumer's home");
            var community = WpfUiHarness.FindByAutomationName<CheckBox>(
                view, "Meeting setting: Community setting");

            home.IsChecked = true;
            community.IsChecked = true;

            Assert.True(home.IsChecked);
            Assert.True(community.IsChecked);
            Assert.True(panel.VisitSettingOptions.Single(option =>
                option.Value == VisitSetting.ConsumerHome).IsSelected);
            Assert.True(panel.VisitSettingOptions.Single(option =>
                option.Value == VisitSetting.Community).IsSelected);
        });
    }

    [Fact]
    public async Task NotesFilterControlsShareRenderedHeightsAndRowBaselines()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();

        WpfUiHarness.Run(() =>
        {
            var view = new NotesLogView { DataContext = log };
            WpfUiHarness.Realize(view, 1400, 900);

            var client = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Filter by person");
            var status = WpfUiHarness.FindByAutomationName<ComboBox>(view, "Filter by status");
            var search = WpfUiHarness.FindByAutomationName<TextBox>(view, "Search Text");
            var start = WpfUiHarness.FindByAutomationName<DatePicker>(view, "Range start date");
            var end = WpfUiHarness.FindByAutomationName<DatePicker>(view, "Range end date");
            var summary = FindNamed<Border>(view, "UnitsRangeSummary");
            var filter = FindNamed<Border>(view, "NotesFilterPanel");

            Assert.All(new FrameworkElement[] { client, status, search, start, end, summary },
                control => Assert.Equal(36, control.ActualHeight, precision: 1));
            Assert.Equal(Top(client), Top(status), precision: 1);
            Assert.Equal(Top(client), Top(search), precision: 1);
            Assert.Equal(Top(start), Top(end), precision: 1);
            Assert.Equal(Top(start), Top(summary), precision: 1);

            double Top(FrameworkElement element) =>
                element.TranslatePoint(new Point(0, 0), filter).Y;
        });
    }

    // -------------------------------------------------------------------------
    // The way back to New Note, as loaded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TheNewNoteButtonIsAlwaysPresentAndEnablesWhenItWouldDoSomething()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var newNote = WpfUiHarness.FindByAutomationName<Button>(view, "Start a new note");

            // Present from the outset, so the way out is visible before it is
            // needed — disabled, not hidden, while the panel is already blank.
            Assert.Equal(Visibility.Visible, newNote.Visibility);
            Assert.False(newNote.IsEnabled);

            panel.EnterViewMode(ExistingNote(person.Id));
            WpfUiHarness.Realize(view);

            Assert.Equal(Visibility.Visible, newNote.Visibility);
            Assert.True(newNote.IsEnabled);
        });
    }

    /// <summary>
    /// Escape is declared on the module and repeated on each host page. What can
    /// actually break is the binding path — <c>StartNewNoteCommand</c> on the
    /// module, <c>NoteEntry.StartNewNoteCommand</c> from a host — and a path that
    /// resolves to nothing leaves a key that silently does nothing.
    /// </summary>
    [Fact]
    public async Task EscapeResolvesToTheNewNoteCommandInThePanelAndOnTheNotesLog()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var log = fixture.NotesWindow();

        WpfUiHarness.Run(() =>
        {
            var panelView = new NoteEntryView { DataContext = log.NoteEntry };
            WpfUiHarness.Realize(panelView);
            Assert.Same(log.NoteEntry.StartNewNoteCommand, EscapeCommand(panelView));

            var logView = new NotesLogView { DataContext = log };
            WpfUiHarness.Realize(logView);
            Assert.Same(log.NoteEntry.StartNewNoteCommand, EscapeCommand(logView));
        });

        // The dashboard declares the same path; the assertion that it declares it
        // lives in StabilizationTests, because its view model cannot be built here.
        // What that path needs from the host is this property.
        var noteEntry = typeof(Sati.ViewModels.CaseManagerDashboardViewModel)
            .GetProperty(nameof(Sati.ViewModels.NotesWindowViewModel.NoteEntry));
        Assert.NotNull(noteEntry);
        Assert.Equal(typeof(NoteEntryViewModel), noteEntry!.PropertyType);
    }

    private static System.Windows.Input.ICommand EscapeCommand(FrameworkElement view)
    {
        var binding = view.InputBindings
            .OfType<System.Windows.Input.KeyBinding>()
            .SingleOrDefault(candidate => candidate.Key == System.Windows.Input.Key.Escape);

        Assert.NotNull(binding);
        Assert.NotNull(binding!.Command);
        return binding.Command;
    }

    /// <summary>
    /// The stale-note warning has to actually reach the screen, and has to reach a
    /// screen reader without waiting for a pause — the case manager is about to
    /// type into a record that is not the one they think it is.
    /// </summary>
    [Fact]
    public async Task TheStaleNoteWarningAppearsAndIsAnnouncedAssertively()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var panel = fixture.NoteEntry();
        var person = await fixture.PersonOneAsync();
        panel.SetPeople([person]);
        panel.EnterViewMode(ExistingNote(person.Id));

        WpfUiHarness.Run(() =>
        {
            var view = new NoteEntryView { DataContext = panel };
            WpfUiHarness.Realize(view);

            var warning = WpfUiHarness.FindByAutomationName<TextBlock>(
                view, "Note changed on the server");
            Assert.Equal(Visibility.Collapsed, VisibleAncestorBanner(warning).Visibility);

            panel.StaleNoteMessage = "This note changed after you opened it.";
            WpfUiHarness.Realize(view);

            Assert.Equal(Visibility.Visible, VisibleAncestorBanner(warning).Visibility);
            Assert.Equal("This note changed after you opened it.", warning.Text);
            Assert.Equal(
                System.Windows.Automation.AutomationLiveSetting.Assertive,
                System.Windows.Automation.AutomationProperties.GetLiveSetting(warning));
        });
    }

    // The banner Border carries the Visibility; the TextBlock is what is named.
    private static FrameworkElement VisibleAncestorBanner(FrameworkElement named) =>
        (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(
            System.Windows.Media.VisualTreeHelper.GetParent(named));

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition(), "The awaited view-model state never arrived.");
    }

    private static void AssertCell(DependencyObject root, string name, int row, int column)
    {
        var panel = FindNamed<FrameworkElement>(root, name);
        Assert.Equal(row, Grid.GetRow(panel));
        Assert.Equal(column, Grid.GetColumn(panel));
    }

    private static T FindNamed<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var match = WpfUiHarness.Descendants(root)
            .OfType<T>()
            .FirstOrDefault(candidate => candidate.Name == name);

        return match ?? throw new InvalidOperationException(
            $"No element named \"{name}\" is present in the rendered tree.");
    }
}

/// <summary>
/// One WPF <see cref="Application"/> exists per process and the harness owns a
/// single UI thread, so view tests must not run beside each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfViewCollection
{
    public const string Name = "wpf-views";
}
