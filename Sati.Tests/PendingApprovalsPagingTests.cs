using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Supervisor;
using Xunit;

namespace Sati.Tests;

public sealed class PendingApprovalsPagingTests
{
    [Fact]
    public async Task LoadingScrollingAndChangingThresholdNeverApproveNotes()
    {
        var (vm, service) = Create();
        await vm.LoadAsync(41);
        Assert.Equal(10, vm.PendingNotes.Count);
        Assert.Equal("4", vm.MaximumUnitsText);
        vm.MaximumUnitsText = "3";
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(13, vm.PendingNotes.Count);
        Assert.Empty(service.Approved);
        Assert.False(vm.HasMore);
        Assert.All(service.Filters, filter => Assert.Equal(41, filter.UserId));
        await vm.BatchApproveCommand.ExecuteAsync(null);
        Assert.Equal(13, service.Approved.Count);
        Assert.All(service.Approved, item => Assert.Equal(3, item.Limit));
        Assert.Contains("Approved 13", vm.StatusMessage);
    }

    [Fact]
    public async Task OlderLoadCannotReplaceNewFilterResults()
    {
        var (vm, service) = Create();
        var delayed = new TaskCompletionSource<NoteReviewPage<Note>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Delayed = delayed.Task;
        var older = vm.LoadAsync(41);
        await vm.LoadAsync(42);
        delayed.SetResult(new([MakeNote(99)], null, 99));
        await older;
        Assert.DoesNotContain(vm.PendingNotes, note => note.NoteId == 99);
        Assert.Equal(10, vm.PendingNotes.Count);
        Assert.False(vm.IsLoading);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("97")]
    public void InvalidThresholdDisablesBatchApproval(string text)
    {
        var (vm, _) = Create();
        vm.MaximumUnitsText = text;
        Assert.False(vm.BatchApproveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingAndClearingFiltersReloadsTheQueueWithOneStableQuery()
    {
        var (vm, service) = Create();
        await vm.LoadAsync();
        vm.SelectedCaseManager = vm.CaseManagerOptions.Single();
        vm.SelectedClient = vm.ClientOptions.Single();
        vm.FromDate = new DateTime(2026, 8, 1);
        vm.ToDate = new DateTime(2026, 8, 31);
        vm.SearchTerm = "follow-up";

        await vm.ApplyFiltersCommand.ExecuteAsync(null);
        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.All(service.Filters.TakeLast(2), filter =>
        {
            Assert.Equal(41, filter.UserId);
            Assert.Equal(501, filter.PersonId);
            Assert.Equal(new DateTime(2026, 8, 1), filter.FromDate);
            Assert.Equal(new DateTime(2026, 8, 31), filter.ToDate);
            Assert.Equal("follow-up", filter.SearchTerm);
        });

        await vm.ClearFiltersCommand.ExecuteAsync(null);
        Assert.Equal(new NoteReviewQuery(), service.Filters[^1]);
    }

    [Fact]
    public async Task InvalidDateRangeDoesNotReplaceTheCurrentQueue()
    {
        var (vm, service) = Create();
        await vm.LoadAsync();
        var calls = service.Filters.Count;
        vm.FromDate = new DateTime(2026, 9, 2);
        vm.ToDate = new DateTime(2026, 9, 1);

        await vm.ApplyFiltersCommand.ExecuteAsync(null);

        Assert.Equal(calls, service.Filters.Count);
        Assert.Contains("start date", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingFilterChoicesAreVisibleAndDisableFilterActions()
    {
        var (vm, service) = Create();
        service.FailFilterOptions = true;

        await vm.LoadAsync();

        Assert.False(vm.AreFiltersAvailable);
        Assert.False(vm.ApplyFiltersCommand.CanExecute(null));
        Assert.False(vm.ClearFiltersCommand.CanExecute(null));
        Assert.Contains("not match", vm.FilterStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(vm.PendingNotes);
    }

    private static (PendingApprovalsViewModel, FakeService) Create()
    {
        var session = new SessionService();
        session.SetUser(User.Create(7, "reviewer", "Reviewer", "hash", "salt", UserRole.Supervisor, null, 1));
        var service = new FakeService();
        return (new(service, session), service);
    }

    private static Note MakeNote(int id)
    {
        var person = Person.CreatePerson(41, "Synthetic", "Consumer", "Test", new DateTime(1990, 1, 1),
            null, WaiverType.None, new Settings());
        var note = Note.Create("Synthetic narrative", DateTime.Today, NoteStatus.Logged, 15, id, noteType: NoteType.Contact);
        typeof(Note).GetProperty(nameof(Note.Id))!.SetValue(note, id);
        note.Person = person;
        return note;
    }

    private sealed class FakeService : ISupervisorService
    {
        public List<(int Id, int? Limit)> Approved { get; } = [];
        public List<NoteReviewQuery> Filters { get; } = [];
        public Task<NoteReviewPage<Note>>? Delayed { get; set; }
        public bool FailFilterOptions { get; set; }
        public Task<NoteReviewPage<Note>> GetReviewPageAsync(
            int supervisorId, int afterId = 0, int? throughId = null, NoteReviewQuery? filter = null)
        {
            Filters.Add(filter ?? new());
            if (Delayed is { } task) { Delayed = null; return task; }
            var rows = Enumerable.Range(1, 13).Where(id => id > afterId && !Approved.Any(a => a.Id == id))
                .Take(10).Select(MakeNote).ToList();
            return Task.FromResult(new NoteReviewPage<Note>(rows, rows.Count == 10 ? rows[^1].Id : null, 13));
        }
        public Task<NoteReviewFilterOptions> GetReviewFilterOptionsAsync(int supervisorId) =>
            FailFilterOptions
                ? Task.FromException<NoteReviewFilterOptions>(new InvalidOperationException("Unavailable"))
                : Task.FromResult(new NoteReviewFilterOptions(
                    [new(41, "Case Manager")],
                    [new(501, 41, "Synthetic Consumer")]));
        public Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision, int? maximumUnits = null)
        { Approved.Add((noteId, maximumUnits)); return Task.CompletedTask; }
        public Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetNonCompliantNotesAsync(int supervisorId, bool allSupervisees = false) => throw new NotSupportedException();
        public Task ApproveWithOverrideAsync(int noteId, int supervisorId, string overrideReason, int expectedRevision) => throw new NotSupportedException();
        public Task ReturnNoteAsync(int noteId, int supervisorId, string reason, int expectedRevision) => throw new NotSupportedException();
    }
}
