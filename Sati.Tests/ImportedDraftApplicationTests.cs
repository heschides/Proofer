using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Applying an accepted Credible import to the new-client form.
///
/// <para>
/// The load-bearing property of the whole import design lives here: it fills the form and does
/// not write. <c>Submit</c> stays the only writer, so an imported consumer passes through
/// exactly the validation, ownership rule, versioning and audit a typed one does. That matters
/// most in local Production, where nothing sits between <c>NewClientViewModel</c> and SQL
/// Server — an import path that wrote directly would be entirely unguarded there.
/// </para>
/// </summary>
public sealed class ImportedDraftApplicationTests
{
    // The test the rest of the design rests on.
    [Fact]
    public void ApplyingADraftFillsTheFormAndSavesNothing()
    {
        var people = new CountingPersonService();
        var model = CreateViewModel(people);

        model.ApplyImportedDraft(Draft());

        Assert.Equal("CREDIBLE", model.FirstName);
        Assert.Equal("TEST", model.LastName);
        Assert.Equal(0, people.AddCalls);
        Assert.Equal(0, people.EditCalls);
    }

    [Fact]
    public void EveryAcceptedDemographicFieldLandsOnTheForm()
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft());

        Assert.Equal(new DateTime(1990, 1, 2), model.BirthDate);
        Assert.Equal(Gender.Male, model.Gender);
        Assert.Equal("12345678A", model.MaineCareId);
        Assert.Equal("F84.0", model.DiagnosisCode);
        Assert.Equal("3016529500", model.PhoneNumber);
        Assert.Equal("demo@credibleinc.test", model.Email);
        Assert.Equal("1 Choice Hotels Circle", model.BillingStreet);
        Assert.Equal("Alexander", model.BillingCity);
        Assert.Equal("MD", model.BillingState);
        Assert.Equal("20850", model.BillingZip);
        Assert.Equal("21864", model.CredibleClientId);
    }

    // Credible reports "Consumer is Own Guardian?", which the mapper has already inverted. This
    // pins the receiving end: "true" here must mean the consumer HAS a guardian.
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void TheGuardianFlagArrivesInSatisSense(string mapped, bool expected)
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft(hasGuardian: mapped));

        Assert.Equal(expected, model.HasGuardian);
    }

    [Fact]
    public void TheGuardiansTwoNameCellsBecomeOneName()
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft());

        Assert.Equal("Bob Jones", model.GuardianName);
    }

    // An imported effective date would silently generate a full compliance cycle for every
    // consumer in a batch, on the caseload-load path that already struggles. It is set later, by
    // somebody who knows the case. See CREDIBLE_IMPORT_DESIGN.md.
    [Fact]
    public void NoEffectiveDateIsSetByAnImport()
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft());

        Assert.True(string.IsNullOrEmpty(model.EffectiveDateText));
    }

    // The demographic save must not carry an SSN, and it cannot be applied before the consumer
    // exists: the value is encrypted against the person's id and agency as additional
    // authenticated data, so there is nothing to bind it to yet.
    [Fact]
    public void TheSsnIsHeldApartFromTheFormRatherThanFilledIn()
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft());

        Assert.Equal("000001800", model.PendingImportedSsn);
    }

    // Only accepted fields are present in the draft, so a field the reviewer declined must not
    // blank out whatever the user already typed.
    [Fact]
    public void AFieldAbsentFromTheDraftLeavesTheFormAlone()
    {
        var model = CreateViewModel(new CountingPersonService());
        model.MaineCareId = "TYPED-BY-HAND";

        model.ApplyImportedDraft(new AcceptedImportDraft(
            new Dictionary<string, string> { [CredibleFields.FirstName] = "CREDIBLE" },
            Ssn: null,
            CredibleClientId: null));

        Assert.Equal("CREDIBLE", model.FirstName);
        Assert.Equal("TYPED-BY-HAND", model.MaineCareId);
    }

    [Fact]
    public void ApplyingADraftOpensTheEntryPanelForCreation()
    {
        var model = CreateViewModel(new CountingPersonService());

        model.ApplyImportedDraft(Draft());

        Assert.True(model.IsClientEditorOpen);
        Assert.False(model.IsEditMode);
    }

    // ---- Helpers ----

    private static AcceptedImportDraft Draft(string hasGuardian = "true") =>
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CredibleFields.FirstName] = "CREDIBLE",
                [CredibleFields.LastName] = "TEST",
                [CredibleFields.BirthDate] = "1990-01-02",
                [CredibleFields.Gender] = "Male",
                [CredibleFields.MaineCareId] = "12345678A",
                [CredibleFields.DiagnosisCode] = "F84.0",
                [CredibleFields.HasGuardian] = hasGuardian,
                [CredibleFields.GuardianFirstName] = "Bob",
                [CredibleFields.GuardianLastName] = "Jones",
                [CredibleFields.PhoneNumber] = "3016529500",
                [CredibleFields.Email] = "demo@credibleinc.test",
                [CredibleFields.BillingStreet] = "1 Choice Hotels Circle",
                [CredibleFields.BillingCity] = "Alexander",
                [CredibleFields.BillingState] = "MD",
                [CredibleFields.BillingZip] = "20850",
                [CredibleFields.CredibleClientId] = "21864",
            },
            Ssn: "000001800",
            CredibleClientId: "21864");

    private static NewClientViewModel CreateViewModel(IPersonService personService)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            41, "case-manager", "Case Manager", "hash", "salt",
            UserRole.CaseManager, null, 7));

        return new NewClientViewModel(
            personService,
            session,
            null!,
            null!,
            new StubSettingsService(),
            null!,
            null!,
            null!,
            new SilentIncidentReporter(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class SilentIncidentReporter : IIncidentReporter
    {
        public Task ReportAsync(
            Exception exception, string operation, string reference,
            string severity = "Error", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingPersonService : IPersonService
    {
        public int AddCalls { get; private set; }
        public int EditCalls { get; private set; }

        public Task<Person> AddPersonAsync(Person person)
        {
            AddCalls++;
            return Task.FromResult(person);
        }

        public Task<Person> EditPersonAsync(Person person)
        {
            EditCalls++;
            return Task.FromResult(person);
        }

        public Task<List<Person>> GetAllPeopleAsync(int userId) =>
            Task.FromResult(new List<Person>());
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            Task.FromResult(new List<PersonSummary>());
        public Task<string?> GetJournalAsync(int personId) => Task.FromResult<string?>(null);
        public Task SaveJournalAsync(int personId, string? journal) => Task.CompletedTask;
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            Task.FromResult(new JournalReminderResult(text));
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CredibleClientMatchDto>> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds) =>
            Task.FromResult<IReadOnlyList<CredibleClientMatchDto>>([]);
    }
}
