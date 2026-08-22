using Carika.ViewModels;
using Sati.Contracts.V1;
using Xunit;

namespace Carika.Tests;

public sealed class NoteEntrySelectionTests
{
    [Fact]
    public void CarikaOffersOnlyIntentionalCaseManagerStatuses()
    {
        Assert.Equal(
            ["Scheduled", "Pending", "Logged", "Cancelled", "Delayed"],
            CaseNoteEntryOptions.CaseManagerStatuses.Select(option => option.Value));
        Assert.All(CaseNoteEntryOptions.CaseManagerStatuses, option => Assert.False(string.IsNullOrWhiteSpace(option.Guidance)));
        Assert.DoesNotContain(CaseNoteEntryOptions.CaseManagerStatuses, option =>
            option.Value is "Approved" or "Returned" or "Abandoned" or "HeldForCompliance" or "ComplianceBlocked");
    }

    [Fact]
    public void CarikaOffersEveryApiNoteTypeButNotTheJournalOnlyReminder()
    {
        Assert.Equal(
            ["Visit", "Contact", "Form", "Other"],
            CaseNoteEntryOptions.NoteTypes.Select(option => option.Value));
        Assert.DoesNotContain(CaseNoteEntryOptions.NoteTypes, option => option.Value == "Reminder");
    }

    [Fact]
    public void ViewModelDefaultsToAnEncryptedDraftFriendlyWorkflow()
    {
        var viewModel = new MainWindowViewModel(null!);

        Assert.Equal("Pending", viewModel.SelectedStatus.Value);
        Assert.Equal("Other", viewModel.SelectedNoteType.Value);
        Assert.False(viewModel.IsFormNote);
        Assert.Null(viewModel.SelectedFormType);
    }

    [Fact]
    public void FormSelectionRequiresAndThenClearsItsSpecificFormChoice()
    {
        var viewModel = new MainWindowViewModel(null!);
        viewModel.SelectedNoteType = viewModel.NoteTypeOptions.Single(option => option.Value == "Form");
        viewModel.SelectedFormType = viewModel.FormTypeOptions.Single(option => option.Value == "PCP");

        Assert.True(viewModel.IsFormNote);
        Assert.Equal("PCP", viewModel.SelectedFormType.Value);

        viewModel.SelectedNoteType = viewModel.NoteTypeOptions.Single(option => option.Value == "Visit");

        Assert.False(viewModel.IsFormNote);
        Assert.Null(viewModel.SelectedFormType);
    }
}
