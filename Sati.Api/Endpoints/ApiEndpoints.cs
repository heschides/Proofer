using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Api.Endpoints;

internal static class ApiEndpoints
{
    public static void MapSatiApi(this WebApplication app)
    {
        MapAuth(app);
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .AddEndpointFilter<ValidatedActorFilter>();
        MapProfile(api);
        MapAudit(api);
        MapAdmin(api);
        MapUsers(api);
        MapSupervisor(api);
        MapCaseload(api);
        MapPeople(api);
        MapReviews(api);
        MapAssessments(api);
        MapProviders(api);
        MapAtRequests(api);
        MapAiContext(api);
        MapNotes(api);
        MapSettings(api);
        MapScratchpads(api);
        MapExemptDates(api);
        MapIncentives(api);
        MapReports(api);
        MapBilling(api);
        MapForms(api);
        MapIncidents(api);
    }

    private static void MapIncidents(RouteGroupBuilder api)
    {
        api.MapPost("/incidents", async Task<IResult> (
            IncidentReportRequest request,
            ClaimsPrincipal principal,
            IncidentAggregator aggregator,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var validation = ValidateIncidentReport(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);

            var occurredAt = DateTime.SpecifyKind(request.OccurredAtUtc, DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            if (occurredAt < now.AddDays(-90) || occurredAt > now.AddMinutes(5))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["occurredAtUtc"] = ["Incident time must be within the last 90 days and not in the future."]
                });
            }

            var incident = await aggregator.UpsertAsync(new IncidentAggregation(
                actor.AgencyId,
                actor.Role == "PlatformOperator" ? IncidentScopes.Platform : IncidentScopes.Agency,
                request.Source,
                request.Severity,
                request.Operation,
                request.Release,
                request.ExceptionFingerprint,
                occurredAt,
                request.Reference,
                actor.Role), cancellationToken);
            return Results.Accepted(value: ToIncidentDto(incident));
        });

        api.MapGet("/admin/incidents", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var window = days ?? 30;
            var limit = take ?? 250;
            if (window is < 1 or > 90 || limit is < 1 or > 500)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Use 1-90 days and request 1-500 rows."] });

            var start = DateTime.UtcNow.AddDays(-window);
            var incidents = await db.IncidentGroups.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId &&
                                    candidate.Scope == IncidentScopes.Agency &&
                                    candidate.LastSeenUtc >= start)
                .OrderByDescending(candidate => candidate.LastSeenUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var dtos = incidents.Select(ToIncidentDto).ToList();
            return Results.Ok(new AdminIncidentDashboardDto(
                DateTime.UtcNow,
                IncidentHealthScoring.Calculate(dtos, DateTime.UtcNow, window),
                dtos));
        });

        api.MapPut("/admin/incidents/{incidentId:long}/status", async Task<IResult> (
            long incidentId,
            UpdateIncidentStatusRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            if (request.Status is not ("Open" or "Investigating" or "Resolved"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Status must be Open, Investigating, or Resolved."] });
            var incident = await db.IncidentGroups.SingleOrDefaultAsync(candidate =>
                candidate.Id == incidentId && candidate.AgencyId == actor.AgencyId &&
                candidate.Scope == IncidentScopes.Agency,
                cancellationToken);
            if (incident is null)
                return Results.NotFound();
            incident.Status = request.Status;
            auditTrail.Record(actor, AuditActions.IncidentStatusUpdated, "IncidentGroup", metadataJson:
                JsonSerializer.Serialize(new { incidentId, status = request.Status }));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToIncidentDto(incident));
        });

        api.MapGet("/platform/incidents", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "PlatformOperator")
                return Results.Forbid();
            var window = days ?? 30;
            var limit = take ?? 500;
            if (window is < 1 or > 90 || limit is < 1 or > 1000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Use 1-90 days and request 1-1000 rows."] });

            var observedAt = DateTime.UtcNow;
            var start = observedAt.AddDays(-window);
            var incidents = await db.IncidentGroups.AsNoTracking()
                .Where(candidate => candidate.LastSeenUtc >= start)
                .OrderByDescending(candidate => candidate.LastSeenUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var dtos = incidents.Select(ToIncidentDto).ToList();
            var agencies = await db.Agencies.AsNoTracking().OrderBy(candidate => candidate.Name)
                .Select(candidate => new { candidate.Id, candidate.Name })
                .ToListAsync(cancellationToken);
            var agencyHealth = agencies.Select(agency => new PlatformAgencyHealthDto(
                agency.Id,
                agency.Name,
                IncidentHealthScoring.Calculate(dtos.Where(item =>
                    item.AgencyId == agency.Id && item.Scope == IncidentScopes.Agency), observedAt, window)))
                .ToList();
            auditTrail.Record(actor, AuditActions.PlatformIncidentsViewed, "IncidentGroup");
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new PlatformIncidentDashboardDto(
                observedAt,
                IncidentHealthScoring.Calculate(dtos, observedAt, window),
                agencyHealth,
                dtos));
        });
    }

    private static void MapAudit(RouteGroupBuilder api)
    {
        api.MapGet("/audit-events", async Task<IResult> (
            DateTime? from,
            DateTime? to,
            string? action,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var start = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
            var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
            var limit = take ?? 100;
            if (end < start || (end - start).TotalDays > 366 || limit is < 1 or > 500 || action?.Length > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Use a valid window no longer than one year, an action up to 100 characters, and a take value from 1 to 500."]
                });
            }

            var query = db.AuditEvents.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId &&
                                    candidate.OccurredAtUtc >= start &&
                                    candidate.OccurredAtUtc <= end);
            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(candidate => candidate.Action == action);

            var events = await query
                .OrderByDescending(candidate => candidate.OccurredAtUtc)
                .ThenByDescending(candidate => candidate.Id)
                .Take(limit)
                .Select(candidate => new AuditEventDto(
                    candidate.Id,
                    candidate.EventId,
                    candidate.AgencyId,
                    candidate.ActorUserId,
                    candidate.Action,
                    candidate.ResourceType,
                    candidate.ResourceId,
                    candidate.OccurredAtUtc,
                    candidate.CorrelationId))
                .ToListAsync(cancellationToken);
            return Results.Ok(events);
        });
    }

    private static void MapAdmin(RouteGroupBuilder api)
    {
        api.MapGet("/admin/overview", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var now = DateTime.UtcNow;
            var today = now.Date;
            var thirtyDaysAgo = now.AddDays(-30);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var agencyName = await db.Agencies.AsNoTracking()
                .Where(agency => agency.Id == actor.AgencyId)
                .Select(agency => agency.Name)
                .SingleAsync(cancellationToken);
            var userCount = await db.Users.AsNoTracking()
                .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role != "PlatformOperator", cancellationToken);
            var caseManagerCount = await db.Users.AsNoTracking()
                .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role == "CaseManager", cancellationToken);
            var personCount = await db.People.AsNoTracking()
                .CountAsync(person => person.AgencyId == actor.AgencyId &&
                    db.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
                    cancellationToken);
            var notesThisMonth = await db.Notes.AsNoTracking()
                .CountAsync(note => note.EventDate >= monthStart &&
                    db.People.Any(person => person.Id == note.PersonId && person.AgencyId == actor.AgencyId &&
                        db.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId)),
                    cancellationToken);
            var activeUsers = await db.AuditEvents.AsNoTracking()
                .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= thirtyDaysAgo)
                .Select(auditEvent => auditEvent.ActorUserId)
                .Distinct()
                .CountAsync(cancellationToken);
            var successfulSignIns = await db.AuditEvents.AsNoTracking()
                .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= thirtyDaysAgo &&
                    auditEvent.Action == AuditActions.AuthenticationSucceeded,
                    cancellationToken);
            var personChanges = await db.PersonVersions.AsNoTracking()
                .CountAsync(version => version.AgencyId == actor.AgencyId &&
                    version.ChangedAtUtc >= thirtyDaysAgo && version.ChangeKind != "TrackingBaseline",
                    cancellationToken);
            var auditEventsToday = await db.AuditEvents.AsNoTracking()
                .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                    auditEvent.OccurredAtUtc >= today,
                    cancellationToken);
            var lastActivity = await db.AuditEvents.AsNoTracking()
                .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId)
                .MaxAsync(auditEvent => (DateTime?)auditEvent.OccurredAtUtc, cancellationToken);

            return Results.Ok(new AdminOverviewDto(
                actor.AgencyId, agencyName, userCount, caseManagerCount, personCount,
                notesThisMonth, activeUsers, successfulSignIns, personChanges,
                auditEventsToday, lastActivity));
        });

        api.MapGet("/admin/operations", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            Microsoft.Extensions.Options.IOptions<SatiApiOptions> options,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            PreventSensitiveResponseCaching(httpContext);
            var auditCount = await db.AuditEvents.AsNoTracking()
                .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
            var ediCount = await db.EdiGenerations.AsNoTracking()
                .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
            var ediCharacters = await db.EdiGenerations.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .SumAsync(candidate => (long?)candidate.Content.Length, cancellationToken) ?? 0;
            var oldestAudit = await db.AuditEvents.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .MinAsync(candidate => (DateTime?)candidate.OccurredAtUtc, cancellationToken);
            var oldestEdi = await db.EdiGenerations.AsNoTracking()
                .Where(candidate => candidate.AgencyId == actor.AgencyId)
                .MinAsync(candidate => (DateTime?)candidate.CreatedAtUtc, cancellationToken);

            return Results.Ok(new AdminOperationsDto(
                DateTime.UtcNow,
                "Healthy",
                "PolicyOnly",
                options.Value.AuditRetentionDays,
                options.Value.EdiReplayRetentionDays,
                auditCount,
                ediCount,
                ediCharacters,
                oldestAudit,
                oldestEdi));
        });

        // Schema drift detail, for the reconciliation that has to classify each
        // discrepancy before it can be fixed. `/health/ready` reports only a status
        // word, and its description never reaches the anonymous response writer, so
        // without this the drift is visible only in the API's own logs.
        //
        // The report names tables and columns, never row data, so it carries no
        // consumer information. It is still Admin-gated: schema shape is
        // operational detail about the deployment, not something every signed-in
        // case manager needs.
        api.MapGet("/admin/schema-drift", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            // The chain is passed empty on purpose. Every migration belongs to
            // SatiContext in the desktop project; this model has no chain of its
            // own, so it reports the applied ids as data and leaves the verdict to
            // a caller that owns the chain.
            return Results.Ok(SchemaComparison.Report(
                SchemaSnapshotReader.FromModel(db.Model, "The API model", describesEveryTable: false),
                await SchemaSnapshotReader.ReadDatabaseAsync(db, "the database", cancellationToken),
                chainMigrationIds: [],
                await SchemaSnapshotReader.ReadAppliedMigrationsAsync(db, cancellationToken)));
        });

        api.MapGet("/admin/people", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var people = await (
                from person in db.People.AsNoTracking()
                join user in db.Users.AsNoTracking() on person.UserId equals user.Id
                where person.AgencyId == actor.AgencyId && user.AgencyId == actor.AgencyId
                orderby person.LastName, person.FirstName, person.Id
                select new AdminPersonListItemDto(
                    person.Id,
                    ((person.LastName ?? string.Empty) + ", " + (person.FirstName ?? string.Empty)).Trim(' ', ','),
                    person.Revision,
                    user.Id,
                    user.DisplayName,
                    person.IsTestData))
                .ToListAsync(cancellationToken);
            return Results.Ok(people);
        });

        api.MapPost("/admin/test-data/consumers/{personId:int}/delete", async Task<IResult> (
            int personId,
            DeleteTestConsumerRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            if (personId <= 0 || request.ExpectedRevision <= 0)
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_test_consumer",
                    "Select a current consumer record and try again.",
                    string.Empty));
            }
            if (!TestDataDeletionRules.HasValidConsumerAttestation(request.Attestation))
            {
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_test_data_attestation",
                    "The required test-data affirmation was not supplied.",
                    string.Empty));
            }

            PreventSensitiveResponseCaching(httpContext);
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == personId && candidate.AgencyId == actor.AgencyId &&
                db.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId),
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (!person.IsTestData)
            {
                return Results.Conflict(new ApiErrorDto(
                    "consumer_not_test_data",
                    "This consumer was not marked as Test when created and cannot be deleted with the test-data tool.",
                    string.Empty));
            }
            if (person.Revision != request.ExpectedRevision)
                return StaleTestConsumerConflict();

            var claimLineCount = await db.ClaimLines.AsNoTracking().CountAsync(claimLine =>
                db.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId),
                cancellationToken);
            if (claimLineCount > 0)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_has_claims",
                    TestDataDeletionRules.ConsumerHasClaimsMessage,
                    string.Empty));
            }

            try
            {
                var appointmentsDeleted = await db.Appointments
                    .Where(appointment => db.ReviewItems.Any(review =>
                        review.Id == appointment.ReviewItemId && review.PersonId == personId))
                    .ExecuteDeleteAsync(cancellationToken);
                var reviewsDeleted = await db.ReviewItems
                    .Where(review => review.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var contactsDeleted = await db.PersonContacts
                    .Where(contact => contact.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var personProvidersDeleted = await db.PersonProviders
                    .Where(link => link.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var formsDeleted = await db.Forms
                    .Where(form => form.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var atRequestItemsDeleted = await db.AtRequestItems
                    .Where(item => db.AtRequests.Any(atRequest =>
                        atRequest.Id == item.ATRequestId && atRequest.PersonId == personId))
                    .ExecuteDeleteAsync(cancellationToken);
                var atRequestsDeleted = await db.AtRequests
                    .Where(atRequest => atRequest.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var assessmentsDeleted = await db.ComprehensiveAssessments
                    .Where(assessment => assessment.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);
                var notesDeleted = await db.Notes
                    .Where(note => note.PersonId == personId)
                    .ExecuteDeleteAsync(cancellationToken);

                // PersonVersion is normally append-only. Test-data deletion is the
                // one narrow exception because each snapshot contains a copy of the
                // synthetic consumer record. AuditEvent remains append-only.
                var personVersionsDeleted = await db.PersonVersions
                    .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                    .ExecuteDeleteAsync(cancellationToken);

                var peopleDeleted = await db.People
                    .Where(candidate => candidate.Id == personId &&
                        candidate.Revision == request.ExpectedRevision &&
                        candidate.AgencyId == actor.AgencyId && candidate.IsTestData &&
                        db.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
                    .ExecuteDeleteAsync(cancellationToken);
                if (peopleDeleted != 1)
                    return StaleTestConsumerConflict();

                var result = new TestConsumerDeletionResultDto(
                    personId,
                    formsDeleted,
                    notesDeleted,
                    contactsDeleted,
                    reviewsDeleted,
                    appointmentsDeleted,
                    assessmentsDeleted,
                    atRequestsDeleted,
                    atRequestItemsDeleted,
                    personVersionsDeleted,
                    personProvidersDeleted);
                auditTrail.Record(
                    actor,
                    AuditActions.TestConsumerDeleted,
                    "Person",
                    personId,
                    JsonSerializer.Serialize(new
                    {
                        attestationVersion = TestDataDeletionRules.ConsumerAttestation,
                        relatedRecordsDeleted = result.RelatedRecordsDeleted,
                        formsDeleted = result.FormsDeleted,
                        notesDeleted = result.NotesDeleted,
                        contactsDeleted = result.ContactsDeleted,
                        personProvidersDeleted = result.PersonProvidersDeleted,
                        reviewsDeleted = result.ReviewsDeleted,
                        appointmentsDeleted = result.AppointmentsDeleted,
                        assessmentsDeleted = result.AssessmentsDeleted,
                        atRequestsDeleted = result.AtRequestsDeleted,
                        atRequestItemsDeleted = result.AtRequestItemsDeleted,
                        personVersionsDeleted = result.PersonVersionsDeleted
                    }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(result);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_related_record_changed",
                    "The consumer was not deleted because a related record changed or is protected. Refresh and try again, or seek guidance in the help menu.",
                    string.Empty));
            }
            catch (DbException)
            {
                return Results.Conflict(new ApiErrorDto(
                    "test_consumer_related_record_changed",
                    "The consumer was not deleted because a related record changed or is protected. Refresh and try again, or seek guidance in the help menu.",
                    string.Empty));
            }
        });

        // Operational Demo seed only. This is deliberately not a broader permission
        // on the ordinary SSN route: case managers still own day-to-day SSN writes,
        // while this one bounded command lets an agency Admin restore the wholly
        // synthetic Demo dataset without impersonating every case manager.
        api.MapPost("/admin/demo/seed-ssns", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            EnvelopeProtector protector,
            AuditTrail auditTrail,
            IOptions<SatiApiOptions> options,
            IHostEnvironment hostEnvironment,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            // DatabaseIdentityHostedService validates these configured expectations
            // against dbo.SatiDatabaseIdentity at startup. Requiring both values here
            // keeps this synthetic-data command absent in effect on Production even
            // if the same API binary is deployed there later.
            var isValidatedDemo =
                string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal);
            var isIsolatedTestHost =
                hostEnvironment.IsEnvironment("Testing") &&
                string.Equals(options.Value.ExpectedDatabaseName, "SatiApiTests", StringComparison.Ordinal) &&
                string.Equals(options.Value.ExpectedEnvironment, "Testing", StringComparison.Ordinal);
            if (!isValidatedDemo && !isIsolatedTestHost)
                return Results.NotFound();

            var people = await db.People
                .Where(person => person.AgencyId == actor.AgencyId)
                .OrderBy(person => person.Id)
                .ToListAsync(cancellationToken);
            if (people.Count is < 1 or > 9999)
                return Results.Conflict(new ApiErrorDto(
                    "demo_seed_range",
                    "The Demo Person count is outside the supported synthetic SSN seed range.",
                    string.Empty));

            for (var index = 0; index < people.Count; index++)
            {
                var syntheticSsn = $"89999{index + 1:D4}";
                await ProtectSsnAsync(
                    people[index], actor.AgencyId, syntheticSsn, protector, cancellationToken);
                auditTrail.Record(actor, AuditActions.PersonSsnUpdated, "Person", people[index].Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new CountDto(people.Count));
        });

        api.MapGet("/admin/activity", async Task<IResult> (
            int? days,
            int? take,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var windowDays = days ?? 30;
            var limit = take ?? 100;
            if (windowDays is < 1 or > 366 || limit is < 1 or > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["query"] = ["Use a day window from 1 to 366 and a take value from 1 to 500."]
                });
            }

            var start = DateTime.UtcNow.AddDays(-windowDays);
            var activity = await (
                from auditEvent in db.AuditEvents.AsNoTracking()
                join user in db.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                where auditEvent.AgencyId == actor.AgencyId && auditEvent.OccurredAtUtc >= start
                orderby auditEvent.OccurredAtUtc descending, auditEvent.Id descending
                select new AdminActivityDto(
                    auditEvent.Id,
                    auditEvent.ActorUserId,
                    user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                    auditEvent.Action,
                    auditEvent.ResourceType,
                    auditEvent.ResourceId,
                    auditEvent.OccurredAtUtc,
                    auditEvent.CorrelationId))
                .Take(limit)
                .ToListAsync(cancellationToken);
            return Results.Ok(activity);
        });

        api.MapPost("/admin/audit-export.csv", async Task<IResult> (
            AdminAuditExportRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            PreventSensitiveResponseCaching(httpContext);
            var start = request.FromUtc.ToUniversalTime();
            var end = request.ToUtc.ToUniversalTime();
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (end < start || (end - start).TotalDays > 366 || end > DateTime.UtcNow.AddMinutes(5) ||
                reason.Length is < 10 or > 250)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["export"] = ["Use a window no longer than one year ending no later than now and provide a 10-250 character reason."]
                });
            }

            var rows = await (
                from auditEvent in db.AuditEvents.AsNoTracking()
                join user in db.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                where auditEvent.AgencyId == actor.AgencyId &&
                      auditEvent.OccurredAtUtc >= start && auditEvent.OccurredAtUtc <= end
                orderby auditEvent.OccurredAtUtc, auditEvent.Id
                select new AuditExportRow(
                    auditEvent.EventId,
                    auditEvent.OccurredAtUtc,
                    auditEvent.ActorUserId,
                    user == null ? $"User {auditEvent.ActorUserId}" : user.DisplayName,
                    auditEvent.Action,
                    auditEvent.ResourceType,
                    auditEvent.ResourceId,
                    auditEvent.CorrelationId))
                .Take(10_000)
                .ToListAsync(cancellationToken);

            var exportedAt = DateTime.UtcNow;
            var metadata = JsonSerializer.Serialize(new
            {
                fromUtc = start,
                toUtc = end,
                rowCount = rows.Count
            });
            auditTrail.Record(actor, AuditActions.AuditExported, "AuditEvent", metadataJson: metadata);
            await db.SaveChangesAsync(cancellationToken);

            var content = BuildAuditCsv(rows, reason, exportedAt);
            var fileName = $"sati-audit-{start:yyyyMMdd}-{end:yyyyMMdd}.csv";
            return Results.File(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content),
                "text/csv; charset=utf-8",
                fileName);
        });
    }

    private static void MapAuth(WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", async Task<IResult> (
            LoginRequest request,
            ApiDbContext db,
            PasswordVerifier passwordVerifier,
            TokenIssuer tokenIssuer,
            LoginAttemptGuard attemptGuard,
            AuditTrail auditTrail,
            HttpContext context,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var username = request.Username?.Trim() ?? string.Empty;
            if (username.Length is < 1 or > 50 || request.Password is null || request.Password.Length is < 1 or > 1024)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["A valid username and password are required."] });

            if (!attemptGuard.TryAcquire(username, out var retryAfter))
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(
                    new ApiErrorDto(
                        "rate_limited",
                        $"Too many sign-in attempts for this username. Try again in about {retryAfterSeconds} seconds.",
                        context.TraceIdentifier),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var user = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Username == username, cancellationToken);

            // Spend the same key-derivation work whether or not the account
            // exists. Skipping it for an unknown username turns sign-in into a
            // username oracle: a missing account answers in microseconds while a
            // wrong password costs 100,000 PBKDF2 iterations.
            var authenticated = user is null
                ? passwordVerifier.VerifyMissingUser(request.Password)
                : passwordVerifier.Verify(request.Password, user.PasswordHash, user.Salt);

            // VerifyMissingUser never returns true, so a null user cannot reach
            // past here; the explicit null check states that for the compiler and
            // fails closed if that ever changes.
            if (!authenticated || user is null)
            {
                logger.LogWarning("Sati authentication failed from {RemoteAddress}.", context.Connection.RemoteIpAddress);
                return TypedResults.Unauthorized();
            }

            attemptGuard.Reset(username);
            var actor = new Actor(user.Id, user.AgencyId, user.Role, user.DisplayName);
            auditTrail.Record(actor, AuditActions.AuthenticationSucceeded, "User", user.Id);
            await db.SaveChangesAsync(cancellationToken);
            var issued = tokenIssuer.Issue(user);
            logger.LogInformation("Sati authentication succeeded for user {UserId} in agency {AgencyId}.", user.Id, user.AgencyId);
            return TypedResults.Ok(new LoginResponse(issued.Token, issued.ExpiresAtUtc, ContractMapper.ToProfile(user)));
        })
        .RequireRateLimiting("login")
        .AllowAnonymous();
    }

    private static void MapProfile(RouteGroupBuilder api)
    {
        api.MapPost("/auth/renew", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            TokenIssuer tokenIssuer,
            IOptions<ApiAuthenticationOptions> authenticationOptions,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var authenticatedAtValue = principal.FindFirst(TokenIssuer.AuthenticatedAtClaim)?.Value;
            if (!long.TryParse(authenticatedAtValue, out var authenticatedAtSeconds))
                return TypedResults.Unauthorized();

            var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(authenticatedAtSeconds);
            var now = DateTimeOffset.UtcNow;
            if (authenticatedAt > now.AddSeconds(30) ||
                now - authenticatedAt > TimeSpan.FromMinutes(authenticationOptions.Value.MaxSessionMinutes))
            {
                return TypedResults.Unauthorized();
            }

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId,
                cancellationToken);
            if (user is null)
                return TypedResults.Unauthorized();

            var issued = tokenIssuer.Issue(user, authenticatedAt);
            return TypedResults.Ok(new SessionRenewalResponse(issued.Token, issued.ExpiresAtUtc));
        });

        api.MapGet("/me", async Task<Results<Ok<UserProfileDto>, NotFound>> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId,
                cancellationToken);
            return user is null ? TypedResults.NotFound() : TypedResults.Ok(ContractMapper.ToProfile(user));
        });

        api.MapGet("/users/switchable", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var users = await db.Users.AsNoTracking()
                .Where(x => x.AgencyId == actor.AgencyId && x.Role != "PlatformOperator")
                .OrderBy(x => x.DisplayName)
                .ThenBy(x => x.Username)
                .ToListAsync(cancellationToken);
            return users.Select(ContractMapper.ToProfile).ToList();
        });
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapPost("/users", async Task<IResult> (
            CreateUserRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role is not ("Supervisor" or "Director" or "Admin")) return Results.Forbid();
            var profile = new SaveUserRequest(
                request.Username, request.DisplayName, request.Role, request.SupervisorId,
                request.AgencyId, request.Email, request.Phone);
            var errors = await ValidateUserRequestAsync(db, actor, profile, null, cancellationToken);
            if (!ValidPassword(request.InitialPassword))
                errors["password"] = ["The initial password must be between 8 and 128 characters."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var credential = passwordVerifier.Hash(request.InitialPassword);
            var user = new ServerUser
            {
                Username = request.Username.Trim(), DisplayName = request.DisplayName.Trim(),
                Role = request.Role, SupervisorId = request.SupervisorId, AgencyId = actor.AgencyId,
                Email = Normalize(request.Email), Phone = Normalize(request.Phone),
                PasswordHash = credential.Hash, Salt = credential.Salt
            };
            db.Users.Add(user);
            auditTrail.Record(actor, AuditActions.UserCreated, "User");
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", ContractMapper.ToProfile(user));
        });

        api.MapPut("/users/{userId:int}", async Task<IResult> (
            int userId, SaveUserRequest request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role is not ("Supervisor" or "Director" or "Admin")) return Results.Forbid();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.AgencyId == actor.AgencyId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (user.Role == "PlatformOperator") return Results.NotFound();
            if (actor.Role == "Supervisor" && (user.Role != "CaseManager" || user.SupervisorId != actor.UserId)) return Results.Forbid();
            var errors = await ValidateUserRequestAsync(db, actor, request, userId, cancellationToken);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            user.Username = request.Username.Trim();
            user.DisplayName = request.DisplayName.Trim();
            user.Role = request.Role;
            user.SupervisorId = request.SupervisorId;
            user.Email = Normalize(request.Email);
            user.Phone = Normalize(request.Phone);
            auditTrail.Record(actor, AuditActions.UserUpdated, "User", userId);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToProfile(user));
        });

        api.MapPut("/users/{userId:int}/password", async Task<IResult> (
            int userId, ResetPasswordRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role is not ("Supervisor" or "Director" or "Admin")) return Results.Forbid();
            if (!ValidPassword(request.NewPassword)) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["The new password must be between 8 and 128 characters."] });
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.AgencyId == actor.AgencyId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (user.Role == "PlatformOperator") return Results.NotFound();
            if (actor.Role == "Supervisor" && (user.Role != "CaseManager" || user.SupervisorId != actor.UserId)) return Results.Forbid();
            var credential = passwordVerifier.Hash(request.NewPassword);
            user.PasswordHash = credential.Hash;
            user.Salt = credential.Salt;
            auditTrail.Record(actor, AuditActions.UserPasswordReset, "User", userId);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        api.MapPut("/users/me/password", async Task<IResult> (
            ChangePasswordRequest request, ClaimsPrincipal principal, ApiDbContext db,
            PasswordVerifier passwordVerifier, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            if (!ValidPassword(request.NewPassword)) return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["password"] = ["The new password must be between 8 and 128 characters."] });
            var actor = Actor.From(principal);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actor.UserId && x.AgencyId == actor.AgencyId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (!passwordVerifier.Verify(request.CurrentPassword, user.PasswordHash, user.Salt))
                return Results.BadRequest(new ApiErrorDto(
                    "invalid_current_password", "The current password is incorrect.", string.Empty));
            var credential = passwordVerifier.Hash(request.NewPassword);
            user.PasswordHash = credential.Hash;
            user.Salt = credential.Salt;
            auditTrail.Record(actor, AuditActions.UserPasswordChanged, "User", actor.UserId);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapSupervisor(RouteGroupBuilder api)
    {
        api.MapGet("/supervisor/supervisees", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role is not ("Supervisor" or "Director" or "Admin"))
                return Results.Forbid();

            var supervisees = await db.Users.AsNoTracking()
                .Where(x => x.SupervisorId == actor.UserId &&
                            x.AgencyId == actor.AgencyId &&
                            x.Role == "CaseManager")
                .OrderBy(x => x.DisplayName)
                .ToListAsync(cancellationToken);
            return Results.Ok(supervisees.Select(ContractMapper.ToProfile).ToList());
        });

        api.MapGet("/supervisor/notes", async Task<IResult> (
            bool compliant,
            bool allSupervisees,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!TenantAccess.IsSupervisorRole(actor.Role))
                return Results.Forbid();

            var canReviewAgency = actor.Role == "Director" || actor.Role == "Admin";
            var caseManagerIds = await db.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId &&
                               user.Role == "CaseManager" &&
                               (canReviewAgency || user.SupervisorId == actor.UserId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);

            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              where note.Status == NoteWorkflow.Logged &&
                                    person.AgencyId == actor.AgencyId &&
                                    caseManagerIds.Contains(person.UserId)
                              orderby note.EventDate
                              select new ReviewableNote(note, person)).ToListAsync(cancellationToken);
            var personIds = rows.Select(row => row.Person.Id).Distinct().ToList();
            var formsByPerson = (await db.Forms.AsNoTracking()
                    .Where(form => personIds.Contains(form.PersonId))
                    .ToListAsync(cancellationToken))
                .GroupBy(form => form.PersonId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());

            // The agency's own date, not the host's. On a UTC server the small
            // hours of the morning are still the previous day in Maine, and a
            // compliance cycle that turns over a day early here would disagree
            // with the billing gate, which has always used the Maine date.
            var today = clock.Today;
            var complianceRequirements = (await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken)).BillingComplianceRequirements;
            var result = rows
                .Select(row => new
                {
                    Row = row,
                    Compliance = EvaluatePersonCompliance(
                        row.Person,
                        formsByPerson.GetValueOrDefault(row.Person.Id) ?? [],
                        today,
                        complianceRequirements)
                })
                .Where(row => row.Compliance.Passed == compliant)
                .Select(row => ContractMapper.ToNote(
                    row.Row.Note,
                    row.Row.Person,
                    row.Compliance.Reasons))
                .ToList();
            return Results.Ok(result);
        });

        api.MapPost("/supervisor/notes/{noteId:int}/approve", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return TenantAccess.IsSupervisorRole(actor.Role) ? Results.NotFound() : Results.Forbid();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanSupervisorTransition(row.Note.Status, NoteWorkflow.Approved))
                return Results.Conflict(new ApiErrorDto("invalid_note_status", "Only logged notes can be approved.", string.Empty));

            var forms = await db.Forms.AsNoTracking()
                .Where(form => form.PersonId == row.Person.Id)
                .ToListAsync(cancellationToken);
            var complianceRequirements = (await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken)).BillingComplianceRequirements;
            if (!EvaluatePersonCompliance(
                    row.Person, forms, clock.Today, complianceRequirements).Passed)
            {
                return Results.Conflict(new ApiErrorDto(
                    "compliance_required",
                    "This client does not meet the compliance requirements. Use the documented override workflow if approval is warranted.",
                    string.Empty));
            }

            row.Note.Status = 6;
            row.Note.ApprovedById = actor.UserId;
            row.Note.ApprovedAt = DateTime.UtcNow;
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteApproved, "Note", noteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });

        api.MapPost("/supervisor/notes/{noteId:int}/approve-override", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 1 or > 4_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["An override reason is required and must not exceed 4,000 characters."]
                });
            }

            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return TenantAccess.IsSupervisorRole(actor.Role) ? Results.NotFound() : Results.Forbid();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanSupervisorTransition(row.Note.Status, NoteWorkflow.Approved))
                return Results.Conflict(new ApiErrorDto("invalid_note_status", "Only logged notes can be approved.", string.Empty));

            var now = DateTime.UtcNow;
            row.Note.Status = 6;
            row.Note.ApprovedById = actor.UserId;
            row.Note.ApprovedAt = now;
            row.Note.ComplianceOverride = true;
            row.Note.OverrideReason = reason;
            row.Note.OverrideApprovedById = actor.UserId;
            row.Note.OverrideApprovedAt = now;
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteApprovalOverridden, "Note", noteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });

        api.MapPost("/supervisor/notes/{noteId:int}/return", async Task<IResult> (
            int noteId,
            SupervisorNoteActionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 1 or > 4_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A return reason is required and must not exceed 4,000 characters."]
                });
            }

            var actor = Actor.From(principal);
            var row = await LoadReviewableNoteAsync(db, actor, noteId, cancellationToken);
            if (row is null)
                return TenantAccess.IsSupervisorRole(actor.Role) ? Results.NotFound() : Results.Forbid();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanSupervisorTransition(row.Note.Status, NoteWorkflow.Returned))
                return Results.Conflict(new ApiErrorDto("invalid_note_status", "Only logged notes can be returned.", string.Empty));

            row.Note.Status = 7;
            row.Note.ReturnedById = actor.UserId;
            row.Note.ReturnReason = reason;
            row.Note.ReturnedAt = DateTime.UtcNow;
            row.Note.Revision++;
            auditTrail.Record(actor, AuditActions.NoteReturned, "Note", noteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, row.Person));
        });
    }

    private static void MapCaseload(RouteGroupBuilder api)
    {
        api.MapGet("/caseload", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var people = await db.People.AsNoTracking()
                .Where(x => x.UserId == targetUserId)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .ToListAsync(cancellationToken);
            var ids = people.Select(x => x.Id).ToList();
            var forms = await db.Forms.AsNoTracking().Where(x => ids.Contains(x.PersonId)).ToListAsync(cancellationToken);
            var notes = await db.Notes.AsNoTracking().Where(x => ids.Contains(x.PersonId)).ToListAsync(cancellationToken);
            var formsByPerson = forms.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => (IReadOnlyList<ServerForm>)x.ToList());
            var notesByPerson = notes.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => (IReadOnlyList<ServerNote>)x.ToList());

            return Results.Ok(people.Select(person => ContractMapper.ToPerson(
                person,
                formsByPerson.GetValueOrDefault(person.Id) ?? [],
                notesByPerson.GetValueOrDefault(person.Id) ?? [])).ToList());
        });

        api.MapGet("/people/{personId:int}/journal", async Task<Results<Ok<string?>, NotFound>> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var journal = await db.People.AsNoTracking()
                .Where(x => x.Id == personId && x.UserId == actor.UserId)
                .Select(x => new { x.Journal })
                .SingleOrDefaultAsync(cancellationToken);
            return journal is null ? TypedResults.NotFound() : TypedResults.Ok<string?>(journal.Journal);
        });

        api.MapPut("/people/{personId:int}/journal", async Task<IResult> (
            int personId,
            SaveJournalRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.SingleOrDefaultAsync(
                x => x.Id == personId && x.UserId == actor.UserId && x.AgencyId == actor.AgencyId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            person.Journal = request.Journal;
            if (!lifecycle.RecordChanged(actor, person, before, "JournalUpdated"))
                return Results.NoContent();

            auditTrail.Record(actor, AuditActions.PersonJournalUpdated, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }
            return Results.NoContent();
        });

        // A journal ENTRY is prepended by the server, not composed by the client.
        // The PUT above replaces the whole journal, so a client that read the
        // journal, prepended locally, and wrote it back would erase anything a
        // concurrent session typed in between. The caller sends only the text:
        // the stamp comes from the agency clock so the record cannot claim a
        // moment the caller invented, and JournalEntry owns the placement so the
        // desktop's transitional local path cannot order entries differently.
        api.MapPost("/people/{personId:int}/journal/entries", async Task<IResult> (
            int personId,
            AddJournalReminderRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var text = request.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Text"] = ["A reminder needs text."]
                });
            if (text.Length > JournalEntry.MaxTextLength)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Text"] = [$"A reminder is limited to {JournalEntry.MaxTextLength} characters."]
                });

            // Same scope gate as the journal PUT: the person must be on this
            // caller's caseload AND in this caller's agency.
            var actor = Actor.From(principal);
            var person = await db.People.SingleOrDefaultAsync(
                x => x.Id == personId && x.UserId == actor.UserId && x.AgencyId == actor.AgencyId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            person.Journal = JournalEntry.PrependReminder(person.Journal, clock.Now, text);
            lifecycle.RecordChanged(actor, person, before, "JournalReminderAdded");
            auditTrail.Record(actor, AuditActions.PersonJournalReminderAdded, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }

            // The updated journal goes back so the caller shows what was actually
            // written rather than its own guess at it.
            return Results.Ok<string?>(person.Journal);
        });
    }

    private static void MapPeople(RouteGroupBuilder api)
    {
        api.MapPost("/people", async Task<IResult> (
            SavePersonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePerson(request, requireNewForms: request.EffectiveDate.HasValue);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (request.IsTestData && actor.Role != "Admin")
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["isTestData"] = ["Only a current Admin can create a consumer marked as Test."]
                });
            }
            ContractMapper.TryParseGender(request.Gender, out var gender);
            ContractMapper.TryParseWaiver(request.Waiver, out var waiver);
            var person = new ServerPerson
            {
                UserId = actor.UserId,
                AgencyId = actor.AgencyId,
                IsTestData = request.IsTestData
            };
            ApplyPerson(person, request, gender, waiver);

            if (request.EffectiveDate is DateTime effectiveDate)
            {
                var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
                person.Forms = BuildInitialForms(request.Forms, effectiveDate, settings);
            }

            db.People.Add(person);
            lifecycle.RecordCreated(actor, person);
            auditTrail.Record(actor, AuditActions.PersonCreated, "Person");
            await db.SaveChangesAsync(cancellationToken);
            // Everything needed for the response is already tracked. Avoid a second
            // database read after the transaction commits: if that read failed, the
            // caller would be told creation failed even though the client existed.
            return Results.Ok(ContractMapper.ToPerson(person, person.Forms, []));
        });

        api.MapPut("/people/{personId:int}", async Task<IResult> (
            int personId,
            SavePersonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var newForms = request.Forms.Where(form => form.Id == 0).ToList();
            var validation = ValidatePerson(request, requireNewForms: newForms.Count > 0);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            var person = await db.People.SingleOrDefaultAsync(
                x => x.Id == personId && x.UserId == actor.UserId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            if (request.ExpectedRevision != person.Revision)
                return StalePersonConflict();
            if (request.IsTestData != person.IsTestData)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["isTestData"] = ["The Test designation is set only when a consumer is created and cannot be changed later."]
                });
            }

            var before = PersonLifecycle.Capture(person);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            ContractMapper.TryParseGender(request.Gender, out var gender);
            ContractMapper.TryParseWaiver(request.Waiver, out var waiver);
            ApplyPerson(person, request, gender, waiver);

            var additionalChanges = new List<PersonFieldChangeDto>();
            if (newForms.Count > 0)
            {
                if (request.EffectiveDate is not DateTime effectiveDate)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["effectiveDate"] = ["An effective date is required when generating forms."]
                    });

                var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
                foreach (var form in BuildInitialForms(newForms, effectiveDate, settings))
                {
                    form.PersonId = person.Id;
                    db.Forms.Add(form);
                }
                additionalChanges.Add(new PersonFieldChangeDto(
                    "forms",
                    "Generated compliance forms",
                    null,
                    $"{newForms.Count} forms"));
            }

            if (lifecycle.RecordChanged(actor, person, before, "Updated", additionalChanges))
                auditTrail.Record(actor, AuditActions.PersonUpdated, "Person", personId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StalePersonConflict();
            }
            return Results.Ok(await LoadPersonDtoAsync(db, person, cancellationToken));
        });

        api.MapGet("/people/{personId:int}/history", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var person = await LoadAuditablePersonAsync(db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            auditTrail.Record(actor, AuditActions.PersonHistoryViewed, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            var versions = await db.PersonVersions.AsNoTracking()
                .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                .OrderBy(version => version.Version)
                .ToListAsync(cancellationToken);
            return Results.Ok(versions.Select(PersonLifecycle.ToDto).ToList());
        });

        api.MapGet("/people/{personId:int}/history.pdf", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            PersonLifecycle lifecycle,
            PersonAuditPdfGenerator pdfGenerator,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var person = await LoadAuditablePersonAsync(db, actor, personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            await lifecycle.EnsureBaselineAsync(person, cancellationToken);
            auditTrail.Record(actor, AuditActions.PersonHistoryPdfGenerated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            var versions = await db.PersonVersions.AsNoTracking()
                .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
                .OrderBy(version => version.Version)
                .ToListAsync(cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId,
                cancellationToken);
            var pdf = pdfGenerator.Generate(person, versions, agency, actor, DateTime.UtcNow);
            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            return Results.File(
                pdf,
                "application/pdf",
                $"person-{person.Id}-{safeName}-lifecycle-audit.pdf");
        });

        // The mask, never the number. There is no route anywhere that returns a
        // plaintext SSN: the only thing that decrypts is the form fill below, and what
        // leaves this process is a PDF, not a string.
        api.MapGet("/people/{personId:int}/ssn", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            PreventSensitiveResponseCaching(httpContext);
            var lastFour = await db.People.AsNoTracking()
                .Where(person => person.Id == personId)
                .Select(person => person.SsnLastFour)
                .SingleOrDefaultAsync(cancellationToken);

            return Results.Ok(new SsnStatusDto(
                SsnMask.Format(lastFour),
                !string.IsNullOrEmpty(lastFour)));
        });

        api.MapPut("/people/{personId:int}/ssn", async Task<IResult> (
            int personId,
            SsnUpdateRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            EnvelopeProtector protector,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            var normalized = SsnMask.Normalize(request.Ssn);
            if (normalized is null)
            {
                ClearSsn(person);
            }
            else
            {
                // Shape-checked before it is encrypted. A transposed digit that reaches
                // an official form is a rejected application; catching it here costs
                // nothing, and once encrypted nothing can look at it again to check.
                if (!SsnMask.IsWellFormed(normalized))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Ssn"] = ["Enter a valid nine-digit Social Security number."],
                    });
                }

                await ProtectSsnAsync(person, actor.AgencyId, normalized, protector, cancellationToken);
            }

            // The action, never the value. An audit row naming what changed is the
            // point; an audit row containing the number would defeat the column.
            auditTrail.Record(actor, AuditActions.PersonSsnUpdated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new SsnStatusDto(
                SsnMask.Format(person.SsnLastFour),
                !string.IsNullOrEmpty(person.SsnLastFour)));
        });

        // The one operation permitted to decrypt an SSN, and the reason the filler
        // lives on the server at all.
        api.MapPost("/people/{personId:int}/forms.pdf", async Task<IResult> (
            int personId,
            DhhsFormRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            EnvelopeProtector protector,
            DhhsFormFiller filler,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<DhhsFormDefinition.FormKey>(request.Form, out var form))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Form"] = ["Unknown DHHS form."],
                });
            }

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId, cancellationToken);
            if (person is null)
                return Results.NotFound();

            var caseManager = await db.Users.AsNoTracking().SingleAsync(
                user => user.Id == actor.UserId, cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId, cancellationToken);

            string? ssn = null;
            if (person.SsnCiphertext is not null && person.SsnKeyId is not null)
            {
                var binding = new FieldBinding(actor.AgencyId, person.Id, "Ssn");
                ssn = await protector.UnprotectAsync(
                    new ProtectedValue(
                        person.SsnCiphertext,
                        person.SsnNonce!,
                        person.SsnTag!,
                        person.SsnWrappedKey!,
                        person.SsnKeyId),
                    binding,
                    cancellationToken);
                auditTrail.Record(actor, AuditActions.PersonSsnDecrypted, "Person", personId);
            }

            var subject = new DhhsFormDefinition.Subject(
                    FullName: $"{person.LastName}, {person.FirstName}".Trim(' ', ','),
                    BirthDate: person.BirthDate,
                    Address: person.Address,
                    PhoneNumber: person.PhoneNumber,
                    SocialSecurityNumber: ssn,
                    RepresentativeName: null,
                    RepresentativeAddress: null,
                    RepresentativePhone: null,
                    RepresentativeEmail: null)
                .WithRepresentative(
                    caseManager.DisplayName,
                    caseManager.Phone,
                    caseManager.Email,
                    agency.Street,
                    agency.City,
                    agency.State,
                    agency.Zip);

            var selections = new DhhsFormDefinition.Selections(request.Checks, request.Text);

            byte[] pdf;
            try
            {
                pdf = filler.Fill(form, subject, selections);
            }
            catch (InvalidOperationException refusal)
            {
                // A selection naming something that is not a consent field of this
                // form. Surfaced rather than ignored: silently dropping it would let a
                // case manager believe they recorded a choice the PDF never received.
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Selections"] = [refusal.Message],
                });
            }

            PreventSensitiveResponseCaching(httpContext);
            auditTrail.Record(actor, AuditActions.DhhsFormGenerated, "Person", personId,
                metadataJson: JsonSerializer.Serialize(new { Form = form.ToString() }));
            await db.SaveChangesAsync(cancellationToken);

            // Which boxes could not be filled, as a header rather than a JSON wrapper,
            // so the body stays a PDF the client can hand straight to a save dialog. A
            // blank box is never an error — the form is still correct and usable, it
            // just needs a pen — so this is advisory, not a failure.
            var unfilled = DhhsFormDefinition.UnfilledFields(form, subject);
            if (unfilled.Count > 0)
                httpContext.Response.Headers["X-Sati-Unfilled-Fields"] = string.Join("|", unfilled);

            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            return Results.File(pdf, "application/pdf", $"{form}-{personId}-{safeName}.pdf");
        });

        // Agency-owned release. Unlike the DHHS route this never decrypts an SSN,
        // but it is still a disclosure artifact: identity is derived from the
        // authorized person/session, generation is audited, and the PDF is no-store.
        api.MapPost("/people/{personId:int}/agency-release.pdf", async Task<IResult> (
            int personId,
            AgencyReleaseRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ApiDbContext db,
            AgencyReleasePdfGenerator generator,
            AuditTrail auditTrail,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var validation = AgencyReleaseRules.Validate(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == personId,
                cancellationToken);
            if (person is null)
                return Results.NotFound();
            var agency = await db.Agencies.AsNoTracking().SingleAsync(
                candidate => candidate.Id == actor.AgencyId,
                cancellationToken);

            var subject = new AgencyReleaseSubject(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                person.BirthDate,
                person.HasGuardian ? person.GuardianName : null,
                agency.Name,
                ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
                agency.EdiContactPhone,
                actor.DisplayName,
                actor.Role);
            var generatedAtUtc = clock.UtcNow.UtcDateTime;
            var pdf = generator.Generate(subject, request, generatedAtUtc);

            PreventSensitiveResponseCaching(httpContext);
            auditTrail.Record(
                actor,
                AuditActions.AgencyReleaseGenerated,
                "Person",
                personId,
                metadataJson: JsonSerializer.Serialize(new
                {
                    Scope = request.Scope,
                    StaffAttestation = request.ConfirmedObtainedRoi,
                    Revocation = request.IsRevocation,
                }));
            await db.SaveChangesAsync(cancellationToken);

            var safeName = SafeFileName($"{person.LastName}-{person.FirstName}");
            var prefix = request.IsRevocation ? "Agency-Release-Revocation" : "Agency-Release";
            return Results.File(pdf, "application/pdf", $"{prefix}-{personId}-{safeName}.pdf");
        });

        api.MapGet("/people/{personId:int}/contacts", async Task<IResult> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var contacts = await db.PersonContacts.AsNoTracking()
                .Where(x => x.PersonId == personId && x.IsActive)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .ToListAsync(cancellationToken);
            return Results.Ok(contacts.Select(ContractMapper.ToPersonContact).ToList());
        });

        api.MapPost("/people/{personId:int}/contacts", async Task<IResult> (
            int personId,
            SavePersonContactRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePersonContact(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var contact = new ServerPersonContact { PersonId = personId };
            ApplyPersonContact(contact, request);
            db.PersonContacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToPersonContact(contact));
        });

        api.MapPut("/people/{personId:int}/contacts/{contactId:int}", async Task<IResult> (
            int personId,
            int contactId,
            SavePersonContactRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validation = ValidatePersonContact(request);
            if (validation.Count > 0)
                return Results.ValidationProblem(validation);

            var actor = Actor.From(principal);
            var contact = await (from candidate in db.PersonContacts
                                 join person in db.People on candidate.PersonId equals person.Id
                                 where candidate.Id == contactId && candidate.PersonId == personId &&
                                       person.UserId == actor.UserId
                                 select candidate).SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
                return Results.NotFound();

            ApplyPersonContact(contact, request);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToPersonContact(contact));
        });

        api.MapDelete("/contacts/{contactId:int}", async Task<IResult> (
            int contactId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var contact = await (from candidate in db.PersonContacts
                                 join person in db.People on candidate.PersonId equals person.Id
                                 where candidate.Id == contactId && person.UserId == actor.UserId
                                 select candidate).SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
                return Results.NotFound();

            contact.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        MapConsumerProviders(api);
    }

    // A consumer's medical provider list. The response carries the link's own fields and
    // nothing derived: the practice and network are resolved by the caller from the
    // directory it already holds, so a payload cannot disagree with the directory it came
    // from, and correcting a directory entry corrects every profile at once.
    private static void MapConsumerProviders(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/providers", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            // Ended links are returned too. Past providers are part of the record; which
            // of them to show is the caller's decision, not the query's.
            var links = await db.PersonProviders.AsNoTracking()
                .Where(link => link.PersonId == personId)
                .OrderByDescending(link => link.IsPrimaryCare)
                .ThenBy(link => link.SortOrder)
                .ThenBy(link => link.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(links.Select(ContractMapper.ToConsumerProvider).ToList());
        });

        api.MapPost("/people/{personId:int}/providers", async Task<IResult> (
            int personId, SaveConsumerProviderRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var errors = ConsumerProviderRules.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var conflict = await FindConsumerProviderConflictAsync(
                db, actor.AgencyId, personId, request, editingLinkId: 0, cancellationToken);
            if (conflict is not null) return conflict;

            var link = new ServerPersonProvider { PersonId = personId };
            ApplyConsumerProvider(link, request);
            db.PersonProviders.Add(link);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToConsumerProvider(link));
        });

        api.MapPut("/people/{personId:int}/providers/{linkId:int}", async Task<IResult> (
            int personId, int linkId, SaveConsumerProviderRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var errors = ConsumerProviderRules.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var link = await db.PersonProviders.SingleOrDefaultAsync(
                candidate => candidate.Id == linkId && candidate.PersonId == personId, cancellationToken);
            if (link is null) return Results.NotFound();

            var conflict = await FindConsumerProviderConflictAsync(
                db, actor.AgencyId, personId, request, linkId, cancellationToken);
            if (conflict is not null) return conflict;

            ApplyConsumerProvider(link, request);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToConsumerProvider(link));
        });

        // Removal, for a link recorded against the wrong consumer. Ending a real
        // relationship is a PUT that sets EndDate, which keeps the row.
        api.MapDelete("/people/{personId:int}/providers/{linkId:int}", async Task<IResult> (
            int personId, int linkId, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var link = await db.PersonProviders.SingleOrDefaultAsync(
                candidate => candidate.Id == linkId && candidate.PersonId == personId, cancellationToken);
            if (link is null) return Results.NotFound();

            db.PersonProviders.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    // The provider lookup is scoped to the actor's agency, which is what makes a directory
    // entry from another tenant fail as absent rather than linking across the boundary.
    private static async Task<IResult?> FindConsumerProviderConflictAsync(
        ApiDbContext db,
        int agencyId,
        int personId,
        SaveConsumerProviderRequest request,
        int editingLinkId,
        CancellationToken cancellationToken)
    {
        var provider = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.Id == request.ProviderId && candidate.AgencyId == agencyId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (provider is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["providerId"] = [ConsumerProviderRules.ProviderOutsideAgencyMessage()]
            });

        var existing = await db.PersonProviders.AsNoTracking()
            .Where(candidate => candidate.PersonId == personId && candidate.Id != editingLinkId)
            .Select(candidate => new
            {
                candidate.ProviderId, candidate.IsPrimaryCare, candidate.EndDate
            })
            .ToListAsync(cancellationToken);

        if (editingLinkId == 0 && existing.Count >= ConsumerProviderRules.MaxProvidersPerConsumer)
            return Results.Conflict(new ApiErrorDto(
                "consumer_provider_limit", ConsumerProviderRules.TooManyProvidersMessage(), string.Empty));

        if (!ConsumerProviderRules.IsCurrent(request.EndDate))
            return null;

        if (existing.Any(candidate =>
                candidate.ProviderId == request.ProviderId &&
                ConsumerProviderRules.IsCurrent(candidate.EndDate)))
            return Results.Conflict(new ApiErrorDto(
                "consumer_provider_duplicate",
                ConsumerProviderRules.DuplicateCurrentLinkMessage(provider.Name), string.Empty));

        if (!request.IsPrimaryCare)
            return null;

        var currentPrimaryId = existing
            .Where(candidate => candidate.IsPrimaryCare && ConsumerProviderRules.IsCurrent(candidate.EndDate))
            .Select(candidate => (int?)candidate.ProviderId)
            .FirstOrDefault();
        if (currentPrimaryId is null)
            return null;

        var name = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.Id == currentPrimaryId)
            .Select(candidate => candidate.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Another provider";
        return Results.Conflict(new ApiErrorDto(
            "consumer_provider_primary_care",
            ConsumerProviderRules.PrimaryCareConflictMessage(name), string.Empty));
    }

    private static void ApplyConsumerProvider(ServerPersonProvider link, SaveConsumerProviderRequest request)
    {
        link.ProviderId = request.ProviderId;
        link.Role = Normalize(request.Role);
        link.IsPrimaryCare = request.IsPrimaryCare;
        link.StartDate = request.StartDate?.Date;
        link.EndDate = request.EndDate?.Date;
        link.HasActiveRelease = request.HasActiveRelease;
        link.SortOrder = request.SortOrder;
    }

    private static void MapReviews(RouteGroupBuilder api)
    {
        api.MapGet("/reviews", async Task<IResult> (
            int userId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.CanAccessUserAsync(db, actor, userId, cancellationToken)) return Results.Forbid();
            var items = await (from review in db.ReviewItems.AsNoTracking().Include(x => x.Appointment)
                               join person in db.People on review.PersonId equals person.Id
                               where person.UserId == userId
                               select review)
                .OrderBy(x => x.PersonId).ThenBy(x => x.CycleAnchor).ThenBy(x => x.Quarter)
                .ThenBy(x => x.Category).ThenBy(x => x.SlotIndex).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(ContractMapper.ToReviewItem).ToList());
        });

        api.MapGet("/people/{personId:int}/reviews", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)) return Results.NotFound();
            var items = await db.ReviewItems.AsNoTracking().Include(x => x.Appointment)
                .Where(x => x.PersonId == personId).OrderBy(x => x.CycleAnchor).ThenBy(x => x.Quarter)
                .ThenBy(x => x.Category).ThenBy(x => x.SlotIndex).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(ContractMapper.ToReviewItem).ToList());
        });

        api.MapPost("/reviews/ensure-current", async Task<IResult> (
            EnsureReviewItemsRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var ids = request.PersonIds.Where(x => x > 0).Distinct().Take(500).ToList();
            var people = await db.People.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
            var created = 0;
            foreach (var person in people)
            {
                if (!await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)) continue;
                var anchor = CurrentCycleAnchor(person.EffectiveDate, request.Today);
                if (anchor is null) continue;
                var existing = await db.ReviewItems.Where(x => x.PersonId == person.Id && x.CycleAnchor == anchor.Value)
                    .Select(x => new { x.Quarter, x.Category, x.SlotIndex }).ToListAsync(cancellationToken);
                var present = existing.Select(x => (x.Quarter, x.Category, x.SlotIndex)).ToHashSet();
                foreach (var required in RequiredReviewItems(person))
                {
                    if (present.Contains(required)) continue;
                    db.ReviewItems.Add(new ServerReviewItem { PersonId = person.Id, CycleAnchor = anchor.Value,
                        Quarter = required.Quarter, Category = required.Category, SlotIndex = required.SlotIndex });
                    present.Add(required);
                    created++;
                }
            }
            if (created > 0) await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new CountDto(created));
        });

        api.MapPut("/reviews/{reviewItemId:int}/stage", async Task<IResult> (
            int reviewItemId, SetReviewStageRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var item = await LoadAccessibleReviewAsync(db, Actor.From(principal), reviewItemId, cancellationToken);
            if (item is null) return Results.NotFound();
            switch (request.Stage)
            {
                case "Requested": item.RequestedDate = request.Date?.Date; break;
                case "Received": item.ReceivedDate = request.Date?.Date; break;
                case "Logged": item.LoggedDate = request.Date?.Date; break;
                default: return Results.ValidationProblem(new Dictionary<string, string[]> { ["stage"] = ["The review stage is invalid."] });
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToReviewItem(item));
        });

        api.MapPut("/reviews/{reviewItemId:int}/appointment", async Task<IResult> (
            int reviewItemId, SetAppointmentRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var item = await LoadAccessibleReviewAsync(db, Actor.From(principal), reviewItemId, cancellationToken);
            if (item is null) return Results.NotFound();
            if (item.Category is not ("Medical" or "Dental"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["appointment"] = ["Appointments apply only to medical and dental reviews."] });
            var providerName = string.IsNullOrWhiteSpace(request.ProviderName) ? null : request.ProviderName.Trim();
            if (providerName?.Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["providerName"] = ["Provider name must not exceed 100 characters."] });
            if (request.Date is null)
            {
                if (item.Appointment is not null) db.Appointments.Remove(item.Appointment);
                item.Appointment = null;
            }
            else if (item.Appointment is null)
                item.Appointment = new ServerAppointment { ReviewItemId = item.Id, Date = request.Date.Value.Date, ProviderName = providerName };
            else
            {
                item.Appointment.Date = request.Date.Value.Date;
                item.Appointment.ProviderName = providerName;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToReviewItem(item));
        });

        api.MapGet("/people/{personId:int}/appointments/latest", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)) return Results.NotFound();
            var medical = await LatestAppointmentAsync(db, personId, "Medical", cancellationToken);
            var dental = await LatestAppointmentAsync(db, personId, "Dental", cancellationToken);
            return Results.Ok(new LatestAppointmentsDto(medical is null ? null : ContractMapper.ToAppointment(medical),
                dental is null ? null : ContractMapper.ToAppointment(dental)));
        });
    }

    private static void MapAssessments(RouteGroupBuilder api)
    {
        api.MapPost("/people/{personId:int}/assessments/draft", async Task<IResult> (
            int personId, int authorUserId, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (authorUserId != actor.UserId ||
                !await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.NotFound();

            var editable = await db.ComprehensiveAssessments
                .Where(x => x.PersonId == personId && x.AuthorUserId == authorUserId &&
                            (x.Status == "Draft" || x.Status == "Returned"))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            if (editable is not null) return Results.Ok(ContractMapper.ToAssessment(editable));

            var approved = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.Status == "Approved")
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            var version = (await db.ComprehensiveAssessments.Where(x => x.PersonId == personId)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            var now = DateTime.UtcNow;
            var assessment = new ServerComprehensiveAssessment
            {
                PersonId = personId, AuthorUserId = authorUserId, Status = "Draft", Version = version,
                CreatedAt = now, UpdatedAt = now,
                DocumentJson = approved?.DocumentJson ?? "{\"contributors\":[],\"answers\":{},\"needs\":[]}"
            };
            db.ComprehensiveAssessments.Add(assessment);
            auditTrail.Record(actor, AuditActions.AssessmentCreated, "Person", personId);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapPut("/assessments/{assessmentId:int}/document", async Task<IResult> (
            int assessmentId, SaveAssessmentDocumentRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DocumentJson) || request.DocumentJson.Length > 4_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Assessment data is required and must not exceed 4 MB."] });
            try { using var _ = JsonDocument.Parse(request.DocumentJson); }
            catch (JsonException) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = ["Assessment data is invalid."] }); }
            var actor = Actor.From(principal);
            var assessment = await db.ComprehensiveAssessments.SingleOrDefaultAsync(x => x.Id == assessmentId, cancellationToken);
            if (assessment is null ||
                !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, cancellationToken))
                return Results.NotFound();
            if (assessment.Status is "Approved" or "Superseded") return Results.Conflict(new ApiErrorDto("assessment_locked", "Approved assessment versions cannot be changed.", string.Empty));
            if (request.ExpectedRevision != assessment.Revision)
                return StaleAssessmentConflict();
            assessment.DocumentJson = request.DocumentJson;
            assessment.UpdatedAt = DateTime.UtcNow;
            assessment.Revision++;
            auditTrail.Record(actor, AuditActions.AssessmentUpdated, "Assessment", assessmentId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAssessmentConflict();
            }
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapPost("/assessments/{assessmentId:int}/submit", async Task<IResult> (
            int assessmentId, int authorUserId, int expectedRevision,
            ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var assessment = await db.ComprehensiveAssessments.SingleOrDefaultAsync(x => x.Id == assessmentId, cancellationToken);
            if (assessment is null || authorUserId != actor.UserId ||
                !await TenantAccess.CanAuthorAssessmentAsync(db, actor, assessment, cancellationToken))
                return Results.NotFound();
            if (assessment.Status is not ("Draft" or "Returned"))
                return Results.Conflict(new ApiErrorDto("assessment_locked", "This assessment is not editable.", string.Empty));
            if (expectedRevision != assessment.Revision)
                return StaleAssessmentConflict();
            assessment.Status = "ReadyForReview";
            assessment.SubmittedAt = DateTime.UtcNow;
            assessment.UpdatedAt = assessment.SubmittedAt.Value;
            assessment.Revision++;
            auditTrail.Record(actor, AuditActions.AssessmentSubmitted, "Assessment", assessmentId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAssessmentConflict();
            }
            return Results.Ok(ContractMapper.ToAssessment(assessment));
        });

        api.MapGet("/people/{personId:int}/pcp-source", async Task<IResult> (
            int personId, int preferredAuthorUserId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == personId, cancellationToken);
            if (person is null || person.UserId != preferredAuthorUserId ||
                !await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)) return Results.NotFound();
            var assessment = await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.Status == "Approved")
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            assessment ??= await db.ComprehensiveAssessments.AsNoTracking()
                .Where(x => x.PersonId == personId && x.AuthorUserId == preferredAuthorUserId &&
                            (x.Status == "Draft" || x.Status == "Returned" || x.Status == "ReadyForReview"))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            var dto = assessment is null ? null : new PersonCenteredPlanSourceDto(
                assessment.Id, assessment.Version, assessment.Status, assessment.UpdatedAt, assessment.DocumentJson);
            return Results.Json(dto);
        });
    }

    private static void MapProviders(RouteGroupBuilder api)
    {
        api.MapGet("/providers", async (bool? passthroughOnly, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var query = db.Providers.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId);
            if (passthroughOnly == true) query = query.Where(x => x.ProvidesPassthroughService);
            return (await query.OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ContractMapper.ToProvider).ToList();
        });
        api.MapPost("/providers", async Task<IResult> (SaveProviderRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            // Anyone working a caseload may add and correct entries: the directory is only useful
            // if the person on the phone with a new specialist can record them straight away.
            // Removing and merging stay Admin-only, below.
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Role)) return Results.Forbid();
            var errors = ValidateProvider(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var affiliationErrors = await ValidateProviderAffiliationAsync(db, actor.AgencyId, request, 0, cancellationToken);
            if (affiliationErrors.Count > 0) return Results.ValidationProblem(affiliationErrors);
            var duplicate = await FindDuplicateProviderAsync(db, actor.AgencyId, request, null, cancellationToken);
            if (duplicate is not null) return duplicate;
            var provider = new ServerProvider { AgencyId = actor.AgencyId }; ApplyProvider(provider, request); db.Providers.Add(provider);
            await db.SaveChangesAsync(cancellationToken); return Results.Ok(ContractMapper.ToProvider(provider));
        });
        api.MapPut("/providers/{id:int}", async Task<IResult> (int id, SaveProviderRequest request, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Role)) return Results.Forbid();
            var errors = ValidateProvider(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var affiliationErrors = await ValidateProviderAffiliationAsync(db, actor.AgencyId, request, id, cancellationToken);
            if (affiliationErrors.Count > 0) return Results.ValidationProblem(affiliationErrors);
            var duplicate = await FindDuplicateProviderAsync(db, actor.AgencyId, request, id, cancellationToken);
            if (duplicate is not null) return duplicate;
            var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, cancellationToken); if (provider is null) return Results.NotFound();
            ApplyProvider(provider, request); await db.SaveChangesAsync(cancellationToken); return Results.Ok(ContractMapper.ToProvider(provider));
        });
        // Delete stays Admin-only: the directory is shared, so removing an entry reaches other
        // case managers' consumers and is not undoable by the person who did it.
        api.MapDelete("/providers/{id:int}", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Role)) return Results.Forbid();
            var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, cancellationToken); if (provider is null) return Results.NotFound();
            // Refused before the settings default is cleared, so a rejected delete leaves
            // nothing changed. Restrict would raise a foreign-key error anyway; this names
            // the affiliated entries instead.
            var affiliated = await db.Providers.AsNoTracking()
                .Where(child => child.AgencyId == actor.AgencyId && child.ParentProviderId == id)
                .OrderBy(child => child.Name).Select(child => child.Name).ToListAsync(cancellationToken);
            if (affiliated.Count > 0)
                return Results.Conflict(new ApiErrorDto(
                    "provider_has_affiliated_entries",
                    ProviderAffiliation.AffiliatedChildrenMessage(provider.Name, affiliated),
                    string.Empty));
            // Also refused while any consumer record references it, ended links included.
            // Without this the foreign key raises a raw constraint error instead, which
            // reaches the Admin as an unexplained failure.
            var onRecords = await db.PersonProviders.AsNoTracking()
                .CountAsync(link => link.ProviderId == id, cancellationToken);
            if (onRecords > 0)
                return Results.Conflict(new ApiErrorDto(
                    "provider_on_consumer_records",
                    ConsumerProviderRules.ProviderOnConsumerRecordsMessage(provider.Name, onRecords),
                    string.Empty));
            var settings = await db.Settings.FirstOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.DefaultPassthroughProviderId == id, cancellationToken);
            if (settings is not null) settings.DefaultPassthroughProviderId = null;
            db.Providers.Remove(provider); await db.SaveChangesAsync(cancellationToken); return Results.NoContent();
        });

        MapProviderContacts(api);
        MapProviderMerge(api);
    }

    // Named people at a provider. Ordinary editing, so any caseload role may maintain them: a
    // phone number is not an entry other case managers' consumers point at.
    private static void MapProviderContacts(RouteGroupBuilder api)
    {
        api.MapGet("/providers/{providerId:int}/contacts", async Task<IResult> (
            int providerId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contacts = await db.ProviderContacts.AsNoTracking()
                .Where(contact => contact.ProviderId == providerId)
                .OrderByDescending(contact => contact.IsPrimary)
                .ThenBy(contact => contact.SortOrder).ThenBy(contact => contact.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(contacts.Select(ContractMapper.ToProviderContact).ToList());
        });

        api.MapPost("/providers/{providerId:int}/contacts", async Task<IResult> (
            int providerId, SaveProviderContactRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Role)) return Results.Forbid();
            var errors = ProviderDirectoryRules.ValidateContact(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await DemoteOtherPrimaryContactsAsync(db, providerId, 0, request.IsPrimary, cancellationToken);
            var contact = new ServerProviderContact { ProviderId = providerId };
            ApplyProviderContact(contact, request);
            db.ProviderContacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToProviderContact(contact));
        });

        api.MapPut("/providers/{providerId:int}/contacts/{contactId:int}", async Task<IResult> (
            int providerId, int contactId, SaveProviderContactRequest request, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Role)) return Results.Forbid();
            var errors = ProviderDirectoryRules.ValidateContact(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contact = await db.ProviderContacts.SingleOrDefaultAsync(
                candidate => candidate.Id == contactId && candidate.ProviderId == providerId, cancellationToken);
            if (contact is null) return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await DemoteOtherPrimaryContactsAsync(db, providerId, contactId, request.IsPrimary, cancellationToken);
            ApplyProviderContact(contact, request);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToProviderContact(contact));
        });

        api.MapDelete("/providers/{providerId:int}/contacts/{contactId:int}", async Task<IResult> (
            int providerId, int contactId, ClaimsPrincipal principal,
            ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanCreateOrEdit(actor.Role)) return Results.Forbid();
            if (!await ProviderIsInAgencyAsync(db, actor.AgencyId, providerId, cancellationToken))
                return Results.NotFound();

            var contact = await db.ProviderContacts.SingleOrDefaultAsync(
                candidate => candidate.Id == contactId && candidate.ProviderId == providerId, cancellationToken);
            if (contact is null) return Results.NotFound();

            db.ProviderContacts.Remove(contact);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    // Folding one directory entry into another. Admin only, and deliberately does NOT repoint
    // AssessmentNeed.ProviderId: a document froze that entry, and rewriting it would change what
    // an approved assessment says.
    private static void MapProviderMerge(RouteGroupBuilder api)
    {
        api.MapPost("/providers/{survivingId:int}/merge", async Task<IResult> (
            int survivingId, MergeProvidersRequest request, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Role)) return Results.Forbid();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var surviving = await db.Providers.SingleOrDefaultAsync(
                p => p.Id == survivingId && p.AgencyId == actor.AgencyId, cancellationToken);
            var merged = await db.Providers.SingleOrDefaultAsync(
                p => p.Id == request.MergedProviderId && p.AgencyId == actor.AgencyId, cancellationToken);
            if (surviving is null || merged is null) return Results.NotFound();

            var problem = ProviderDirectoryRules.ValidateMerge(
                ToAffiliationNode(surviving), ToAffiliationNode(merged));
            if (problem is not null)
                return Results.Conflict(new ApiErrorDto("provider_merge_invalid", problem, string.Empty));

            var identifierProblem =
                IdentifierConflict(surviving.Npi, merged.Npi, "National Provider Identifier")
                ?? IdentifierConflict(surviving.MaineCareProviderId, merged.MaineCareProviderId,
                    "MaineCare provider identifier");
            if (identifierProblem is not null)
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_identifier_conflict", identifierProblem, string.Empty));

            var directory = (await db.Providers.AsNoTracking()
                    .Where(p => p.AgencyId == actor.AgencyId)
                    .Select(p => new { p.Id, p.Name, p.ParentProviderId, p.MedicalKind })
                    .ToListAsync(cancellationToken))
                .Select(p => new ProviderAffiliationNode(p.Id, p.Name, p.ParentProviderId,
                    Enum.TryParse<MedicalProviderKind>(p.MedicalKind, out var kind) ? kind : null))
                .ToList();
            if (ProviderAffiliation.ResolveAncestors(surviving.Id, directory).Any(n => n.Id == merged.Id))
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_loop", ProviderDirectoryRules.MergeWouldCreateLoopMessage, string.Empty));

            var duplicateCurrentLinks = await (
                from incoming in db.PersonProviders.AsNoTracking()
                join existing in db.PersonProviders.AsNoTracking()
                    on incoming.PersonId equals existing.PersonId
                where incoming.ProviderId == merged.Id && incoming.EndDate == null &&
                      existing.ProviderId == surviving.Id && existing.EndDate == null
                select incoming.PersonId).Distinct().CountAsync(cancellationToken);
            if (duplicateCurrentLinks > 0)
            {
                return Results.Conflict(new ApiErrorDto(
                    "provider_merge_consumer_link_conflict",
                    ProviderDirectoryRules.MergeConsumerLinkConflictMessage(duplicateCurrentLinks),
                    string.Empty));
            }

            var affiliatedMoved = await db.Providers
                .Where(child => child.AgencyId == actor.AgencyId && child.ParentProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(child => child.ParentProviderId, surviving.Id), cancellationToken);
            var consumerLinksMoved = await db.PersonProviders
                .Where(link => link.ProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(link => link.ProviderId, surviving.Id), cancellationToken);
            var contactsMoved = await db.ProviderContacts
                .Where(contact => contact.ProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(contact => contact.ProviderId, surviving.Id)
                    .SetProperty(contact => contact.IsPrimary, false), cancellationToken);
            await db.Settings
                .Where(s => s.AgencyId == actor.AgencyId && s.DefaultPassthroughProviderId == merged.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.DefaultPassthroughProviderId, surviving.Id), cancellationToken);

            // Adopted only where the survivor has none, so a merge never overwrites a fact
            // somebody deliberately recorded on the surviving entry.
            surviving.Npi ??= merged.Npi;
            surviving.MaineCareProviderId ??= merged.MaineCareProviderId;
            surviving.ParentProviderId ??= merged.ParentProviderId == surviving.Id ? null : merged.ParentProviderId;

            db.Providers.Remove(merged);
            auditTrail.Record(
                actor,
                AuditActions.ProviderMerged,
                "Provider",
                surviving.Id,
                JsonSerializer.Serialize(new
                {
                    mergedProviderId = merged.Id,
                    affiliatedMoved,
                    consumerLinksMoved,
                    contactsMoved
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new MergeProvidersResultDto(
                surviving.Id,
                ProviderDirectoryRules.MergeSummary(
                    surviving.Name, merged.Name, affiliatedMoved, consumerLinksMoved, contactsMoved)));
        });
    }

    private static ProviderAffiliationNode ToAffiliationNode(ServerProvider provider) =>
        new(provider.Id, provider.Name, provider.ParentProviderId,
            Enum.TryParse<MedicalProviderKind>(provider.MedicalKind, out var kind) ? kind : null);

    private static string? IdentifierConflict(string? surviving, string? merged, string which) =>
        Normalize(surviving) is { } left && Normalize(merged) is { } right &&
        !string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? ProviderDirectoryRules.ConflictingIdentifierMessage(which)
            : null;

    private static Task<bool> ProviderIsInAgencyAsync(
        ApiDbContext db, int agencyId, int providerId, CancellationToken cancellationToken) =>
        db.Providers.AsNoTracking().AnyAsync(p => p.Id == providerId && p.AgencyId == agencyId, cancellationToken);

    private static async Task DemoteOtherPrimaryContactsAsync(
        ApiDbContext db, int providerId, int editingContactId, bool isPrimary, CancellationToken cancellationToken)
    {
        if (!isPrimary) return;
        await db.ProviderContacts
            .Where(other => other.ProviderId == providerId && other.Id != editingContactId && other.IsPrimary)
            .ExecuteUpdateAsync(u => u.SetProperty(other => other.IsPrimary, false), cancellationToken);
    }

    private static void ApplyProviderContact(ServerProviderContact contact, SaveProviderContactRequest request)
    {
        contact.Name = request.Name.Trim();
        contact.Role = Normalize(request.Role);
        contact.Phone = Normalize(request.Phone);
        contact.Extension = Normalize(request.Extension);
        contact.Email = Normalize(request.Email);
        contact.IsPrimary = request.IsPrimary;
        contact.SortOrder = request.SortOrder;
    }

    private static void MapAtRequests(RouteGroupBuilder api)
    {
        api.MapGet("/at-requests", async Task<IResult> (int userId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal); if (!await TenantAccess.CanAccessUserAsync(db, actor, userId, cancellationToken)) return Results.Forbid();
            var rate = (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;
            var requests = await (from request in db.AtRequests.AsNoTracking()
                                  join person in db.People on request.PersonId equals person.Id
                                  where person.UserId == userId
                                  select new AtRequestRow
                                  {
                                      Id = request.Id, ClientName = request.ClientName, Status = request.Status,
                                      SalesTax = request.SalesTax, SubmittedDate = request.SubmittedDate,
                                      VendorName = request.VendorName, CaseManagerName = request.CaseManagerName,
                                      PassthroughRate = request.PassthroughRate, SignedByName = request.SignedByName,
                                      SignedAtUtc = request.SignedAtUtc, HasSnapshot = request.SnapshotPng != null
                                  })
                .OrderByDescending(x => x.SubmittedDate).ToListAsync(cancellationToken);
            return Results.Ok(await BuildAtRequestRowsAsync(db, requests, rate, cancellationToken));
        });

        // The same list narrowed to one client, for the AT requests section of a
        // client's profile. Gated on the CLIENT's owning user, so reaching another
        // agency's client here fails the same way it does everywhere else.
        api.MapGet("/people/{personId:int}/at-requests", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == personId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken))
                return Results.NotFound();

            var rate = (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;
            var requests = await db.AtRequests.AsNoTracking()
                .Where(request => request.PersonId == personId)
                .Select(request => new AtRequestRow
                {
                    Id = request.Id, ClientName = request.ClientName, Status = request.Status,
                    SalesTax = request.SalesTax, SubmittedDate = request.SubmittedDate,
                    VendorName = request.VendorName, CaseManagerName = request.CaseManagerName,
                    PassthroughRate = request.PassthroughRate, SignedByName = request.SignedByName,
                    SignedAtUtc = request.SignedAtUtc, HasSnapshot = request.SnapshotPng != null
                })
                .OrderByDescending(x => x.SubmittedDate).ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(await BuildAtRequestRowsAsync(db, requests, rate, cancellationToken));
        });
        api.MapGet("/at-requests/{id:int}", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            return request is null ? Results.NotFound() : Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapGet("/at-requests/{id:int}/snapshot", async Task<IResult> (int id, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            return request is null ? Results.NotFound() : Results.Ok(new BinaryPayloadDto(
                request.SnapshotPng is null ? null : Convert.ToBase64String(request.SnapshotPng)));
        });
        api.MapPost("/at-requests", async Task<IResult> (SaveAtRequestRequest input, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.PersonId, cancellationToken);
            if (person is null || !await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)) return Results.NotFound();
            var owner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == person.UserId, cancellationToken);
            var agency = await db.Agencies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.AgencyId, cancellationToken);
            var errors = ValidateAtRequest(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var request = new ServerAtRequest { PersonId = person.Id,
                ClientName = $"{person.FirstName} {person.LastName}".Trim(), ClientEvergreenId = person.EvergreenId,
                CaseManagerName = owner.DisplayName, CaseManagerEmail = owner.Email, CaseManagerPhone = owner.Phone,
                CaseManagerAgency = agency?.Name };
            ApplyAtRequest(request, input); db.AtRequests.Add(request); await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapPut("/at-requests/{id:int}", async Task<IResult> (int id, SaveAtRequestRequest input, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            if (request is null || request.PersonId != input.PersonId) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            // The publication lock, checked against the stored row. Publishing does
            // not come through here; it has its own route.
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();
            var errors = ValidateAtRequest(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApplyAtRequest(request, input); request.Revision++;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        // Publish. The attestation is derived HERE, from the validated actor, and
        // there is deliberately no way for a caller to supply a signer name: the
        // server records who published, not who the client says published.
        //
        // ValidatedActorFilter has already re-confirmed the claimed identity, role,
        // and agency against the database, and LoadAccessibleAtRequestAsync gates
        // on TenantAccess, so by this point the actor is a real user who may reach
        // this client's request.
        api.MapPost("/at-requests/{id:int}/publish", async Task<IResult> (
            int id, PublishAtRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var request = await LoadAccessibleAtRequestAsync(db, actor, id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();

            // Completeness is decided by the shared rule owner, not re-expressed
            // here. A second copy of these checks would be a rule enforced two ways.
            var blockers = AtRequestPublication.FindBlockers(
                request.VendorName, request.VendorBillingLocation,
                request.Items.Select(item => new AtRequestLine(item.Name, item.ItemCost, item.Quantity)),
                alreadyPublished: false);
            if (blockers.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["publish"] = [.. blockers] });

            var signedAtUtc = DateTime.UtcNow;

            // Frozen from agency settings, never from the payload. Regenerating
            // this document next year must reproduce the money it was filed at,
            // not recompute it against whatever rate the agency has by then.
            request.PassthroughRate =
                (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).PassthroughRate;

            request.SignedByName = actor.DisplayName;
            request.SignedByRole = actor.Role;
            request.SignedByUserId = actor.UserId;
            request.SignedAtUtc = signedAtUtc;
            request.AttestationStatement = AtRequestPublication.AttestationStatement;
            request.SubmittedDate = signedAtUtc.Date;
            request.Status = "Review";
            request.Revision++;

            audit.Record(actor, AuditActions.AtRequestPublished, "AtRequest", request.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });

        // Reopen for correction. Audited on its own action because discarding an
        // attestation is a materially different event from making one, and a
        // reviewer reading the trail should not have to infer it from a gap.
        api.MapPost("/at-requests/{id:int}/reopen", async Task<IResult> (
            int id, ReopenAtRequestRequest input, ClaimsPrincipal principal,
            ApiDbContext db, AuditTrail audit, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var request = await LoadAccessibleAtRequestAsync(db, actor, id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != input.ExpectedRevision) return StaleAtRequestConflict();
            if (!IsAtRequestPublished(request))
                return Results.Ok(ContractMapper.ToAtRequest(request));

            audit.Record(actor, AuditActions.AtRequestReopened, "AtRequest", request.Id,
                JsonSerializer.Serialize(new
                {
                    discardedSigner = request.SignedByName,
                    discardedSignedAtUtc = request.SignedAtUtc
                }));

            request.SignedByName = null;
            request.SignedByRole = null;
            request.SignedByUserId = null;
            request.SignedAtUtc = null;
            request.AttestationStatement = null;
            request.SubmittedDate = null;
            // A draft again, so the live agency rate governs.
            request.PassthroughRate = null;
            request.Status = "Development";
            request.Revision++;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.Ok(ContractMapper.ToAtRequest(request));
        });
        api.MapDelete("/at-requests/{id:int}", async Task<IResult> (int id, int? expectedRevision, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var request = await LoadAccessibleAtRequestAsync(db, Actor.From(principal), id, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Revision != expectedRevision) return StaleAtRequestConflict();
            // A published request is a document someone attested to. Deleting it
            // outright is not a correction; reopening it is.
            if (IsAtRequestPublished(request)) return PublishedAtRequestConflict();
            db.AtRequests.Remove(request);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleAtRequestConflict();
            }
            return Results.NoContent();
        });
    }

    private static void MapAiContext(RouteGroupBuilder api)
    {
        api.MapGet("/people/{personId:int}/ai-context", async Task<IResult> (
            int personId, ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return Results.Forbid();

            var selected = await db.People
                .AsNoTracking()
                .Where(person => person.Id == personId && person.AgencyId == actor.AgencyId)
                .Select(person => new { person.Id, person.FirstName })
                .SingleOrDefaultAsync(cancellationToken);
            if (selected is null)
                return Results.Forbid();

            return Results.Ok(new ClientAiContextDto(
                selected.Id,
                selected.FirstName,
                [new ClientAiContextSourceDto("Scope", "Selected client identity only; no prior records")]));
        });
    }

    private static void MapNotes(RouteGroupBuilder api)
    {
        api.MapPost("/notes", async (
            SaveNoteRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            request = NoteSchedulingPolicy.Normalize(request, clock.Today);
            var validation = ValidateNote(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, request.PersonId, cancellationToken))
                return Results.NotFound();

            var timeConflict = await FindServiceTimeProblemAsync(db, actor, request, null, cancellationToken);
            if (timeConflict is not null)
                return timeConflict;

            ContractMapper.TryParseNoteStatus(request.Status, out var status);
            ContractMapper.TryParseFormType(request.FormType, out var formType);
            ContractMapper.TryParseNoteType(request.NoteType, out var noteType);
            var note = new ServerNote
            {
                Narrative = request.Narrative,
                EventDate = request.EventDate,
                Status = status,
                Minutes = request.Minutes,
                StartTime = request.StartTime,
                PersonId = request.PersonId,
                FormType = formType,
                NoteType = noteType,
                AgencyId = actor.AgencyId,
                CaseManagerJustification = request.CaseManagerJustification,
                VisitDocumentationJson = request.VisitDocumentationJson
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToNote(note));
        });

        api.MapPut("/notes/{id:int}", async (
            int id,
            SaveNoteRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            request = NoteSchedulingPolicy.Normalize(request, clock.Today);
            var validation = ValidateNote(request);
            if (validation is not null)
                return Results.ValidationProblem(validation);
            var row = await (from note in db.Notes
                             join person in db.People on note.PersonId equals person.Id
                             where person.UserId == actor.UserId &&
                                   person.AgencyId == actor.AgencyId &&
                                   note.AgencyId == actor.AgencyId &&
                                   note.Id == id
                             select new { Note = note, Person = person })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null)
                return Results.NotFound();
            if (request.ExpectedRevision != row.Note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanCaseManagerEdit(row.Note.Status))
                return Results.Conflict(new ApiErrorDto(
                    "note_locked",
                    "Logged and approved notes cannot be edited. A supervisor must return a logged note before it can be corrected.",
                    string.Empty));
            ContractMapper.TryParseNoteStatus(request.Status, out var requestedStatus);
            if (!NoteWorkflow.CanCaseManagerTransition(row.Note.Status, requestedStatus))
                return Results.Conflict(new ApiErrorDto(
                    "invalid_note_transition",
                    NoteWorkflow.DescribeRejectedTransition(row.Note.Status, requestedStatus),
                    string.Empty));

            var previousPersonId = row.Note.PersonId;
            var responsePerson = row.Person;
            if (request.PersonId != previousPersonId)
            {
                responsePerson = await db.People.SingleOrDefaultAsync(person =>
                    person.Id == request.PersonId &&
                    person.UserId == actor.UserId &&
                    person.AgencyId == actor.AgencyId,
                    cancellationToken);
                if (responsePerson is null)
                    return Results.NotFound();
            }

            var timeConflict = await FindServiceTimeProblemAsync(db, actor, request, id, cancellationToken);
            if (timeConflict is not null)
                return timeConflict;

            ContractMapper.TryParseNoteStatus(request.Status, out var status);
            ContractMapper.TryParseFormType(request.FormType, out var formType);
            ContractMapper.TryParseNoteType(request.NoteType, out var noteType);
            row.Note.Narrative = request.Narrative;
            row.Note.EventDate = request.EventDate;
            row.Note.Status = status;
            row.Note.Minutes = request.Minutes;
            row.Note.StartTime = request.StartTime;
            row.Note.PersonId = request.PersonId;
            row.Note.FormType = formType;
            row.Note.NoteType = noteType;
            row.Note.CaseManagerJustification = request.CaseManagerJustification;
            row.Note.VisitDocumentationJson = request.VisitDocumentationJson;
            row.Note.Revision++;
            if (previousPersonId != row.Note.PersonId)
            {
                auditTrail.Record(
                    actor,
                    AuditActions.NoteReassigned,
                    "Note",
                    row.Note.Id,
                    JsonSerializer.Serialize(new
                    {
                        previousPersonId,
                        newPersonId = row.Note.PersonId
                    }));
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.Ok(ContractMapper.ToNote(row.Note, responsePerson));
        });

        api.MapDelete("/notes/{id:int}", async (
            int id,
            int? expectedRevision,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var note = await (from candidate in db.Notes
                              join person in db.People on candidate.PersonId equals person.Id
                              where candidate.Id == id && person.UserId == actor.UserId
                              select candidate).SingleOrDefaultAsync(cancellationToken);
            if (note is null)
                return Results.NotFound();
            if (expectedRevision != note.Revision)
                return StaleNoteConflict();
            if (!NoteWorkflow.CanCaseManagerDelete(note.Status))
                return Results.Conflict(new ApiErrorDto(
                    "note_retained",
                    "Submitted and workflow-controlled notes are retained as part of the clinical record.",
                    string.Empty));

            db.Notes.Remove(note);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleNoteConflict();
            }
            return Results.NoContent();
        });

        api.MapGet("/people/{personId:int}/notes", async Task<Results<Ok<List<NoteDto>>, NotFound>> (
            int personId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (!await TenantAccess.OwnsPersonAsync(db, actor, personId, cancellationToken))
                return TypedResults.NotFound();
            var notes = await db.Notes.AsNoTracking().Where(x => x.PersonId == personId).ToListAsync(cancellationToken);
            return TypedResults.Ok(notes.Select(x => ContractMapper.ToNote(x)).ToList());
        });

        api.MapGet("/notes/monthly", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var first = new DateTime(clock.Today.Year, clock.Today.Month, 1);
            var end = first.AddMonths(1);
            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              where person.UserId == targetUserId &&
                                    note.EventDate >= first && note.EventDate < end
                              select new { Note = note, Person = person })
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        // The case manager's whole day, across their whole caseload. Overlapping
        // service time is a property of one person's day, so this is scoped by
        // user and date and never by client.
        api.MapGet("/notes/day", async Task<IResult> (
            int? userId,
            DateTime date,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var rows = await LoadDayNotesAsync(db, targetUserId, date, cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        api.MapGet("/notes/year/{year:int}", async (
            int year,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (year is < 2000 or > 2200)
                return Results.BadRequest();
            var actor = Actor.From(principal);
            var first = new DateTime(year, 1, 1);
            var end = first.AddYears(1);
            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              where person.UserId == actor.UserId &&
                                    note.EventDate >= first && note.EventDate < end
                              select new { Note = note, Person = person })
                .ToListAsync(cancellationToken);
            return Results.Ok(rows.Select(x => ContractMapper.ToNote(x.Note, x.Person)).ToList());
        });

        api.MapPost("/notes/abandon-overdue", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var abandonedAfterDays = (await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken)).AbandonedAfterDays;
            var threshold = clock.Today.AddDays(-abandonedAfterDays);
            var personIds = db.People.Where(x => x.UserId == actor.UserId).Select(x => x.Id);
            var count = await db.Notes
                .Where(x => personIds.Contains(x.PersonId) && x.Status == 1 && x.EventDate < threshold)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(n => n.Status, 8)
                    .SetProperty(n => n.Revision, n => n.Revision + 1), cancellationToken);
            return TypedResults.Ok(new CountDto(count));
        });
    }

    private static void MapSettings(RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            return ContractMapper.ToSettings(await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken));
        });

        api.MapPut("/settings", async Task<IResult> (
            SettingsDto request, ClaimsPrincipal principal, ApiDbContext db,
            AuditTrail auditTrail, CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin") return Results.Forbid();
            if (request.AbandonedAfterDays < 0 || request.ProductivityThreshold < 0 ||
                request.BaseIncentive < 0 || request.PerUnitIncentive < 0 ||
                request.PassthroughRate is < 0 or > 1 || request.SalesTaxRate is < 0 or > 1)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Settings contain an invalid negative value or percentage."] });
            if (!BillingComplianceGate.IsSupported(request.BillingComplianceRequirements))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["billingComplianceRequirements"] = ["The compliance requirement selection is invalid."] });
            if (request.DefaultPassthroughProviderId is int providerId &&
                !await db.Providers.AsNoTracking().AnyAsync(
                    x => x.Id == providerId && x.AgencyId == actor.AgencyId && x.ProvidesPassthroughService,
                    cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["defaultPassthroughProviderId"] = ["The default provider is outside your agency or does not provide passthrough service."] });
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            if (request.Revision != settings.Revision)
                return StaleSettingsConflict();

            var id = settings.Id;
            var agencyId = settings.AgencyId;
            db.Entry(settings).CurrentValues.SetValues(request);
            settings.Id = id;
            settings.AgencyId = agencyId;
            settings.Revision++;
            auditTrail.Record(actor, AuditActions.SettingsUpdated, "Settings", settings.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleSettingsConflict();
            }
            return Results.Ok(ContractMapper.ToSettings(settings));
        });
    }

    private static void MapScratchpads(RouteGroupBuilder api)
    {
        api.MapGet("/scratchpad/today", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var today = clock.Today;
            var scratchpad = await GetOrCreateScratchpadAsync(
                db, actor.UserId, today, cancellationToken);
            return ContractMapper.ToScratchpad(scratchpad, []);
        });

        api.MapGet("/scratchpad/tomorrow", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var agendaDate = WorkAgendaDates.NextWorkday(clock.Today);
            var scratchpad = await GetOrCreateScratchpadAsync(
                db, actor.UserId, agendaDate, cancellationToken);
            return ContractMapper.ToScratchpad(scratchpad, []);
        });

        api.MapGet("/scratchpad/history", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var pads = await db.Scratchpads.AsNoTracking()
                .Where(x => x.UserId == actor.UserId && x.Date < clock.Today)
                .OrderByDescending(x => x.Date)
                .ToListAsync(cancellationToken);
            var ids = pads.Select(x => x.Id).ToList();
            var comments = await db.ScratchpadComments.AsNoTracking()
                .Where(x => ids.Contains(x.ScratchpadId))
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var byPad = comments.GroupBy(x => x.ScratchpadId).ToDictionary(x => x.Key, x => (IReadOnlyList<ServerScratchpadComment>)x.ToList());
            return pads.Select(x => ContractMapper.ToScratchpad(x, byPad.GetValueOrDefault(x.Id) ?? [])).ToList();
        });

        api.MapPut("/scratchpad", async Task<IResult> (
            SaveScratchpadRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var content = request.Content ?? string.Empty;
            if (content.Length > 1_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["content"] = ["Scratchpad content must not exceed 1,000,000 characters."]
                });

            var scratchpad = await db.Scratchpads.SingleOrDefaultAsync(
                x => x.Id == request.Id && x.UserId == actor.UserId,
                cancellationToken);
            if (scratchpad is null)
                return Results.NotFound();
            if (request.ExpectedRevision != scratchpad.Revision)
                return StaleScratchpadConflict();

            if (scratchpad.Content == content)
                return Results.Ok(ContractMapper.ToScratchpad(scratchpad, []));

            scratchpad.Content = content;
            scratchpad.Revision++;
            auditTrail.Record(actor, AuditActions.ScratchpadUpdated, "Scratchpad", scratchpad.Id);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaleScratchpadConflict();
            }

            return Results.Ok(ContractMapper.ToScratchpad(scratchpad, []));
        });

        api.MapPost("/scratchpad/{scratchpadId:int}/comments", async Task<Results<Ok<ScratchpadCommentDto>, NotFound, ValidationProblem>> (
            int scratchpadId,
            AddScratchpadCommentRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            ApiClock clock,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length is < 1 or > 10_000)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Comment content is required and must not exceed 10,000 characters."] });
            var exists = await db.Scratchpads.AnyAsync(x => x.Id == scratchpadId && x.UserId == actor.UserId && x.Date < clock.Today, cancellationToken);
            if (!exists)
                return TypedResults.NotFound();
            var comment = new ServerScratchpadComment
            {
                ScratchpadId = scratchpadId,
                AuthorUserId = actor.UserId,
                AuthorDisplayName = actor.DisplayName,
                CreatedAtUtc = DateTime.UtcNow,
                Content = content
            };
            db.ScratchpadComments.Add(comment);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ContractMapper.ToScratchpadComment(comment));
        });
    }

    private static async Task<ServerScratchpad> GetOrCreateScratchpadAsync(
        ApiDbContext db,
        int userId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var scratchpad = await db.Scratchpads.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Date == date,
            cancellationToken);
        if (scratchpad is not null)
            return scratchpad;

        scratchpad = new ServerScratchpad { UserId = userId, Date = date };
        db.Scratchpads.Add(scratchpad);
        await db.SaveChangesAsync(cancellationToken);
        return scratchpad;
    }

    private static void MapExemptDates(RouteGroupBuilder api)
    {
        api.MapGet("/exempt-dates/{year:int}", async (
            int year,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var first = new DateTime(year, 1, 1);
            var end = first.AddYears(1);
            return await db.ExemptDates.AsNoTracking()
                .Where(x => x.UserId == actor.UserId && x.Date >= first && x.Date < end)
                .OrderBy(x => x.Date)
                .Select(x => new ExemptDateDto(x.Id, x.Date, x.Reason))
                .ToListAsync(cancellationToken);
        });

        api.MapPost("/exempt-dates", async (
            AddExemptDateRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var entity = new ServerExemptDate { UserId = actor.UserId, Date = request.Date.Date, Reason = request.Reason?.Trim() };
            db.ExemptDates.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new ExemptDateDto(entity.Id, entity.Date, entity.Reason));
        });

        api.MapDelete("/exempt-dates/{id:int}", async Task<Results<NoContent, NotFound>> (
            int id,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var count = await db.ExemptDates.Where(x => x.Id == id && x.UserId == actor.UserId).ExecuteDeleteAsync(cancellationToken);
            return count == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        });
    }

    private static void MapIncentives(RouteGroupBuilder api)
    {
        api.MapGet("/incentives/{year:int}/{month:int}", async Task<IResult> (
            int year,
            int month,
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var targetUserId = userId ?? actor.UserId;
            if (!await TenantAccess.CanAccessUserAsync(db, actor, targetUserId, cancellationToken))
                return Results.Forbid();

            var incentive = await db.Incentives.SingleOrDefaultAsync(x => x.UserId == targetUserId && x.Month == month && x.Year == year, cancellationToken);
            var created = false;
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var correctDays = WorkdayCalculator.Count(new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)), settings);
            if (incentive is null)
            {
                incentive = new ServerIncentive
                {
                    UserId = targetUserId,
                    Month = month,
                    Year = year,
                    DaysScheduled = correctDays,
                    BaseIncentive = settings.BaseIncentive,
                    PerUnitIncentive = settings.PerUnitIncentive,
                    UnitsPerDay = settings.ProductivityThreshold
                };
                db.Incentives.Add(incentive);
                created = true;
            }
            else
            {
                incentive.DaysScheduled = correctDays;
                if (incentive.UnitsPerDay == 0)
                    incentive.UnitsPerDay = settings.ProductivityThreshold;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new IncentiveEnvelopeDto(ContractMapper.ToIncentive(incentive), created));
        });

        api.MapGet("/incentives/history", async (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var rows = await db.Incentives.AsNoTracking().Where(x => x.UserId == actor.UserId).OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(cancellationToken);
            return rows.Select(ContractMapper.ToIncentive).ToList();
        });

        api.MapPut("/incentives/{id:int}", async (
            int id,
            IncentiveDto request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var incentive = await db.Incentives.SingleOrDefaultAsync(
                x => x.Id == id && x.UserId == actor.UserId,
                cancellationToken);
            if (incentive is null || request.Id != id)
                return Results.NotFound();
            if (request.DaysScheduled is < 0 or > 31 || request.UnitsPerDay is < 0 or > 1000 || request.ExcludedDatesJson.Length > 100_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["incentive"] = ["The incentive values are invalid."] });

            incentive.DaysScheduled = request.DaysScheduled;
            incentive.BaseIncentive = request.BaseIncentive;
            incentive.PerUnitIncentive = request.PerUnitIncentive;
            incentive.UnitsPerDay = request.UnitsPerDay;
            incentive.ExcludedDatesJson = request.ExcludedDatesJson;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToIncentive(incentive));
        });

        api.MapPost("/incentives/eligible-days", async (
            DateWindowRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            return new CountDto(WorkdayCalculator.Count(request.StartInclusive, request.EndInclusive, settings));
        });

        api.MapPost("/incentives/remaining-days", async (
            RemainingEligibleDaysRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var settings = await GetOrCreateSettingsAsync(db, actor.AgencyId, cancellationToken);
            var worked = request.DaysAlreadyWorked.Select(x => x.Date).ToHashSet();
            var exempt = request.ExemptDates.Select(x => x.Date).ToHashSet();
            var count = 0;
            for (var day = 1; day <= DateTime.DaysInMonth(request.Year, request.Month); day++)
            {
                var date = new DateTime(request.Year, request.Month, day);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || WorkdayCalculator.IsExcluded(date, settings) || worked.Contains(date) || exempt.Contains(date))
                    continue;
                count++;
            }
            return new CountDto(count);
        });
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports/consumer-billing-loss", async Task<IResult> (
            DateTime start,
            DateTime end,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            start = start.Date;
            end = end.Date;
            if (end < start || start.Year < 2000 || end.Year > 2200 || (end - start).TotalDays > 3_660)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = ["The report window must be valid, within 2000-2200, and no longer than 10 years."]
                });
            }

            var actor = Actor.From(principal);
            var complianceRequirements = (await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken)).BillingComplianceRequirements;
            var people = await db.People.AsNoTracking()
                .Where(x => x.UserId == actor.UserId)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .Select(x => new BillingLossPersonRow(x.Id, x.FirstName, x.LastName, x.EffectiveDate))
                .ToListAsync(cancellationToken);
            var personIds = people.Select(x => x.Id).ToList();
            if (personIds.Count == 0)
                return Results.Ok(new ConsumerBillingLossReportDto([], 0, 0, null));

            var forms = await db.Forms.AsNoTracking()
                .Where(x => personIds.Contains(x.PersonId) &&
                            x.DueDate < end &&
                            (x.CompletedDate == null || x.CompletedDate > start))
                .Select(x => new BillingLossFormRow(x.PersonId, x.Type, x.DueDate, x.CompletedDate))
                .ToListAsync(cancellationToken);
            var endExclusive = end.AddDays(1);
            var notes = await db.Notes.AsNoTracking()
                .Where(x => personIds.Contains(x.PersonId) &&
                            x.EventDate.HasValue &&
                            x.EventDate.Value >= start &&
                            x.EventDate.Value < endExclusive &&
                            (x.Status == 1 || x.Status == 2 || x.Status == 3 ||
                             x.Status == 6 || x.Status == 7 || x.Status == 9))
                .Select(x => new BillingLossNoteRow(x.PersonId, x.EventDate!.Value, x.Minutes))
                .ToListAsync(cancellationToken);

            var formsByPerson = forms.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.ToList());
            var notesByPerson = notes.GroupBy(x => x.PersonId).ToDictionary(x => x.Key, x => x.ToList());
            var consumers = new List<ConsumerBillingLossRowDto>(people.Count);
            foreach (var person in people)
            {
                var activeStart = person.EffectiveDate is DateTime effectiveDate && effectiveDate.Date > start
                    ? effectiveDate.Date
                    : start;
                var totalDays = activeStart <= end ? (end - activeStart).Days + 1 : 0;
                var blockedDates = new HashSet<DateTime>();
                if (totalDays > 0 && formsByPerson.TryGetValue(person.Id, out var personForms))
                {
                    for (var date = activeStart; date <= end; date = date.AddDays(1))
                    {
                        if (personForms.Any(form => BillingComplianceGate.IsBillingWindowBlocked(
                                form.Type, form.DueDate, form.CompletedDate, date,
                                complianceRequirements)))
                            blockedDates.Add(date);
                    }
                }

                var billableUnits = 0;
                var nonBillableUnits = 0;
                if (notesByPerson.TryGetValue(person.Id, out var personNotes))
                {
                    foreach (var note in personNotes.Where(x => x.EventDate.Date >= activeStart))
                    {
                        var units = CalculateUnits(note.Minutes);
                        if (blockedDates.Contains(note.EventDate.Date))
                            nonBillableUnits += units;
                        else
                            billableUnits += units;
                    }
                }

                var totalUnits = billableUnits + nonBillableUnits;
                var name = $"{person.FirstName ?? string.Empty} {person.LastName ?? string.Empty}".Trim();
                consumers.Add(new ConsumerBillingLossRowDto(
                    person.Id,
                    string.IsNullOrWhiteSpace(name) ? $"Consumer #{person.Id}" : name,
                    totalDays - blockedDates.Count,
                    blockedDates.Count,
                    billableUnits,
                    nonBillableUnits,
                    totalUnits > 0 ? Math.Round(100m * nonBillableUnits / totalUnits, 1) : null));
            }

            var totalBillableUnits = consumers.Sum(x => x.BillableUnits);
            var totalNonBillableUnits = consumers.Sum(x => x.NonBillableUnits);
            var totalWorkUnits = totalBillableUnits + totalNonBillableUnits;
            return Results.Ok(new ConsumerBillingLossReportDto(
                consumers,
                totalBillableUnits,
                totalNonBillableUnits,
                totalWorkUnits > 0
                    ? Math.Round(100m * totalNonBillableUnits / totalWorkUnits, 1)
                    : null));
        });
    }

    private static void MapBilling(RouteGroupBuilder api)
    {
        api.MapGet("/billing/configuration", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var agency = await db.Agencies.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            return Results.Ok(ContractMapper.ToBillingConfiguration(agency));
        });

        api.MapPut("/billing/configuration", async Task<IResult> (
            SaveBillingConfigurationRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var procedureCode = Normalize(request.ProcedureCode)?.ToUpperInvariant() ?? string.Empty;
            var modifier = Normalize(request.Modifier)?.ToUpperInvariant();
            var submitterId = Normalize(request.EdiSubmitterId) ?? string.Empty;
            var payerName = Normalize(request.PayerName) ?? string.Empty;
            var payerId = Normalize(request.PayerId) ?? string.Empty;
            var contactName = Normalize(request.ContactName) ?? string.Empty;
            var contactPhone = new string((request.ContactPhone ?? string.Empty).Where(char.IsDigit).ToArray());
            var errors = new Dictionary<string, string[]>();
            if (!BillingRules.IsValidProcedureCode(procedureCode))
                errors["procedureCode"] = ["Procedure code must contain four or five letters or digits."];
            if (!BillingRules.IsValidModifier(modifier))
                errors["modifier"] = ["Modifier must be blank or contain exactly two letters or digits."];
            if (request.UnitRate is null or <= 0 or > 100_000)
                errors["unitRate"] = ["Unit rate must be greater than zero and no more than 100,000."];
            if (!BillingRules.IsSafeX12Element(submitterId, 15))
                errors["ediSubmitterId"] = ["Submitter ID is required, must be X12-safe, and cannot exceed 15 characters."];
            if (!BillingRules.IsSafeX12Element(payerName, 60) || !BillingRules.IsSafeX12Element(payerId, 80))
                errors["payer"] = ["Payer name and ID are required and must be safe X12 values."];
            if (!BillingRules.IsSafeX12Element(contactName, 60) || contactPhone.Length is < 10 or > 15)
                errors["contact"] = ["Contact name and a 10-to-15 digit telephone number are required."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var agency = await db.Agencies.SingleAsync(
                candidate => candidate.Id == actor.AgencyId, cancellationToken);
            agency.BillingProcedureCode = procedureCode;
            agency.BillingModifier = modifier;
            agency.BillingUnitRate = request.UnitRate;
            agency.EdiSubmitterId = submitterId;
            agency.EdiPayerName = payerName;
            agency.EdiPayerId = payerId;
            agency.EdiContactName = contactName;
            agency.EdiContactPhone = contactPhone;
            auditTrail.Record(actor, AuditActions.BillingConfigurationUpdated, "Agency", agency.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ContractMapper.ToBillingConfiguration(agency));
        });

        api.MapPost("/billing/periods/{year:int}/{month:int}", async Task<IResult> (
            int year,
            int month,
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var targetUserId = userId ?? actor.UserId;
            if (month is < 1 or > 12 || year is < 2000 or > 2200 ||
                !await db.Users.AnyAsync(user => user.Id == targetUserId && user.AgencyId == actor.AgencyId, cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["The billing period is invalid."] });

            var period = await db.BillingPeriods.Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.UserId == targetUserId &&
                                                   candidate.Month == month && candidate.Year == year,
                    cancellationToken);
            if (period is null)
            {
                period = new ServerBillingPeriod
                {
                    UserId = targetUserId,
                    Month = month,
                    Year = year,
                    Status = 0
                };
                db.BillingPeriods.Add(period);
                await db.SaveChangesAsync(cancellationToken);
            }
            return Results.Ok(ContractMapper.ToBillingPeriod(period));
        });

        api.MapGet("/billing/periods", async Task<IResult> (
            int? userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var query = from period in db.BillingPeriods.AsNoTracking().Include(candidate => candidate.Lines)
                        join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                        where owner.AgencyId == actor.AgencyId && (!userId.HasValue || period.UserId == userId.Value)
                        orderby period.Year descending, period.Month descending
                        select period;
            return Results.Ok((await query.ToListAsync(cancellationToken))
                .Select(ContractMapper.ToBillingPeriod)
                .ToList());
        });

        api.MapGet("/billing/submissions", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var rows = await (from item in db.BillingSubmissionEvents.AsNoTracking()
                              join period in db.BillingPeriods.AsNoTracking() on item.BillingPeriodId equals period.Id
                              join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                              where item.AgencyId == actor.AgencyId && owner.AgencyId == actor.AgencyId
                              orderby item.OccurredAtUtc descending
                              select new BillingSubmissionHistoryDto(
                                  item.Id, period.Id, period.Year, period.Month, owner.DisplayName,
                                  period.Lines.Count, item.OccurredAtUtc, item.Stage.ToString(),
                                  item.Reference, item.ResponseType, item.ResponseCode,
                                  item.Explanation, item.IsSynthetic)).ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        api.MapGet("/billing/remittances", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var rows = await db.RemittanceClaimOutcomes.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .Select(item => new RemittanceClaimOutcomeDto(
                    item.Id, item.BillingPeriodId, item.ClaimReference, item.PayerName,
                    item.ReceivedAtUtc, item.PaymentDate, item.Status.ToString(),
                    item.BilledAmount, item.AllowedAmount, item.PaidAmount,
                    item.AdjustmentAmount, item.PatientResponsibilityAmount,
                    item.ReasonCode, item.Explanation, item.PaymentReference,
                    item.IsSynthetic))
                .ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        api.MapGet("/billing/remittance-deposits", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var deposits = await db.RemittanceDeposits.AsNoTracking()
                .Where(item => item.AgencyId == actor.AgencyId)
                .OrderByDescending(item => item.ReceivedAtUtc)
                .ToListAsync(cancellationToken);
            return Results.Ok(deposits.Select(item =>
            {
                var status = DepositReconciliationRules.GetStatus(
                    item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount,
                    item.RemittancePaymentAmount, item.EftDepositAmount);
                return new RemittanceDepositDto(
                    item.Id, item.PaymentReference, item.PayerName, item.ReceivedAtUtc,
                    item.PaymentDate, item.ClaimPaymentAmount, item.ProviderLevelAdjustmentAmount,
                    item.ProviderLevelAdjustmentSummary, item.RemittancePaymentAmount,
                    item.EftDepositAmount, status.ToString(),
                    item.EftDepositAmount - item.RemittancePaymentAmount,
                    DepositReconciliationRules.Explain(status), item.IsSynthetic);
            }).ToList());
        });

        api.MapGet("/billing/candidates", async Task<IResult> (
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var rows = await (from note in db.Notes.AsNoTracking()
                              join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                              join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                              where note.Status == 6 && owner.AgencyId == actor.AgencyId &&
                                    !db.ClaimLines.Any(line => line.NoteId == note.Id)
                              orderby note.EventDate
                              select new ReviewableNote(note, person)).ToListAsync(cancellationToken);
            var agency = await db.Agencies.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            var personIds = rows.Select(row => row.Person.Id).Distinct().ToList();
            var formsByPerson = (await db.Forms.AsNoTracking()
                    .Where(form => personIds.Contains(form.PersonId))
                    .ToListAsync(cancellationToken))
                .GroupBy(form => form.PersonId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ServerForm>)group.ToList());
            var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
            var complianceRequirements = (await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken)).BillingComplianceRequirements;
            var candidates = rows.Select(row => new BillingCandidateDto(
                ContractMapper.ToNote(row.Note, row.Person),
                ValidateBillingCandidate(row.Note, row.Person, agency,
                    formsByPerson.GetValueOrDefault(row.Person.Id) ?? [], today,
                    complianceRequirements))).ToList();
            return Results.Ok(candidates);
        });

        api.MapPost("/billing/claim-lines", async Task<IResult> (
            CreateClaimLineRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();

            var row = await (from note in db.Notes
                             join person in db.People on note.PersonId equals person.Id
                             join owner in db.Users on person.UserId equals owner.Id
                             where note.Id == request.NoteId && note.Status == 6 &&
                                   owner.AgencyId == actor.AgencyId
                             select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
            if (row is null)
                return Results.NotFound();
            if (await db.ClaimLines.AnyAsync(line => line.NoteId == request.NoteId, cancellationToken))
                return DuplicateClaimLineConflict();
            var agency = await db.Agencies.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);
            var forms = await db.Forms.AsNoTracking()
                .Where(form => form.PersonId == row.Person.Id)
                .ToListAsync(cancellationToken);
            var complianceRequirements = (await GetOrCreateSettingsAsync(
                db, actor.AgencyId, cancellationToken)).BillingComplianceRequirements;
            var errors = ValidateBillingCandidate(row.Note, row.Person, agency, forms,
                BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow),
                complianceRequirements);
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = errors.ToArray() });

            var serviceDate = row.Note.EventDate!.Value.Date;
            var period = await db.BillingPeriods.SingleOrDefaultAsync(candidate =>
                candidate.UserId == row.Person.UserId && candidate.Month == serviceDate.Month &&
                candidate.Year == serviceDate.Year, cancellationToken);
            if (period is null)
            {
                period = new ServerBillingPeriod
                {
                    UserId = row.Person.UserId,
                    Month = serviceDate.Month,
                    Year = serviceDate.Year,
                    Status = 0
                };
                db.BillingPeriods.Add(period);
            }
            if (period.Status != 0)
                return Results.Conflict(new ApiErrorDto("period_submitted", "This billing period is no longer a draft.", string.Empty));

            var line = new ServerClaimLine
            {
                NoteId = row.Note.Id,
                BillingPeriodId = period.Id,
                DateOfService = serviceDate,
                ProcedureCode = agency!.BillingProcedureCode!,
                ProcedureModifier = agency.BillingModifier,
                Units = BillingRules.CalculateSection13Units(row.Note.Minutes),
                ChargeAmount = BillingRules.CalculateCharge(
                    BillingRules.CalculateSection13Units(row.Note.Minutes),
                    agency.BillingUnitRate!.Value),
                ClientMaineCareId = row.Person.MaineCareId!,
                RenderingProviderNpi = agency!.Npi!,
                DiagnosisCode = row.Person.DiagnosisCode!,
                PlaceOfService = row.Person.PlaceOfService!.Value,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(
                    CreateClaimSnapshot(row.Person, agency)),
                // The approved note is the sole authority for a compliance
                // exception. A billing request must not be able to add, remove,
                // or rewrite this regulated financial-record fact.
                IsComplianceException = row.Note.ComplianceOverride,
                ComplianceExceptionReason = row.Note.ComplianceOverride
                    ? Normalize(row.Note.OverrideReason)
                    : null
            };
            period.Lines.Add(line);
            auditTrail.Record(actor, AuditActions.BillingClaimLineCreated, "Note", request.NoteId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateClaimLine(exception))
            {
                return DuplicateClaimLineConflict();
            }
            return Results.Ok(ContractMapper.ToClaimLine(line));
        });

        api.MapGet("/billing/claim-lines/draft", async Task<IResult> (
            int userId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var lines = await (from line in db.ClaimLines.AsNoTracking()
                               join period in db.BillingPeriods.AsNoTracking() on line.BillingPeriodId equals period.Id
                               join owner in db.Users.AsNoTracking() on period.UserId equals owner.Id
                               where period.UserId == userId && period.Status == 0 && owner.AgencyId == actor.AgencyId
                               orderby line.DateOfService
                               select line).ToListAsync(cancellationToken);
            return Results.Ok(lines.Select(ContractMapper.ToClaimLine).ToList());
        });

        api.MapPost("/billing/periods/{periodId:int}/submit", async Task<IResult> (
            int periodId,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            var period = await (from candidate in db.BillingPeriods.Include(value => value.Lines)
                                join owner in db.Users on candidate.UserId equals owner.Id
                                where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                select candidate).SingleOrDefaultAsync(cancellationToken);
            if (period is null)
                return Results.NotFound();
            if (period.Status == 1)
                return Results.Ok(ContractMapper.ToBillingPeriod(period));
            if (period.Status != 0)
                return Results.Conflict(new ApiErrorDto("invalid_period_status", "Only draft billing periods can be submitted.", string.Empty));
            if (period.Lines.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["period"] = ["A billing period with no claim lines cannot be submitted."]
                });
            }
            period.Status = 1;
            period.SubmittedAt = DateTime.UtcNow;
            auditTrail.Record(actor, AuditActions.BillingPeriodSubmitted, "BillingPeriod", periodId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                var completed = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                                       join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                       where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                       select candidate).SingleOrDefaultAsync(cancellationToken);
                if (completed?.Status == 1)
                    return Results.Ok(ContractMapper.ToBillingPeriod(completed));
                return Results.Conflict(new ApiErrorDto("billing_period_changed", "The billing period changed while it was being submitted.", string.Empty));
            }
            return Results.Ok(ContractMapper.ToBillingPeriod(period));
        });

        api.MapPost("/billing/periods/{periodId:int}/edi", async Task<IResult> (
            int periodId,
            GenerateEdiRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            AuditTrail auditTrail,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            if (actor.Role != "Admin")
                return Results.Forbid();
            if (!Guid.TryParse(request.IdempotencyKey, out var parsedKey))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["idempotencyKey"] = ["A valid idempotency key is required."]
                });
            }
            var normalizedKey = parsedKey.ToString("N");
            var previous = await db.EdiGenerations.AsNoTracking().SingleOrDefaultAsync(generation =>
                generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.UserId &&
                generation.IdempotencyKey == normalizedKey, cancellationToken);
            if (previous is not null)
                return ReplayEdiOrConflict(previous, periodId, request.IsTest);

            var period = await (from candidate in db.BillingPeriods.AsNoTracking().Include(value => value.Lines)
                                join owner in db.Users.AsNoTracking() on candidate.UserId equals owner.Id
                                where candidate.Id == periodId && owner.AgencyId == actor.AgencyId
                                select candidate).SingleOrDefaultAsync(cancellationToken);
            if (period is null)
                return Results.NotFound();
            if (period.Lines.Count == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["The billing period has no claim lines."] });
            if (period.Status != 1)
                return Results.Conflict(new ApiErrorDto(
                    "billing_period_not_submitted",
                    "Submit and lock the billing period before generating its 837P file.",
                    string.Empty));

            var noteIds = period.Lines.Select(line => line.NoteId).Distinct().ToList();
            var sourceRows = await (from note in db.Notes.AsNoTracking()
                                    join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                                    join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
                                    where noteIds.Contains(note.Id) &&
                                          owner.AgencyId == actor.AgencyId &&
                                          person.AgencyId == actor.AgencyId
                                    select new { Note = note, Person = person })
                .ToListAsync(cancellationToken);
            if (sourceRows.Select(row => row.Note.Id).Distinct().Count() != noteIds.Count)
            {
                return Results.Conflict(new ApiErrorDto(
                    "invalid_billing_source",
                    "The billing period contains a note outside the agency boundary or a missing source record.",
                    string.Empty));
            }

            var generatedAt = DateTime.Now;
            var controlNumber = CreateEdiControlNumber(normalizedKey);
            var content = ServerEdiGenerator.Generate(
                period, request.IsTest, generatedAt, controlNumber);
            var timestamp = generatedAt.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var testMarker = request.IsTest ? ".OATEST" : string.Empty;
            var file = new EdiFileDto($"837P{testMarker}_{period.Year}{period.Month:D2}_{timestamp}_{normalizedKey[..8]}.txt", content);
            db.EdiGenerations.Add(new ServerEdiGeneration
            {
                AgencyId = actor.AgencyId,
                ActorUserId = actor.UserId,
                BillingPeriodId = periodId,
                IdempotencyKey = normalizedKey,
                IsTest = request.IsTest,
                FileName = file.FileName,
                Content = file.Content,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
            {
                AgencyId = actor.AgencyId,
                BillingPeriodId = periodId,
                OccurredAtUtc = DateTime.UtcNow,
                Stage = BillingSubmissionStage.Generated,
                Reference = file.FileName,
                Explanation = request.IsTest
                    ? "Test 837P generated; no external transmission is implied."
                    : "Production 837P generated; transmission status has not been recorded.",
                IsSynthetic = false
            });
            auditTrail.Record(actor, AuditActions.BillingEdiGenerated, "BillingPeriod", periodId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateEdiGeneration(exception))
            {
                db.ChangeTracker.Clear();
                var completed = await db.EdiGenerations.AsNoTracking().SingleAsync(generation =>
                    generation.AgencyId == actor.AgencyId && generation.ActorUserId == actor.UserId &&
                    generation.IdempotencyKey == normalizedKey, cancellationToken);
                return ReplayEdiOrConflict(completed, periodId, request.IsTest);
            }
            return Results.Ok(file);
        });
    }

    private static void MapForms(RouteGroupBuilder api)
    {
        api.MapPost("/forms/delete", async Task<IResult> (
            DeleteFormsRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var ids = request.FormIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["formIds"] = ["No more than 100 forms may be deleted at once."]
                });
            }
            if (ids.Count == 0)
                return Results.Ok(new CountDto(0));

            var actor = Actor.From(principal);
            var ownedIds = await (from form in db.Forms.AsNoTracking()
                                  join person in db.People.AsNoTracking() on form.PersonId equals person.Id
                                  where ids.Contains(form.Id) && person.UserId == actor.UserId
                                  select form.Id).ToListAsync(cancellationToken);
            if (ownedIds.Count != ids.Count)
                return Results.NotFound();

            var deleted = await db.Forms.Where(form => ownedIds.Contains(form.Id))
                .ExecuteDeleteAsync(cancellationToken);
            return Results.Ok(new CountDto(deleted));
        });

        api.MapPut("/forms/{id:int}", async Task<Results<Ok<FormDto>, NotFound>> (
            int id,
            UpdateFormRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var actor = Actor.From(principal);
            var form = await (from f in db.Forms
                              join p in db.People on f.PersonId equals p.Id
                              where f.Id == id && p.UserId == actor.UserId
                              select f).SingleOrDefaultAsync(cancellationToken);
            if (form is null)
                return TypedResults.NotFound();
            form.CompletedDate = request.CompletedDate?.Date;
            form.OpenedDate = request.OpenedDate?.Date;
            form.IsCompliant = request.CompletedDate.HasValue;
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ContractMapper.ToForm(form));
        });
    }

    private static async Task<PersonDto> LoadPersonDtoAsync(
        ApiDbContext db,
        ServerPerson person,
        CancellationToken cancellationToken)
    {
        var forms = await db.Forms.AsNoTracking()
            .Where(x => x.PersonId == person.Id)
            .ToListAsync(cancellationToken);
        var notes = await db.Notes.AsNoTracking()
            .Where(x => x.PersonId == person.Id)
            .ToListAsync(cancellationToken);
        return ContractMapper.ToPerson(person, forms, notes);
    }

    private static Task<ServerPerson?> LoadAuditablePersonAsync(
        ApiDbContext db,
        Actor actor,
        int personId,
        CancellationToken cancellationToken) =>
        (from person in db.People
         join owner in db.Users.AsNoTracking() on person.UserId equals owner.Id
         where person.Id == personId &&
               person.AgencyId == actor.AgencyId &&
               owner.AgencyId == actor.AgencyId
         select person).SingleOrDefaultAsync(cancellationToken);

    private static Dictionary<string, string[]> ValidatePerson(
        SavePersonRequest request,
        bool requireNewForms) =>
        PersonSaveRules.Validate(request, DateTime.Today, requireNewForms);

    private static void ApplyPerson(ServerPerson person, SavePersonRequest request, int gender, int waiver)
    {
        person.FirstName = request.FirstName.Trim();
        person.LastName = request.LastName.Trim();
        person.BirthDate = request.BirthDate.Date;
        person.Gender = gender;
        person.EffectiveDate = request.EffectiveDate?.Date;
        person.Bio = request.Bio?.Trim();
        person.Waiver = waiver;
        person.MaineCareId = Normalize(request.MaineCareId);
        person.DiagnosisCode = Normalize(request.DiagnosisCode);
        person.PlaceOfService = request.PlaceOfService;
        person.EvergreenId = Normalize(request.EvergreenId);
        person.OpenWithVR = request.OpenWithVR;
        person.HasGuardian = request.HasGuardian;
        person.GuardianName = Normalize(request.GuardianName);
        person.PhoneNumber = Normalize(request.PhoneNumber);
        person.Email = Normalize(request.Email);
        person.Address = Normalize(request.Address);
        if (request.UpdateBillingAddress)
        {
            person.BillingStreet = Normalize(request.BillingStreet);
            person.BillingCity = Normalize(request.BillingCity);
            person.BillingState = Normalize(request.BillingState)?.ToUpperInvariant();
            person.BillingZip = Normalize(request.BillingZip);
        }
        person.PrimaryCareProvider = Normalize(request.PrimaryCareProvider);
        person.HealthcareSystemName = Normalize(request.HealthcareSystemName);
        person.CaseManagerIsRepPayee = request.CaseManagerIsRepPayee;
        person.CaseManagerIsDhhsRepresentative = request.CaseManagerIsDhhsRepresentative;
        person.UsesModivcare = request.UsesModivcare;
        person.RepPayeeMonthlyIncome = request.CaseManagerIsRepPayee
            ? request.RepPayeeMonthlyIncome
            : null;
        person.RepPayeeRegularCheckRequestNeeds = request.CaseManagerIsRepPayee
            ? Normalize(request.RepPayeeRegularCheckRequestNeeds)
            : null;
        person.HasHomeSupport = request.HasHomeSupport;
        person.HasSelfDirectedHomeSupport = request.HasSelfDirectedHomeSupport;
        person.HasSharedLiving = request.HasSharedLiving;
        person.HasCommunitySupport1To1 = request.HasCommunitySupport1To1;
        person.HasCommunitySupportSelfDirected = request.HasCommunitySupportSelfDirected;
        person.HasCommunitySupportDayProgram = request.HasCommunitySupportDayProgram;
        person.DayProgramCount = request.HasCommunitySupportDayProgram ? request.DayProgramCount : 1;
        person.IsEmployed = request.IsEmployed;
        person.HasEmploymentSpecialist = request.IsEmployed && request.HasEmploymentSpecialist;
        person.HasWorkSupports = request.IsEmployed && request.HasWorkSupports;
    }

    private static List<ServerForm> BuildInitialForms(
        IReadOnlyList<SavePersonFormRequest> requestForms,
        DateTime effectiveDate,
        ServerSettings settings)
    {
        var byType = requestForms
            .Where(form => form.Id == 0)
            .ToDictionary(form => form.Type, StringComparer.Ordinal);
        var cycleEnd = effectiveDate.AddYears(1);
        var forms = new List<ServerForm>(ContractMapper.FormTypeCount);
        for (var type = 0; type < ContractMapper.FormTypeCount; type++)
        {
            var typeName = ContractMapper.FormTypeName(type);
            var requested = byType[typeName];
            forms.Add(new ServerForm
            {
                Type = typeName,
                DueDate = ComputeFormDueDate(type, effectiveDate, cycleEnd, settings),
                IsCompliant = requested.IsCompliant,
                CompletedDate = requested.CompletedDate?.Date,
                OpenedDate = requested.OpenedDate?.Date
            });
        }
        return forms;
    }

    private static DateTime ComputeFormDueDate(
        int type,
        DateTime cycleStart,
        DateTime cycleEnd,
        ServerSettings settings) => type switch
    {
        0 => cycleStart.AddDays(90),
        1 => cycleStart.AddDays(180),
        2 => cycleStart.AddDays(270),
        3 => cycleEnd.AddDays(-settings.Q4RDaysBeforeAnniversary),
        4 => cycleEnd.AddDays(-settings.PcpDaysBeforeAnniversary),
        5 => cycleEnd.AddDays(-settings.CompAssessmentDaysBeforeAnniversary),
        6 => cycleEnd.AddDays(-settings.ReclassificationDaysBeforeAnniversary),
        7 => cycleEnd.AddDays(-settings.SafetyPlanDaysBeforeAnniversary),
        8 => cycleEnd.AddDays(-settings.PrivacyPracticesDaysBeforeAnniversary),
        9 => cycleEnd.AddDays(-settings.ReleaseAgencyDaysBeforeAnniversary),
        10 => cycleEnd.AddDays(-settings.ReleaseDhhsDaysBeforeAnniversary),
        11 => cycleEnd.AddDays(-settings.ReleaseMedicalDaysBeforeAnniversary),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static Dictionary<string, string[]> ValidatePersonContact(SavePersonContactRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Trim().Length > 75)
            errors["firstName"] = ["First name is required and must not exceed 75 characters."];
        if (string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Trim().Length > 75)
            errors["lastName"] = ["Last name is required and must not exceed 75 characters."];
        if (request.Kind is not ("Personal" or "Guardian" or "AuthorizedRepresentative" or
            "ServiceProvider" or "HealthcareProvider" or "Other"))
            errors["kind"] = ["Contact type is invalid."];
        ValidateLength(errors, "relationship", request.Relationship, 100);
        ValidateLength(errors, "organization", request.Organization, 150);
        ValidateLength(errors, "phone", request.Phone, 30);
        ValidateLength(errors, "email", request.Email, 254);
        return errors;
    }

    private static void ApplyPersonContact(ServerPersonContact contact, SavePersonContactRequest request)
    {
        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.Kind = request.Kind;
        contact.Relationship = Normalize(request.Relationship);
        contact.Organization = Normalize(request.Organization);
        contact.Phone = Normalize(request.Phone);
        contact.Email = Normalize(request.Email);
        contact.IsEmergencyContact = request.IsEmergencyContact;
        contact.HasActiveRelease = request.HasActiveRelease;
        contact.IsActive = true;
    }

    private static void ValidateLength(
        Dictionary<string, string[]> errors,
        string field,
        string? value,
        int maximum)
    {
        if (value?.Trim().Length > maximum)
            errors[field] = [$"Value must not exceed {maximum} characters."];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<ReviewableNote?> LoadReviewableNoteAsync(
        ApiDbContext db,
        Actor actor,
        int noteId,
        CancellationToken cancellationToken)
    {
        if (!TenantAccess.IsSupervisorRole(actor.Role))
            return null;

        // The client's own agency is checked alongside its owner's. A person row
        // carrying a different agency than the case manager who holds it must not
        // become reviewable through that case manager.
        return await (from note in db.Notes
                      join person in db.People on note.PersonId equals person.Id
                      join owner in db.Users on person.UserId equals owner.Id
                      where note.Id == noteId && owner.AgencyId == actor.AgencyId &&
                            person.AgencyId == actor.AgencyId && owner.Role == "CaseManager" &&
                            (actor.Role == "Director" || actor.Role == "Admin" ||
                             owner.SupervisorId == actor.UserId)
                      select new ReviewableNote(note, person)).SingleOrDefaultAsync(cancellationToken);
    }

    private static BillingComplianceResult EvaluatePersonCompliance(
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        DateTime today,
        BillingComplianceRequirements requirements)
        => BillingComplianceGate.Evaluate(
            person.EffectiveDate,
            forms.Select(form => new ComplianceFormSnapshot(
                form.Type, form.DueDate, form.CompletedDate)),
            today,
            requirements: requirements);

    private static IReadOnlyList<string> ValidateBillingCandidate(
        ServerNote note,
        ServerPerson person,
        ServerAgency? agency,
        IReadOnlyList<ServerForm> forms,
        DateTime today,
        BillingComplianceRequirements requirements)
    {
        var errors = new List<string>();
        if (note.Status != 6)
            errors.Add("Service note is not approved.");
        if (note.EventDate is null)
            errors.Add("No service date.");
        if (BillingRules.CalculateSection13Units(note.Minutes) < 1)
            errors.Add("Units must be at least 1.");
        if (string.IsNullOrWhiteSpace(person.MaineCareId))
            errors.Add("Consumer has no MaineCare ID.");
        if (!BillingRules.IsValidDiagnosisCode(person.DiagnosisCode))
            errors.Add("Consumer diagnosis code is missing or invalid.");
        if (person.PlaceOfService is null)
            errors.Add("Consumer has no place of service.");
        if (!HasValidSubscriberClaimIdentity(person))
            errors.Add("Consumer claim name, birth date, or structured claim address is incomplete or invalid.");
        if (!BillingRules.IsValidNpi(agency?.Npi))
            errors.Add("Agency NPI is missing or invalid.");
        if (agency is not null)
            errors.AddRange(ValidateBillingConfiguration(agency));

        if (note.ComplianceOverride)
        {
            if (string.IsNullOrWhiteSpace(note.OverrideReason) ||
                note.OverrideApprovedById is null || note.OverrideApprovedAt is null)
                errors.Add("Compliance override is incomplete.");
        }
        else
        {
            if (!EvaluatePersonCompliance(person, forms, today, requirements).Passed)
                errors.Add("Consumer does not meet current compliance requirements.");
            if (note.EventDate is DateTime serviceDate)
            {
                errors.AddRange(BillingComplianceGate.EvaluateBillingWindow(
                    forms.Select(form => new ComplianceFormSnapshot(
                        form.Type, form.DueDate, form.CompletedDate)),
                    serviceDate,
                    requirements));
            }
        }
        return errors;
    }

    private static IReadOnlyList<string> ValidateBillingConfiguration(ServerAgency agency)
    {
        var errors = new List<string>();
        if (!BillingRules.IsValidProcedureCode(agency.BillingProcedureCode))
            errors.Add("Agency billing procedure code is missing or invalid.");
        if (!BillingRules.IsValidModifier(agency.BillingModifier))
            errors.Add("Agency billing modifier is invalid.");
        if (agency.BillingUnitRate is null or <= 0)
            errors.Add("Agency billing unit rate is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiSubmitterId, 15))
            errors.Add("EDI submitter ID is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiPayerName, 60) ||
            !BillingRules.IsSafeX12Element(agency.EdiPayerId, 80))
            errors.Add("EDI payer name or payer ID is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.EdiContactName, 60) ||
            agency.EdiContactPhone is null || agency.EdiContactPhone.Length is < 10 or > 15 ||
            agency.EdiContactPhone.Any(character => !char.IsDigit(character)))
            errors.Add("EDI contact name or telephone number is missing or invalid.");
        if (!BillingRules.IsSafeX12Element(agency.Name, 60) ||
            !BillingRules.IsValidNpi(agency.Npi) ||
            !BillingRules.IsSafeX12Element(agency.TaxId, 50) ||
            !BillingRules.IsSafeX12Element(agency.Street, 55) ||
            !BillingRules.IsSafeX12Element(agency.City, 30) ||
            !BillingRules.IsSafeX12Element(agency.State, 2) ||
            !BillingRules.IsSafeX12Element(agency.Zip, 15))
            errors.Add("Agency billing provider name, NPI, tax ID, or structured address is incomplete or invalid.");
        return errors;
    }

    private static bool HasValidSubscriberClaimIdentity(ServerPerson person) =>
        BillingRules.IsSafeX12Element(person.FirstName, 35) &&
        BillingRules.IsSafeX12Element(person.LastName, 60) &&
        person.BirthDate >= new DateTime(1900, 1, 1) &&
        BillingRules.IsSafeX12Element(person.BillingStreet, 55) &&
        BillingRules.IsSafeX12Element(person.BillingCity, 30) &&
        BillingRules.IsSafeX12Element(person.BillingState, 2) &&
        BillingRules.IsSafeX12Element(person.BillingZip, 15);

    private static ProfessionalClaimSnapshot CreateClaimSnapshot(ServerPerson person, ServerAgency agency) => new(
        ProfessionalClaimSnapshotCodec.CurrentVersion,
        agency.Id,
        person.Id,
        person.FirstName!,
        person.LastName!,
        person.BirthDate.Date,
        person.Gender == 1 ? "M" : person.Gender == 2 ? "F" : "U",
        person.MaineCareId!,
        person.BillingStreet!,
        person.BillingCity!,
        person.BillingState!,
        person.BillingZip!,
        agency.Name,
        agency.Npi!,
        agency.TaxId!,
        agency.Street!,
        agency.City!,
        agency.State!,
        agency.Zip!,
        agency.EdiSubmitterId!,
        agency.EdiContactName!,
        agency.EdiContactPhone!,
        agency.EdiPayerName!,
        agency.EdiPayerId!);

    private static DateTime? CurrentCycleAnchor(DateTime? effectiveDate, DateTime today)
    {
        if (effectiveDate is null || effectiveDate.Value.Date > today.Date) return null;
        var start = effectiveDate.Value.Date;
        var years = today.Year - start.Year;
        if (today.Date < start.AddYears(years)) years--;
        return start.AddYears(years);
    }

    private static IEnumerable<(int Quarter, string Category, int SlotIndex)> RequiredReviewItems(ServerPerson person)
    {
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            yield return (quarter, "Medical", 0);
            yield return (quarter, "Dental", 0);
            yield return (quarter, "GoalReview", 0);
            if (person.IsEmployed && !person.HasEmploymentSpecialist && !person.HasWorkSupports && !person.OpenWithVR)
                yield return (quarter, "Employment", 0);
        }
        var arrangements = new List<(string Category, int SlotIndex)>();
        if (person.HasHomeSupport) arrangements.Add(("NoteReviewHome", arrangements.Count(x => x.Category == "NoteReviewHome")));
        if (person.HasSharedLiving) arrangements.Add(("NoteReviewHome", arrangements.Count(x => x.Category == "NoteReviewHome")));
        if (person.HasCommunitySupport1To1) arrangements.Add(("NoteReviewCommunity", arrangements.Count(x => x.Category == "NoteReviewCommunity")));
        if (person.HasCommunitySupportDayProgram)
            for (var i = 0; i < Math.Max(0, person.DayProgramCount); i++)
                arrangements.Add(("NoteReviewCommunity", arrangements.Count(x => x.Category == "NoteReviewCommunity")));
        if (arrangements.Count == 0) yield break;
        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var arrangement = arrangements[(quarter - 1) % arrangements.Count];
            yield return (quarter, arrangement.Category, arrangement.SlotIndex);
        }
    }

    private static async Task<ServerReviewItem?> LoadAccessibleReviewAsync(
        ApiDbContext db, Actor actor, int reviewItemId, CancellationToken cancellationToken)
    {
        var item = await db.ReviewItems.Include(x => x.Appointment).SingleOrDefaultAsync(x => x.Id == reviewItemId, cancellationToken);
        if (item is null) return null;
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == item.PersonId, cancellationToken);
        return person is not null &&
               await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)
            ? item
            : null;
    }

    private static Task<ServerAppointment?> LatestAppointmentAsync(
        ApiDbContext db, int personId, string category, CancellationToken cancellationToken) =>
        (from appointment in db.Appointments.AsNoTracking()
         join review in db.ReviewItems on appointment.ReviewItemId equals review.Id
         where review.PersonId == personId && review.Category == category
         orderby appointment.Date descending
         select appointment).FirstOrDefaultAsync(cancellationToken);

    private static int CalculateUnits(int? minutes) =>
        minutes.HasValue ? Math.Max(1, (int)Math.Ceiling(minutes.Value / 15.0)) : 0;

    private static IResult StaleAssessmentConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_assessment",
            "This assessment was changed after you opened it. Reload it before saving or submitting.",
            string.Empty));

    private static IResult StalePersonConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_person",
            "This person record was changed after you opened it. Reload it before saving.",
            string.Empty));

    private static IResult StaleTestConsumerConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_test_consumer",
            "This consumer changed after you selected them. Refresh the Admin dashboard and review the current record before trying again.",
            string.Empty));

    private static IResult StaleNoteConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_note",
            "This note changed after it was opened. Reload the saved copy before applying your changes.",
            string.Empty));

    /// <summary>
    /// The shape both AT request list routes project to. A named type rather than
    /// an anonymous one so the two routes can share the row-building step below
    /// instead of each re-expressing the total.
    /// </summary>
    private sealed class AtRequestRow
    {
        public int Id { get; init; }
        public string? ClientName { get; init; }
        public string Status { get; init; } = string.Empty;
        public decimal SalesTax { get; init; }
        public DateTime? SubmittedDate { get; init; }
        public string? VendorName { get; init; }
        public string? CaseManagerName { get; init; }
        public decimal? PassthroughRate { get; init; }
        public string? SignedByName { get; init; }
        public DateTime? SignedAtUtc { get; init; }
        public bool HasSnapshot { get; init; }
    }

    /// <summary>
    /// Attaches item totals and produces the list DTOs.
    ///
    /// MIRRORED MATH — the canonical definition is ATRequestCalculator.Total. It
    /// is re-expressed here because the sum is done in SQL without loading item
    /// rows. A request's FROZEN rate wins over the agency's current one, so a
    /// published row keeps reporting the total it was filed at.
    /// </summary>
    private static async Task<List<AtRequestListItemDto>> BuildAtRequestRowsAsync(
        ApiDbContext db, List<AtRequestRow> requests, decimal currentRate, CancellationToken cancellationToken)
    {
        var requestIds = requests.Select(x => x.Id).ToList();
        var totals = await db.AtRequestItems.AsNoTracking()
            .Where(x => requestIds.Contains(x.ATRequestId))
            .GroupBy(x => x.ATRequestId)
            .Select(x => new { RequestId = x.Key, Total = x.Sum(i => i.ItemCost * i.Quantity) })
            .ToDictionaryAsync(x => x.RequestId, x => x.Total, cancellationToken);

        return [.. requests.Select(request => new AtRequestListItemDto(
            request.Id, request.ClientName, request.Status,
            (totals.GetValueOrDefault(request.Id) + request.SalesTax)
                * (1 + (request.PassthroughRate ?? currentRate)),
            request.SubmittedDate, request.VendorName, request.CaseManagerName, request.HasSnapshot,
            request.SignedByName, request.SignedAtUtc))];
    }

    private static IResult StaleAtRequestConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_at_request",
            "This AT request changed after it was opened. Reload the saved request before applying your changes.",
            string.Empty));

    // Distinct code from stale_at_request: the client cannot fix this by
    // reloading and retrying, which is exactly what a stale conflict invites.
    private static IResult PublishedAtRequestConflict() =>
        Results.Conflict(new ApiErrorDto(
            "published_at_request",
            "This AT request has been published and can no longer be edited. Reopen it for correction first; reopening removes the attestation.",
            string.Empty));

    // Publication state is read through the shared rule owner rather than tested
    // field by field, so the API and the desktop agree on what counts as signed.
    private static bool IsAtRequestPublished(ServerAtRequest request) =>
        AtRequestPublication.IsPublished(request.SignedByName, request.SignedAtUtc);

    private static IResult StaleSettingsConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_settings",
            "Agency settings changed after this window was opened. Review the latest settings before trying again.",
            string.Empty));

    private static IResult StaleScratchpadConflict() =>
        Results.Conflict(new ApiErrorDto(
            "stale_scratchpad",
            "Your scratchpad changed in another Sati session. Reload the saved copy before trying again.",
            string.Empty));

    private static IResult DuplicateClaimLineConflict() =>
        Results.Conflict(new ApiErrorDto(
            "claim_line_exists",
            "This service note already has a billing claim line.",
            string.Empty));

    private static bool IsDuplicateClaimLine(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains("IX_ClaimLines_NoteId", StringComparison.Ordinal);

    private static IResult ReplayEdiOrConflict(
        ServerEdiGeneration generation,
        int billingPeriodId,
        bool isTest) =>
        generation.BillingPeriodId == billingPeriodId && generation.IsTest == isTest
            ? Results.Ok(new EdiFileDto(generation.FileName, generation.Content))
            : Results.Conflict(new ApiErrorDto(
                "idempotency_key_reused",
                "This retry key was already used for a different EDI request.",
                string.Empty));

    private static string CreateEdiControlNumber(string normalizedKey) =>
        (Convert.ToUInt32(normalizedKey[..8], 16) % 1_000_000_000)
        .ToString("D9", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsDuplicateEdiGeneration(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains("IX_EdiGenerations_AgencyId_ActorUserId_IdempotencyKey", StringComparison.Ordinal)
        || exception.InnerException?.Message.Contains(
            "EdiGenerations.AgencyId, EdiGenerations.ActorUserId, EdiGenerations.IdempotencyKey",
            StringComparison.Ordinal) == true;

    // Format and escaping are owned by Sati.Contracts AuditCsv, shared with the
    // desktop's local export. Do not hand-build this file here.
    private static string BuildAuditCsv(
        IReadOnlyList<AuditExportRow> rows,
        string reason,
        DateTime exportedAtUtc) =>
        AuditCsv.Build(
            rows.Select(row => new AuditCsvRow(
                row.EventId,
                row.OccurredAtUtc,
                row.ActorUserId,
                row.ActorDisplayName,
                row.Action,
                row.ResourceType,
                row.ResourceId,
                row.CorrelationId)),
            reason,
            exportedAtUtc);

    private sealed record AuditExportRow(
        Guid EventId,
        DateTime OccurredAtUtc,
        int ActorUserId,
        string ActorDisplayName,
        string Action,
        string ResourceType,
        string? ResourceId,
        string CorrelationId);

    private sealed record BillingLossPersonRow(
        int Id,
        string? FirstName,
        string? LastName,
        DateTime? EffectiveDate);

    private sealed record BillingLossFormRow(
        int PersonId,
        string Type,
        DateTime DueDate,
        DateTime? CompletedDate);

    private sealed record BillingLossNoteRow(int PersonId, DateTime EventDate, int? Minutes);

    private sealed record ReviewableNote(ServerNote Note, ServerPerson Person);

    private static async Task<ServerSettings> GetOrCreateSettingsAsync(
        ApiDbContext db,
        int agencyId,
        CancellationToken cancellationToken)
    {
        var settings = await db.Settings.SingleOrDefaultAsync(
            x => x.AgencyId == agencyId,
            cancellationToken);
        if (settings is not null)
            return settings;

        settings = new ServerSettings { AgencyId = agencyId };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private sealed record DayNoteRow(ServerNote Note, ServerPerson Person);

    private static async Task<List<DayNoteRow>> LoadDayNotesAsync(
        ApiDbContext db, int userId, DateTime date, CancellationToken cancellationToken)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        return await (from note in db.Notes.AsNoTracking()
                      join person in db.People.AsNoTracking() on note.PersonId equals person.Id
                      where person.UserId == userId &&
                            note.EventDate >= dayStart && note.EventDate < dayEnd
                      select new DayNoteRow(note, person))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Server-side enforcement of the service-day rule. The desktop client checks
    /// the same <see cref="ServiceTimeline"/> rule for immediate feedback, but a
    /// rule that decides what may be billed cannot be enforced by a client, so
    /// this runs on every create and update regardless of what the client sent.
    /// Returns null when the request claims no time or claims only free time.
    /// </summary>
    private static async Task<IResult?> FindServiceTimeProblemAsync(
        ApiDbContext db,
        Actor actor,
        SaveNoteRequest request,
        int? editingNoteId,
        CancellationToken cancellationToken)
    {
        var candidate = ServiceTimeline.TryCreateBlock(
            editingNoteId ?? 0, request.StartTime, request.Minutes, request.Status);
        if (candidate is null)
            return null;

        var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
        if (windowProblem is not null)
            return Results.Conflict(new ApiErrorDto("service_time_window", windowProblem, string.Empty));

        if (request.EventDate is not DateTime eventDate)
            return null;

        var sameDay = await LoadDayNotesAsync(db, actor.UserId, eventDate, cancellationToken);
        var blocks = sameDay
            .Select(row => ServiceTimeline.TryCreateBlock(
                row.Note.Id,
                row.Note.StartTime,
                row.Note.Minutes,
                ContractMapper.NoteStatusName(row.Note.Status),
                DescribeNoteOwner(row.Person)))
            .OfType<ServiceBlock>();

        var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
        if (conflicts.Count == 0)
            return null;

        return Results.Conflict(new ApiErrorDto(
            "service_time_overlap",
            "This service time overlaps time already recorded on this date. " +
            string.Join(" ", conflicts.Select(conflict => conflict.Reason)),
            string.Empty));
    }

    private static string DescribeNoteOwner(ServerPerson person)
    {
        var name = $"{person.FirstName} {person.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "another note" : $"a note for {name}";
    }

    private static Dictionary<string, string[]>? ValidateNote(SaveNoteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Narrative is null || request.Narrative.Length > 1_000_000)
            errors["narrative"] = ["Narrative is required and must not exceed 1,000,000 characters."];
        if (request.PersonId <= 0)
            errors["personId"] = ["A valid person is required."];
        if (string.Equals(request.NoteType, NoteSchedulingPolicy.ReminderType, StringComparison.Ordinal) &&
            request.EventDate is null)
            errors["eventDate"] = ["A calendar reminder requires a date."];
        if (request.Minutes is < 0 or > 1_440)
            errors["minutes"] = ["Minutes must be between 0 and 1,440."];
        if (request.StartTime is int start && (start < 0 || start > ServiceTimeline.WindowLengthMinutes))
            errors["startTime"] = ["Service start time must fall inside the 7:00 AM to 7:00 PM logging window."];
        if (!ContractMapper.TryParseNoteStatus(request.Status, out _))
            errors["status"] = ["The note status is invalid."];
        else if (!ContractMapper.TryParseNoteStatus(request.Status, out var status) ||
                 !NoteWorkflow.IsCaseManagerWritableStatus(status))
            errors["status"] = ["That note status is controlled by a server workflow."];
        if (!ContractMapper.TryParseFormType(request.FormType, out _))
            errors["formType"] = ["The form type is invalid."];
        if (!ContractMapper.TryParseNoteType(request.NoteType, out _))
            errors["noteType"] = ["The note type is invalid."];
        return errors.Count == 0 ? null : errors;
    }

    private static async Task<Dictionary<string, string[]>> ValidateUserRequestAsync(
        ApiDbContext db, Actor actor, SaveUserRequest request, int? currentUserId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (username.Length is < 1 or > 50) errors["username"] = ["Username is required and must not exceed 50 characters."];
        if (displayName.Length is < 1 or > 150) errors["displayName"] = ["Display name is required and must not exceed 150 characters."];
        var roles = new[] { "CaseManager", "Supervisor", "Director", "Admin" };
        if (!roles.Contains(request.Role)) errors["role"] = ["The user role is invalid."];
        if (request.AgencyId != actor.AgencyId) errors["agencyId"] = ["Users must belong to your agency."];
        if (request.Email?.Length > 254) errors["email"] = ["Email must not exceed 254 characters."];
        if (request.Phone?.Length > 30) errors["phone"] = ["Phone must not exceed 30 characters."];
        if (actor.Role == "Supervisor" && (request.Role != "CaseManager" || request.SupervisorId != actor.UserId))
            errors["role"] = ["Supervisors may manage only their assigned case managers."];
        if (actor.Role == "Director" && request.Role == "Admin") errors["role"] = ["Only an administrator may create or assign an administrator."];
        if (!string.IsNullOrWhiteSpace(username) && await db.Users.AsNoTracking().AnyAsync(
                x => x.Username == username && x.Id != currentUserId, cancellationToken))
            errors["username"] = ["A user with that username already exists."];
        if (request.SupervisorId.HasValue && !await db.Users.AsNoTracking().AnyAsync(
                x => x.Id == request.SupervisorId && x.AgencyId == actor.AgencyId &&
                     (x.Role == "Supervisor" || x.Role == "Admin"), cancellationToken))
            errors["supervisorId"] = ["The selected supervisor is invalid."];
        return errors;
    }

    private static bool ValidPassword(string? password) => password?.Length is >= 8 and <= 128;

    private static Dictionary<string, string[]>? ValidateIncidentReport(IncidentReportRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsSafeIncidentToken(request.Reference, 6, 40))
            errors["reference"] = ["Reference must contain 6-40 letters, numbers, hyphens, or underscores."];
        if (request.Source is not ("Desktop" or "Api"))
            errors["source"] = ["Source must be Desktop or Api."];
        if (!IncidentSeverities.IsValid(request.Severity))
            errors["severity"] = ["Severity must be Warning, Error, or Critical."];
        if (!IsSafeIncidentToken(request.Operation, 1, 80))
            errors["operation"] = ["Operation must contain only letters, numbers, dots, hyphens, or underscores."];
        if (!IsSafeIncidentToken(request.Release, 1, 30))
            errors["release"] = ["Release contains unsupported characters."];
        if (string.IsNullOrWhiteSpace(request.ExceptionFingerprint) ||
            request.ExceptionFingerprint.Length is < 12 or > 64 ||
            request.ExceptionFingerprint.Any(character => !Uri.IsHexDigit(character)))
            errors["exceptionFingerprint"] = ["Exception fingerprint must be 12-64 hexadecimal characters."];
        return errors.Count == 0 ? null : errors;
    }

    private static bool IsSafeIncidentToken(string? value, int minimumLength, int maximumLength) =>
        value?.Length >= minimumLength && value.Length <= maximumLength &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static IncidentGroupDto ToIncidentDto(ServerIncidentGroup incident) => new(
        incident.Id,
        incident.AgencyId,
        incident.Scope,
        incident.Source,
        incident.Severity,
        incident.Operation,
        incident.FirstRelease,
        incident.LastRelease,
        incident.ExceptionFingerprint,
        incident.Status,
        incident.OccurrenceCount,
        incident.FirstSeenUtc,
        incident.LastSeenUtc,
        incident.LastReference,
        incident.LastActorRole);

    /// <summary>
    /// Refuses a second directory entry for an organization this agency has already
    /// recorded under the same durable identifier. The database enforces this with a
    /// filtered unique index; this check exists so the answer names the existing
    /// entry instead of surfacing a constraint violation.
    ///
    /// Scope is deliberately one agency. The same organization appearing in several
    /// agencies' directories is correct — each holds its own local knowledge of that
    /// organization — and must not be treated as a duplicate.
    /// </summary>
    // Affiliation is decided by ProviderAffiliation in Sati.Contracts, the same call the
    // transitional desktop service makes. Only this agency's rows are loaded, so a parent
    // belonging to another tenant fails as "not in this directory" rather than linking
    // across the boundary.
    private static async Task<Dictionary<string, string[]>> ValidateProviderAffiliationAsync(
        ApiDbContext db,
        int agencyId,
        SaveProviderRequest request,
        int editingProviderId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        MedicalProviderKind? kind = null;
        if (!string.IsNullOrWhiteSpace(request.MedicalKind))
        {
            if (!Enum.TryParse<MedicalProviderKind>(request.MedicalKind, out var parsed))
            {
                errors["medicalKind"] = ["The medical provider designation is invalid."];
                return errors;
            }
            kind = parsed;
        }

        var kindProblem = ProviderAffiliation.ValidateKind(request.Type == "Healthcare", kind);
        if (kindProblem is not null)
        {
            errors["medicalKind"] = [kindProblem];
            return errors;
        }

        if (request.ParentProviderId is null)
            return errors;

        var directory = (await db.Providers.AsNoTracking()
                .Where(candidate => candidate.AgencyId == agencyId)
                .Select(candidate => new { candidate.Id, candidate.Name, candidate.ParentProviderId, candidate.MedicalKind })
                .ToListAsync(cancellationToken))
            .Select(candidate => new ProviderAffiliationNode(
                candidate.Id,
                candidate.Name,
                candidate.ParentProviderId,
                Enum.TryParse<MedicalProviderKind>(candidate.MedicalKind, out var storedKind) ? storedKind : null))
            .ToList();

        var parentProblem = ProviderAffiliation.ValidateParent(
            editingProviderId, kind, request.ParentProviderId, directory);
        if (parentProblem is not null)
            errors["parentProviderId"] = [parentProblem];

        return errors;
    }

    private static async Task<IResult?> FindDuplicateProviderAsync(
        ApiDbContext db,
        int agencyId,
        SaveProviderRequest request,
        int? editingProviderId,
        CancellationToken cancellationToken)
    {
        var npi = Normalize(request.Npi);
        var maineCareProviderId = Normalize(request.MaineCareProviderId);
        if (npi is null && maineCareProviderId is null)
            return null;

        var clash = await db.Providers.AsNoTracking()
            .Where(candidate => candidate.AgencyId == agencyId &&
                                (editingProviderId == null || candidate.Id != editingProviderId) &&
                                ((npi != null && candidate.Npi == npi) ||
                                 (maineCareProviderId != null && candidate.MaineCareProviderId == maineCareProviderId)))
            .Select(candidate => new { candidate.Name, candidate.Npi, candidate.MaineCareProviderId })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is null)
            return null;

        var which = npi is not null && clash.Npi == npi
            ? "National Provider Identifier"
            : "MaineCare provider identifier";

        return Results.Conflict(new ApiErrorDto(
            "duplicate_provider_identifier",
            $"\"{clash.Name}\" is already in this agency's provider directory with the same {which}. " +
            "Edit that entry rather than creating a second one, so the organization stays a single record.",
            string.Empty));
    }

    private static Dictionary<string, string[]> ValidateProvider(SaveProviderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Type is not ("Waiver" or "Healthcare" or "Other")) errors["type"] = ["The provider type is invalid."];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) errors["name"] = ["Provider name is required and must not exceed 200 characters."];
        if (request.OfferedServices < 0 || (request.OfferedServices & ~15) != 0) errors["offeredServices"] = ["The selected services are invalid."];

        // Durable identifiers. An NPI carries a Luhn check digit, so a typo is
        // detectable here rather than surfacing years later as a failed match
        // against an organization onboarding as a tenant. BillingRules already owns
        // that check for claim generation; validating it in one place keeps the two
        // from drifting.
        var npi = request.Npi?.Trim();
        if (!string.IsNullOrEmpty(npi) && !BillingRules.IsValidNpi(npi))
            errors["npi"] = ["The National Provider Identifier must be 10 digits with a valid check digit."];

        var maineCareProviderId = request.MaineCareProviderId?.Trim();
        if (maineCareProviderId is { Length: > 30 })
            errors["maineCareProviderId"] = ["The MaineCare provider identifier must not exceed 30 characters."];

        return errors;
    }

    private static void ApplyProvider(ServerProvider provider, SaveProviderRequest request)
    {
        provider.Type = request.Type; provider.Name = request.Name.Trim(); provider.Street = Normalize(request.Street);
        provider.City = Normalize(request.City); provider.State = Normalize(request.State); provider.Zip = Normalize(request.Zip);
        provider.PrimaryContact = Normalize(request.PrimaryContact); provider.Phone = Normalize(request.Phone);
        provider.OfferedServices = request.OfferedServices; provider.ProvidesPassthroughService = request.ProvidesPassthroughService;
        provider.BillingLocationEis = Normalize(request.BillingLocationEis); provider.ProgramContact = Normalize(request.ProgramContact);
        provider.BillingContact = Normalize(request.BillingContact);
        provider.Npi = Normalize(request.Npi); provider.MaineCareProviderId = Normalize(request.MaineCareProviderId);
        provider.MedicalKind = Normalize(request.MedicalKind); provider.ParentProviderId = request.ParentProviderId;
    }

    private static Dictionary<string, string[]> ValidateAtRequest(SaveAtRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var statuses = new[] { "Development", "Review", "Approved", "Denied", "Appeal", "Received", "Withdrawn" };
        if (!statuses.Contains(request.Status)) errors["status"] = ["The request status is invalid."];
        if (request.SalesTax < 0) errors["salesTax"] = ["Sales tax cannot be negative."];
        if (request.Items.Count > 500 || request.Items.Any(x => x.ItemCost < 0 || x.Quantity < 1 || x.Quantity > 10000))
            errors["items"] = ["Request items contain an invalid cost or quantity."];

        // Screenshots are re-checked here even though the desktop caps them at the
        // paste boundary. A client-side limit tells the user something useful; it
        // does not constrain what arrives in a request body.
        foreach (var item in request.Items)
        {
            if (item.ScreenshotBase64 is null)
                continue;

            var decoded = TryDecodeBase64(item.ScreenshotBase64);
            var problem = decoded is null
                ? "A screenshot was not valid base64 data."
                : AtRequestScreenshot.Describe(decoded);
            if (problem is not null)
            {
                errors["screenshots"] = [problem];
                break;
            }
        }
        return errors;
    }

    private static byte[]? DecodeScreenshot(string? base64) =>
        string.IsNullOrEmpty(base64) ? null : TryDecodeBase64(base64);

    private static byte[]? TryDecodeBase64(string value)
    {
        // Convert.FromBase64String throws on malformed input, and a bad paste is
        // a client mistake, not a server fault. TryFromBase64String needs a
        // buffer sized from the encoded length.
        var buffer = new byte[value.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(value, buffer, out var written)
            ? buffer.AsSpan(0, written).ToArray()
            : null;
    }

    private static void ApplyAtRequest(ServerAtRequest request, SaveAtRequestRequest input)
    {
        request.VendorName = Normalize(input.VendorName); request.VendorBillingLocation = Normalize(input.VendorBillingLocation);
        request.VendorProgramContact = Normalize(input.VendorProgramContact); request.VendorBillingContact = Normalize(input.VendorBillingContact);
        request.SalesTax = input.SalesTax; request.SalesTaxOverridden = input.SalesTaxOverridden;
        request.SubmittedDate = input.SubmittedDate?.Date;
        request.DecisionDate = input.DecisionDate?.Date; request.Status = input.Status;
        request.Items.Clear();
        foreach (var item in input.Items)
            request.Items.Add(new ServerAtRequestItem { Name = Normalize(item.Name), ItemCost = item.ItemCost,
                Quantity = item.Quantity, Url = Normalize(item.Url),
                ScreenshotPng = DecodeScreenshot(item.ScreenshotBase64) });
    }

    private static async Task<ServerAtRequest?> LoadAccessibleAtRequestAsync(
        ApiDbContext db, Actor actor, int id, CancellationToken cancellationToken)
    {
        var request = await db.AtRequests.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return null;
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.PersonId, cancellationToken);
        return person is not null &&
               await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, cancellationToken)
            ? request
            : null;
    }

    private static void PreventSensitiveResponseCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>
    /// Removes a stored SSN.
    ///
    /// Every part goes, including the last four. Leaving the tail behind would keep a
    /// consumer who asked to have their number removed partially on file, and would
    /// leave the mask claiming a number that can no longer be produced.
    /// </summary>
    private static void ClearSsn(ServerPerson person)
    {
        person.SsnCiphertext = null;
        person.SsnNonce = null;
        person.SsnTag = null;
        person.SsnWrappedKey = null;
        person.SsnKeyId = null;
        person.SsnLastFour = null;
    }

    private static async Task ProtectSsnAsync(
        ServerPerson person,
        int agencyId,
        string normalized,
        EnvelopeProtector protector,
        CancellationToken cancellationToken)
    {
        var binding = new FieldBinding(agencyId, person.Id, "Ssn");
        var protectedValue = await protector.ProtectAsync(normalized, binding, cancellationToken);
        person.SsnCiphertext = protectedValue.Ciphertext;
        person.SsnNonce = protectedValue.Nonce;
        person.SsnTag = protectedValue.Tag;
        person.SsnWrappedKey = protectedValue.WrappedDataKey;
        person.SsnKeyId = protectedValue.KeyId;
        person.SsnLastFour = SsnMask.LastFourOf(normalized);
    }

    private static string SafeFileName(string value)
    {
        var safe = new string(value
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray());
        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(safe.Trim('-')) ? "person" : safe.Trim('-');
    }

    private static string? ComposeAddress(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return present.Length == 0 ? null : string.Join(", ", present);
    }

}
