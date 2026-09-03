using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Supervisor;
using System.IO;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Bulk folder import, for agency onboarding.
///
/// <para>
/// The property everything else rests on is that the dry run writes nothing. Onboarding is the
/// moment an agency's entire caseload lands in Sati; a mistake made silently there is the most
/// expensive kind, so the operator sees what would happen before anything happens.
/// </para>
/// </summary>
public sealed class CaseloadImportViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"credible-bulk-{Guid.NewGuid():N}");

    public CaseloadImportViewModelTests() => Directory.CreateDirectory(_folder);

    // ---- The dry run ----

    [Fact]
    public async Task TheDryRunReportsWhatItFoundAndCreatesNothing()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        WriteExport("beta.html", "1002", "BETA");
        var people = new RecordingPersonService();
        var model = Build(people);

        await model.DryRunAsync(_folder);

        Assert.Equal(2, model.ReadyCount);
        Assert.Empty(people.Created);
        Assert.Contains("Nothing has been saved yet", model.StatusMessage);
    }

    [Fact]
    public async Task NothingCanBeCommittedBeforeADryRun()
    {
        var model = Build(new RecordingPersonService());

        Assert.False(model.CanCommit);
    }

    // Both of the artifacts an operator is most likely to produce by mistake. Neither should
    // stop the batch; both should be named.
    [Fact]
    public async Task FilesThatCannotBeReadAreReportedWithoutStoppingTheRest()
    {
        WriteExport("good.html", "1001", "ALPHA");
        File.WriteAllText(Path.Combine(_folder, "printed.html"), "%PDF-1.7\n");
        File.WriteAllText(Path.Combine(_folder, "appwindow.html"),
            "<html><frameset rows=\"95,*\"><frame name=\"main\"></frameset></html>");
        var model = Build(new RecordingPersonService());

        await model.DryRunAsync(_folder);

        Assert.Equal(1, model.ReadyCount);
        Assert.Equal(2, model.ProblemCount);
        Assert.Contains(model.Rows, row => row.Detail.Contains("printing to PDF"));
        Assert.Contains(model.Rows, row => row.Detail.Contains("application window"));
    }

    // A consumer with no name cannot be created and must not be guessed at. Reporting it is
    // what stops the file being silently absent from the batch.
    [Fact]
    public async Task AnExportWithNoNameIsReportedRatherThanSkippedSilently()
    {
        File.WriteAllText(Path.Combine(_folder, "nameless.html"),
            """
            <html><body><form action="./client_printview.aspx?client_id=1009"><table>
              <tr><td class="shc" colspan="4"><b>CONSUMER INFO</b></td></tr>
              <tr><td class="lc"><b>MaineCare ID</b></td><td class="vc">999</td></tr>
            </table></form></body></html>
            """);
        var model = Build(new RecordingPersonService());

        await model.DryRunAsync(_folder);

        var row = Assert.Single(model.Rows);
        Assert.Equal(BulkImportDisposition.Incomplete, row.Disposition);
        Assert.Equal(0, model.ReadyCount);
    }

    [Fact]
    public async Task AFolderWithNoSavedPagesSaysSoAndNamesTheLikelyCause()
    {
        File.WriteAllText(Path.Combine(_folder, "export.pdf"), "%PDF-1.7\n");
        var model = Build(new RecordingPersonService());

        await model.DryRunAsync(_folder);

        Assert.False(model.HasRows);
        Assert.Contains("saved as", model.StatusMessage);
    }

    // ---- Dedupe ----

    [Fact]
    public async Task AConsumerTheAgencyAlreadyHoldsIsSkippedAndNamed()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        WriteExport("beta.html", "1002", "BETA");
        var people = new RecordingPersonService();
        people.ExistingMatches.Add(new CredibleClientMatchDto("1001", "Jane Doe"));
        var model = Build(people);

        await model.DryRunAsync(_folder);

        Assert.Equal(1, model.ReadyCount);
        Assert.Equal(1, model.AlreadyImportedCount);
        Assert.Contains(model.Rows, row => row.Detail.Contains("Jane Doe"));
    }

    // The gap the address demo actually hit: a consumer hand-entered before Credible import
    // existed has no Credible id on file at all, so the id-only tier cannot see them. MaineCare
    // id is the second tier for exactly this case.
    [Fact]
    public async Task AConsumerWithNoCredibleIdOnFileIsStillCaughtByMaineCareId()
    {
        WriteExport("alpha.html", "1001", "ALPHA", maineCareId: "99998888A");
        var people = new RecordingPersonService();
        people.ExistingMaineCareMatches.Add(new MaineCareIdMatchDto("99998888A", "Jane Doe"));
        var model = Build(people);

        await model.DryRunAsync(_folder);

        Assert.Equal(0, model.ReadyCount);
        Assert.Equal(1, model.AlreadyImportedCount);
        Assert.Contains(model.Rows, row =>
            row.Detail.Contains("Jane Doe") && row.Detail.Contains("MaineCare ID"));
    }

    // Third tier: neither a Credible id nor a MaineCare id on file, only a name and birth date.
    [Fact]
    public async Task AConsumerWithOnlyANameAndBirthDateOnFileIsStillCaughtByNameAndDob()
    {
        WriteExport("alpha.html", "1001", "ALPHA", lastName: "SMITH", maineCareId: "");
        var people = new RecordingPersonService();
        people.ExistingNameBirthDateMatches.Add(new NameBirthDateMatchDto(
            new PersonNameBirthDate("SMITH", "ALPHA", new DateTime(1990, 1, 2)), "Jane Doe"));
        var model = Build(people);

        await model.DryRunAsync(_folder);

        Assert.Equal(0, model.ReadyCount);
        Assert.Equal(1, model.AlreadyImportedCount);
        Assert.Contains(model.Rows, row =>
            row.Detail.Contains("Jane Doe") && row.Detail.Contains("name and date of birth"));
    }

    // Precedence: when a row's Credible id already matches, that must win even if a coincidental
    // name+DOB collision exists elsewhere in the agency — the Credible id is the reliable tier.
    [Fact]
    public async Task ACredibleIdMatchTakesPrecedenceOverANameAndDobCollision()
    {
        WriteExport("alpha.html", "1001", "ALPHA", lastName: "SMITH");
        var people = new RecordingPersonService();
        people.ExistingMatches.Add(new CredibleClientMatchDto("1001", "Credible Match"));
        people.ExistingNameBirthDateMatches.Add(new NameBirthDateMatchDto(
            new PersonNameBirthDate("SMITH", "ALPHA", new DateTime(1990, 1, 2)), "Name Match"));
        var model = Build(people);

        await model.DryRunAsync(_folder);

        var row = Assert.Single(model.Rows);
        Assert.Contains("Credible Match", row.Detail);
        Assert.DoesNotContain("Name Match", row.Detail);
    }

    // Re-running the same folder must report, not duplicate. This is the whole reason the
    // Credible id is captured.
    [Fact]
    public async Task ReRunningTheSameFolderCreatesNothingASecondTime()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService();
        var model = Build(people);

        await model.DryRunAsync(_folder);
        await model.CommitCommand.ExecuteAsync(null);
        Assert.Single(people.Created);

        await model.DryRunAsync(_folder);

        Assert.Equal(0, model.ReadyCount);
        Assert.Equal(1, model.AlreadyImportedCount);
        Assert.False(model.CanCommit);
    }

    // A dedupe check that failed must not read as "nothing is a duplicate" — that is how a
    // transient error becomes 400 duplicate clinical records.
    [Fact]
    public async Task AFailedDedupeCheckWarnsRatherThanAssumingNoDuplicates()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService { FailMatchLookup = true };
        var model = Build(people);

        await model.DryRunAsync(_folder);

        Assert.Contains("could not check", model.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Committing ----

    [Fact]
    public async Task CommittingCreatesOneConsumerPerReadyFile()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        WriteExport("beta.html", "1002", "BETA");
        var people = new RecordingPersonService();
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);

        Assert.Equal(2, people.Created.Count);
        Assert.Equal(["ALPHA", "BETA"], people.Created.Select(p => p.FirstName).Order());
        Assert.Contains("2 consumers created", model.StatusMessage);
    }

    [Fact]
    public async Task TheCreatedConsumerCarriesTheMappedFieldsAndTheCredibleId()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService();
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);

        var created = Assert.Single(people.Created);
        Assert.Equal("ALPHA", created.FirstName);
        Assert.Equal("1001", created.CredibleClientId);
        Assert.Equal("12345678A", created.MaineCareId);
        Assert.Equal(new DateTime(1990, 1, 2), created.BirthDate);
    }

    // An imported effective date would generate a full compliance cycle per consumer, on the
    // caseload-load path that already struggles. It is set at distribution instead.
    [Fact]
    public async Task NoEffectiveDateIsSetSoNoComplianceFormsAreGenerated()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService();
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(people.Created).EffectiveDate);
    }

    // Consumers land on the importer, who then distributes. The transfer route and the
    // distribution screen are what move them on from here.
    [Fact]
    public async Task ImportedConsumersLandOnTheImportersOwnCaseload()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService();
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);

        Assert.Equal(41, Assert.Single(people.Created).UserId);
    }

    // Per-record, not a batch: one bad record must not abandon the good ones, nor vanish.
    [Fact]
    public async Task OneFailedCreateLeavesTheOthersCreatedAndIsReportedOnItsOwnRow()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        WriteExport("beta.html", "1002", "BETA");
        var people = new RecordingPersonService { FailForFirstName = "BETA" };
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);

        Assert.Single(people.Created);
        var failedRow = model.Rows.Single(row => row.FileName == "beta.html");
        Assert.True(failedRow.Failed);
        Assert.Contains("1 created", model.StatusMessage);
        Assert.Contains("1 could not be", model.StatusMessage);
    }

    [Fact]
    public async Task CommittingTwiceDoesNotCreateTheSameConsumerAgain()
    {
        WriteExport("alpha.html", "1001", "ALPHA");
        var people = new RecordingPersonService();
        var model = Build(people);
        await model.DryRunAsync(_folder);

        await model.CommitCommand.ExecuteAsync(null);
        await model.CommitCommand.ExecuteAsync(null);

        Assert.Single(people.Created);
    }

    // ---- Helpers ----

    private CaseloadImportViewModel Build(IPersonService people)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            41, "supervisor", "Supervisor", "hash", "salt",
            UserRole.Supervisor, null, 7));
        return new CaseloadImportViewModel(
            new CredibleExportReader(), new StubPicker(), people, session);
    }

    private void WriteExport(
        string fileName, string clientId, string firstName,
        string lastName = "TEST", string maineCareId = "12345678A", string dob = "01/02/1990") =>
        File.WriteAllText(Path.Combine(_folder, fileName),
            $$"""
            <html><body><form action="./client_printview.aspx?client_id={{clientId}}"><table>
              <tr><td class="shc" colspan="4"><b>CONSUMER INFO</b></td></tr>
              <tr>
                <td class="lc"><b>First Name</b></td><td class="vc">{{firstName}}</td>
                <td class="lc"><b>Last Name</b></td><td class="vc">{{lastName}}</td>
              </tr>
              <tr>
                <td class="lc"><b>MaineCare ID</b></td><td class="vc">{{maineCareId}}</td>
                <td class="lc"><b>DOB</b></td><td class="vc">{{dob}}</td>
              </tr>
              <tr><td class="lc"><b>Consumer ID</b></td><td class="vc">{{clientId}}</td></tr>
            </table></form></body></html>
            """);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private sealed class StubPicker : IExportFilePicker
    {
        public string? PickExportFile() => null;
        public string? PickExportFolder() => null;
    }

    private sealed class RecordingPersonService : IPersonService
    {
        public List<Person> Created { get; } = [];
        public List<CredibleClientMatchDto> ExistingMatches { get; } = [];
        public List<MaineCareIdMatchDto> ExistingMaineCareMatches { get; } = [];
        public List<NameBirthDateMatchDto> ExistingNameBirthDateMatches { get; } = [];
        public bool FailMatchLookup { get; set; }
        public string? FailForFirstName { get; set; }

        public Task<Person> AddPersonAsync(Person person)
        {
            if (FailForFirstName is not null && person.FirstName == FailForFirstName)
                return Task.FromException<Person>(new InvalidOperationException("simulated failure"));

            Created.Add(person);
            // Mirrors the real world: once created, its identifiers are ones the agency holds.
            if (person.CredibleClientId is not null)
                ExistingMatches.Add(new CredibleClientMatchDto(person.CredibleClientId, "Supervisor"));
            if (person.MaineCareId is not null)
                ExistingMaineCareMatches.Add(new MaineCareIdMatchDto(person.MaineCareId, "Supervisor"));
            if (person.LastName is not null && person.FirstName is not null)
                ExistingNameBirthDateMatches.Add(new NameBirthDateMatchDto(
                    new PersonNameBirthDate(person.LastName, person.FirstName, person.BirthDate),
                    "Supervisor"));
            return Task.FromResult(person);
        }

        public Task<CredibleMatchLookupResult> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds,
            IReadOnlyList<string>? maineCareIds = null,
            IReadOnlyList<PersonNameBirthDate>? nameBirthDates = null)
        {
            if (FailMatchLookup)
                return Task.FromException<CredibleMatchLookupResult>(
                    new InvalidOperationException("lookup unavailable"));

            var mcIds = maineCareIds ?? [];
            var names = nameBirthDates ?? [];
            return Task.FromResult(new CredibleMatchLookupResult(
                ExistingMatches
                    .Where(match => credibleClientIds.Contains(match.CredibleClientId))
                    .ToList(),
                ExistingMaineCareMatches
                    .Where(match => mcIds.Contains(match.MaineCareId))
                    .ToList(),
                ExistingNameBirthDateMatches
                    .Where(match => names.Any(candidate =>
                        string.Equals(candidate.LastName, match.NameBirthDate.LastName,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.FirstName, match.NameBirthDate.FirstName,
                            StringComparison.OrdinalIgnoreCase) &&
                        candidate.BirthDate.Date == match.NameBirthDate.BirthDate.Date))
                    .ToList()));
        }

        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            throw new NotSupportedException();
        public Task<string?> GetJournalAsync(int personId) => throw new NotSupportedException();
        public Task SaveJournalAsync(int personId, string? journal) => throw new NotSupportedException();
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            throw new NotSupportedException();
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId, int targetUserId, int expectedRevision) => throw new NotSupportedException();
        public Task<PersonStatusDto> SetPersonStatusAsync(
            int personId, string status, string? note, int expectedRevision) =>
            throw new NotSupportedException();
    }
}
