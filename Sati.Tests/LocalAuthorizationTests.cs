using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Xunit;

namespace Sati.Tests;

public sealed class LocalAuthorizationTests
{
    [Fact]
    public async Task AssessmentWritesRequireTheSignedInAssignedCaseManager()
    {
        await using var fixture = await LocalFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new ComprehensiveAssessmentService(fixture.Factory, session);

        var assessment = await service.GetOrCreateDraftAsync(
            fixture.PersonOneId,
            fixture.CaseManagerOne.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetOrCreateDraftAsync(fixture.UnassignedPersonId, fixture.CaseManagerOne.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetOrCreateDraftAsync(fixture.ForeignPersonId, fixture.CaseManagerOne.Id));

        await service.SaveDocumentAsync(
            assessment,
            new AssessmentDocument
            {
                Contributors = [new AssessmentContributor { Name = "Reviewer", Relationship = "Team" }]
            });
        await service.SubmitForReviewAsync(assessment);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.ComprehensiveAssessments.SingleAsync(candidate => candidate.Id == assessment.Id);
        var events = await db.AuditEvents
            .Where(candidate => candidate.AgencyId == fixture.CaseManagerOne.AgencyId &&
                candidate.ResourceType == "ComprehensiveAssessment")
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        Assert.Equal(AssessmentStatus.ReadyForReview, stored.Status);
        Assert.Equal(3, stored.Revision);
        Assert.Equal(
            ["assessment.created", "assessment.updated", "assessment.submitted"],
            events.Select(candidate => candidate.Action).ToArray());
        Assert.All(events, candidate => Assert.Equal(fixture.CaseManagerOne.Id, candidate.ActorUserId));
    }

    [Fact]
    public async Task AssessmentCallerCannotImpersonateItsStoredAuthor()
    {
        await using var fixture = await LocalFixture.CreateAsync();
        var authorSession = new SessionService();
        authorSession.SetUser(fixture.CaseManagerOne);
        var authorService = new ComprehensiveAssessmentService(fixture.Factory, authorSession);
        var assessment = await authorService.GetOrCreateDraftAsync(
            fixture.PersonOneId,
            fixture.CaseManagerOne.Id);

        var otherSession = new SessionService();
        otherSession.SetUser(fixture.CaseManagerTwo);
        var otherService = new ComprehensiveAssessmentService(fixture.Factory, otherSession);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            otherService.SaveDocumentAsync(assessment, new AssessmentDocument()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            otherService.SubmitForReviewAsync(assessment));
    }

    [Fact]
    public async Task SupervisorAllScopeNeverCrossesAssignmentOrAgency()
    {
        await using var fixture = await LocalFixture.CreateAsync();
        var supervisorSession = new SessionService();
        supervisorSession.SetUser(fixture.SupervisorOne);
        var supervisorService = new SupervisorService(fixture.Factory, supervisorSession);

        var supervised = (await supervisorService.GetPendingNotesAsync(fixture.SupervisorOne.Id, true))
            .Concat(await supervisorService.GetNonCompliantNotesAsync(fixture.SupervisorOne.Id, true))
            .Select(candidate => candidate.Id)
            .Distinct()
            .ToList();

        Assert.Contains(fixture.NoteOneId, supervised);
        Assert.DoesNotContain(fixture.UnassignedNoteId, supervised);
        Assert.DoesNotContain(fixture.ForeignNoteId, supervised);

        var adminSession = new SessionService();
        adminSession.SetUser(fixture.AdminOne);
        var adminService = new SupervisorService(fixture.Factory, adminSession);
        var agencyWide = (await adminService.GetPendingNotesAsync(fixture.AdminOne.Id, true))
            .Concat(await adminService.GetNonCompliantNotesAsync(fixture.AdminOne.Id, true))
            .Select(candidate => candidate.Id)
            .Distinct()
            .ToList();
        Assert.Contains(fixture.NoteOneId, agencyWide);
        Assert.Contains(fixture.UnassignedNoteId, agencyWide);
        Assert.DoesNotContain(fixture.ForeignNoteId, agencyWide);
    }

    [Fact]
    public async Task SupervisorActionsRequireSessionIdentityAndReviewScope()
    {
        await using var fixture = await LocalFixture.CreateAsync();
        var session = new SessionService();
        session.SetUser(fixture.SupervisorOne);
        var service = new SupervisorService(fixture.Factory, session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetPendingNotesAsync(fixture.SupervisorTwo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReturnNoteAsync(fixture.UnassignedNoteId, fixture.SupervisorOne.Id, "Not mine", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReturnNoteAsync(fixture.ForeignNoteId, fixture.SupervisorOne.Id, "Foreign", 1));

        await service.ReturnNoteAsync(
            fixture.NoteOneId,
            fixture.SupervisorOne.Id,
            "Please clarify the documented intervention.",
            1);

        await using var db = fixture.Factory.CreateDbContext();
        var note = await db.Notes.SingleAsync(candidate => candidate.Id == fixture.NoteOneId);
        var audit = await db.AuditEvents.SingleAsync(candidate =>
            candidate.Action == "note.returned" && candidate.ResourceId == fixture.NoteOneId.ToString());
        Assert.Equal(NoteStatus.Returned, note.Status);
        Assert.Equal(fixture.SupervisorOne.Id, note.ReturnedById);
        Assert.Equal(fixture.SupervisorOne.AgencyId, audit.AgencyId);
        Assert.Equal(fixture.SupervisorOne.Id, audit.ActorUserId);
    }

    private sealed class LocalFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private LocalFixture(SqliteConnection connection, DbContextOptions<SatiContext> options)
        {
            _connection = connection;
            Factory = new TestContextFactory(options);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User SupervisorOne { get; private set; } = null!;
        public User SupervisorTwo { get; private set; } = null!;
        public User AdminOne { get; private set; } = null!;
        public User CaseManagerOne { get; private set; } = null!;
        public User CaseManagerTwo { get; private set; } = null!;
        public int PersonOneId { get; private set; }
        public int UnassignedPersonId { get; private set; }
        public int ForeignPersonId { get; private set; }
        public int NoteOneId { get; private set; }
        public int UnassignedNoteId { get; private set; }
        public int ForeignNoteId { get; private set; }

        public static async Task<LocalFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var fixture = new LocalFixture(connection, options);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(
                new Agency { Id = 101, Name = "Agency One" },
                new Agency { Id = 102, Name = "Agency Two" });

            SupervisorOne = User.Create(1101, "supervisor-one-local", "Supervisor One", "hash", "salt", UserRole.Supervisor, null, 101);
            SupervisorTwo = User.Create(1201, "supervisor-two-local", "Supervisor Two", "hash", "salt", UserRole.Supervisor, null, 102);
            AdminOne = User.Create(1102, "admin-one-local", "Admin One", "hash", "salt", UserRole.Admin, null, 101);
            CaseManagerOne = User.Create(1111, "case-manager-one-local", "Case Manager One", "hash", "salt", UserRole.CaseManager, SupervisorOne.Id, 101);
            var unassignedCaseManager = User.Create(1112, "case-manager-unassigned-local", "Unassigned Case Manager", "hash", "salt", UserRole.CaseManager, null, 101);
            CaseManagerTwo = User.Create(1211, "case-manager-two-local", "Case Manager Two", "hash", "salt", UserRole.CaseManager, SupervisorTwo.Id, 102);
            db.Users.AddRange(SupervisorOne, SupervisorTwo, AdminOne, CaseManagerOne, unassignedCaseManager, CaseManagerTwo);

            var personOne = CreatePerson(CaseManagerOne.Id, 101, "Owned");
            var unassignedPerson = CreatePerson(unassignedCaseManager.Id, 101, "Unassigned");
            var foreignPerson = CreatePerson(CaseManagerTwo.Id, 102, "Foreign");
            db.People.AddRange(personOne, unassignedPerson, foreignPerson);
            await db.SaveChangesAsync();
            PersonOneId = personOne.Id;
            UnassignedPersonId = unassignedPerson.Id;
            ForeignPersonId = foreignPerson.Id;

            var noteOne = CreateLoggedNote(personOne, 101, "Owned note");
            var unassignedNote = CreateLoggedNote(unassignedPerson, 101, "Unassigned note");
            var foreignNote = CreateLoggedNote(foreignPerson, 102, "Foreign note");
            db.Notes.AddRange(noteOne, unassignedNote, foreignNote);
            await db.SaveChangesAsync();
            NoteOneId = noteOne.Id;
            UnassignedNoteId = unassignedNote.Id;
            ForeignNoteId = foreignNote.Id;
        }

        private static Person CreatePerson(int userId, int agencyId, string firstName)
        {
            var person = Person.CreatePerson(
                userId,
                firstName,
                "Person",
                string.Empty,
                new DateTime(1990, 1, 1),
                DateTime.Today.AddYears(-1),
                WaiverType.Section21,
                new Settings());
            person.AgencyId = agencyId;
            return person;
        }

        private static Note CreateLoggedNote(Person person, int agencyId, string narrative)
        {
            var note = Note.Create(
                narrative,
                DateTime.Today,
                NoteStatus.Logged,
                30,
                person.Id,
                noteType: NoteType.Visit);
            note.Person = person;
            note.AgencyId = agencyId;
            return note;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class TestContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);

        public Task<SatiContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
