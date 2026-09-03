using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Reporting;

namespace Sati.Data;

public sealed class AdminService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    PersonAuditPdfExporter pdfExporter,
    ILegalHoldRegistry legalHoldRegistry) : IAdminService
{
    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var today = now.Date;
        var thirtyDaysAgo = now.AddDays(-30);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var agencyName = await context.Agencies.AsNoTracking()
            .Where(agency => agency.Id == actor.AgencyId)
            .Select(agency => agency.Name)
            .SingleAsync(cancellationToken);
        var userCount = await context.Users.AsNoTracking()
            .CountAsync(user => user.AgencyId == actor.AgencyId && user.Role != UserRole.PlatformOperator, cancellationToken);
        var caseManagerCount = await context.Users.AsNoTracking()
            .CountAsync(user => user.AgencyId == actor.AgencyId &&
                (user.Permissions & UserPermissions.CaseManagement) != 0, cancellationToken);
        var personCount = await context.People.AsNoTracking()
            .CountAsync(person => person.AgencyId == actor.AgencyId &&
                context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
                cancellationToken);
        var notesThisMonth = await context.Notes.AsNoTracking()
            .CountAsync(note => note.EventDate >= monthStart &&
                context.People.Any(person => person.Id == note.PersonId && person.AgencyId == actor.AgencyId &&
                    context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId)),
                cancellationToken);
        var activeUsers = await context.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= thirtyDaysAgo)
            .Select(auditEvent => auditEvent.ActorUserId)
            .Distinct()
            .CountAsync(cancellationToken);
        var successfulSignIns = await context.AuditEvents.AsNoTracking()
            .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= thirtyDaysAgo &&
                auditEvent.Action == LocalAuditActions.AuthenticationSucceeded,
                cancellationToken);
        var personChanges = await context.PersonVersions.AsNoTracking()
            .CountAsync(version => version.AgencyId == actor.AgencyId &&
                version.ChangedAtUtc >= thirtyDaysAgo && version.ChangeKind != "TrackingBaseline",
                cancellationToken);
        var auditEventsToday = await context.AuditEvents.AsNoTracking()
            .CountAsync(auditEvent => auditEvent.AgencyId == actor.AgencyId &&
                auditEvent.OccurredAtUtc >= today,
                cancellationToken);
        var lastActivity = await context.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.AgencyId == actor.AgencyId)
            .MaxAsync(auditEvent => (DateTime?)auditEvent.OccurredAtUtc, cancellationToken);

        return new AdminOverviewDto(
            actor.AgencyId, agencyName, userCount, caseManagerCount, personCount,
            notesThisMonth, activeUsers, successfulSignIns, personChanges,
            auditEventsToday, lastActivity);
    }

    public async Task<AdminOperationsDto> GetOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var auditCount = await context.AuditEvents.AsNoTracking()
            .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
        var ediCount = await context.EdiGenerations.AsNoTracking()
            .LongCountAsync(candidate => candidate.AgencyId == actor.AgencyId, cancellationToken);
        var ediCharacters = await context.EdiGenerations.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .SumAsync(candidate => (long?)candidate.Content.Length, cancellationToken) ?? 0;
        var oldestAudit = await context.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .MinAsync(candidate => (DateTime?)candidate.OccurredAtUtc, cancellationToken);
        var oldestEdi = await context.EdiGenerations.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId)
            .MinAsync(candidate => (DateTime?)candidate.CreatedAtUtc, cancellationToken);

        return new AdminOperationsDto(
            DateTime.UtcNow,
            "Healthy",
            "PolicyOnly",
            OperationalPolicyDefaults.AuditRetentionDays,
            OperationalPolicyDefaults.EdiReplayRetentionDays,
            auditCount,
            ediCount,
            ediCharacters,
            oldestAudit,
            oldestEdi);
    }

    public async Task<AdminIncidentDashboardDto> GetIncidentsAsync(
        int days = 30,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (days is < 1 or > 90 || take is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(days));
        await using var context = contextFactory.CreateDbContext();
        var observedAt = DateTime.UtcNow;
        var start = observedAt.AddDays(-days);
        var incidents = await context.IncidentGroups.AsNoTracking()
            .Where(candidate => candidate.AgencyId == actor.AgencyId &&
                                candidate.Scope == IncidentScopes.Agency &&
                                candidate.LastSeenUtc >= start)
            .OrderByDescending(candidate => candidate.LastSeenUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Take(take)
            .Select(candidate => new IncidentGroupDto(
                candidate.Id,
                candidate.AgencyId,
                candidate.Scope,
                candidate.Source,
                candidate.Severity,
                candidate.Operation,
                candidate.FirstRelease,
                candidate.LastRelease,
                candidate.ExceptionFingerprint,
                candidate.Status,
                candidate.OccurrenceCount,
                candidate.FirstSeenUtc,
                candidate.LastSeenUtc,
                candidate.LastReference,
                candidate.LastActorRole))
            .ToListAsync(cancellationToken);
        return new AdminIncidentDashboardDto(
            observedAt,
            IncidentHealthScoring.Calculate(incidents, observedAt, days),
            incidents);
    }

    public async Task<IncidentGroupDto> UpdateIncidentStatusAsync(
        long incidentId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (status is not ("Open" or "Investigating" or "Resolved"))
            throw new ArgumentException("Status must be Open, Investigating, or Resolved.", nameof(status));
        await using var context = contextFactory.CreateDbContext();
        var incident = await context.IncidentGroups.SingleOrDefaultAsync(candidate =>
            candidate.Id == incidentId && candidate.AgencyId == actor.AgencyId &&
            candidate.Scope == IncidentScopes.Agency,
            cancellationToken) ?? throw new InvalidOperationException("This incident was not found in your agency.");
        incident.Status = status;
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.IncidentStatusUpdated,
            "IncidentGroup",
            metadataJson: JsonSerializer.Serialize(new { incidentId, status }));
        await context.SaveChangesAsync(cancellationToken);
        return new IncidentGroupDto(
            incident.Id, incident.AgencyId, incident.Scope, incident.Source, incident.Severity,
            incident.Operation, incident.FirstRelease, incident.LastRelease,
            incident.ExceptionFingerprint, incident.Status, incident.OccurrenceCount,
            incident.FirstSeenUtc, incident.LastSeenUtc, incident.LastReference,
            incident.LastActorRole);
    }

    public async Task<LegalHoldDto> PlaceLegalHoldAsync(
        PlaceLegalHoldRequest request, CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A reason is required to place a legal hold.", nameof(request));

        await using var context = contextFactory.CreateDbContext();
        var personExists = await context.People.AsNoTracking().AnyAsync(candidate =>
            candidate.Id == request.PersonId && candidate.AgencyId == actor.AgencyId,
            cancellationToken);
        if (!personExists)
            throw new InvalidOperationException("This consumer was not found in your agency.");

        var hold = new LegalHold
        {
            AgencyId = actor.AgencyId,
            PersonId = request.PersonId,
            Reason = request.Reason.Trim(),
            CaseReference = string.IsNullOrWhiteSpace(request.CaseReference) ? null : request.CaseReference.Trim(),
            IssuedBy = string.IsNullOrWhiteSpace(request.IssuedBy) ? null : request.IssuedBy.Trim(),
            EffectiveAtUtc = request.EffectiveAtUtc
        };
        hold.PlacedByUserId = actor.Id;
        context.LegalHolds.Add(hold);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.LegalHoldPlaced, "LegalHold",
            metadataJson: JsonSerializer.Serialize(new { personId = request.PersonId, reason = hold.Reason }));
        await context.SaveChangesAsync(cancellationToken);
        return ToLegalHoldDto(hold);
    }

    public async Task<LegalHoldDto> ReleaseLegalHoldAsync(
        int legalHoldId, string? releaseNote, CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var hold = await context.LegalHolds.SingleOrDefaultAsync(candidate =>
            candidate.Id == legalHoldId && candidate.AgencyId == actor.AgencyId,
            cancellationToken) ??
            throw new InvalidOperationException("This legal hold was not found in your agency.");
        if (hold.IsReleased)
            throw new InvalidOperationException("This legal hold has already been released.");

        hold.IsReleased = true;
        hold.ReleasedByUserId = actor.Id;
        hold.ReleasedAtUtc = DateTime.UtcNow;
        hold.ReleaseNote = string.IsNullOrWhiteSpace(releaseNote) ? null : releaseNote.Trim();
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.LegalHoldReleased, "LegalHold",
            metadataJson: JsonSerializer.Serialize(new { legalHoldId, personId = hold.PersonId }));
        await context.SaveChangesAsync(cancellationToken);
        return ToLegalHoldDto(hold);
    }

    public async Task<List<LegalHoldDto>> GetLegalHoldsAsync(
        int personId, CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        return await context.LegalHolds.AsNoTracking()
            .Where(hold => hold.PersonId == personId && hold.AgencyId == actor.AgencyId)
            .OrderByDescending(hold => hold.PlacedAtUtc)
            .Select(hold => new LegalHoldDto(
                hold.Id, hold.PersonId, hold.Reason, hold.CaseReference, hold.IssuedBy,
                hold.EffectiveAtUtc, hold.PlacedByUserId, hold.PlacedAtUtc,
                hold.IsReleased, hold.ReleasedByUserId, hold.ReleasedAtUtc, hold.ReleaseNote))
            .ToListAsync(cancellationToken);
    }

    private static LegalHoldDto ToLegalHoldDto(LegalHold hold) => new(
        hold.Id, hold.PersonId, hold.Reason, hold.CaseReference, hold.IssuedBy,
        hold.EffectiveAtUtc, hold.PlacedByUserId, hold.PlacedAtUtc,
        hold.IsReleased, hold.ReleasedByUserId, hold.ReleasedAtUtc, hold.ReleaseNote);

    /// <summary>
    /// Rule-3 deletion, in local Production: permanently deletes an ordinary consumer created
    /// inside <see cref="ConsumerDeletionRules.DeletionWindowDays"/> days.
    ///
    /// <para>
    /// Extends <see cref="DeleteTestConsumerAsync"/>'s transaction shape and child-record
    /// cascade, but gates on the creation-time window, the A1 billing-integrity check, and the
    /// A3 legal-hold registry instead of <c>IsTestData</c>. Unlike test-consumer deletion, this
    /// command deletes claim lines rather than refusing whenever one exists — A1 permits draft
    /// and synthetic billing inside the window, so claim lines belonging only to those must be
    /// removable along with everything else.
    /// </para>
    ///
    /// <para>
    /// The audit event is a tombstone: an itemized inventory by id, date, and type, captured
    /// before any delete, with no narrative, name, MaineCareId, birth date, or address. See
    /// HANDOFF_CLIENT_DELETION_POLICY.md's audit section for why the exclusion is load-bearing.
    /// </para>
    /// </summary>
    public async Task<ConsumerDeletionResultDto> DeleteConsumerInWindowAsync(
        int personId,
        int expectedRevision,
        string attestation,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (personId <= 0 || expectedRevision <= 0)
            throw new ArgumentException("Select a current consumer record and try again.");
        if (!ConsumerDeletionRules.HasValidConsumerAttestation(attestation))
            throw new ArgumentException("The required deletion affirmation was not supplied.", nameof(attestation));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to delete a consumer.", nameof(reason));

        await using var context = contextFactory.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var actorIsCurrentAdmin = await context.Users.AsNoTracking().AnyAsync(candidate =>
            candidate.Id == actor.Id && candidate.AgencyId == actor.AgencyId &&
            (candidate.Permissions & UserPermissions.Administration) != 0,
            cancellationToken);
        if (!actorIsCurrentAdmin)
            throw new UnauthorizedAccessException("Only a current Admin can delete a consumer.");

        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == personId && candidate.AgencyId == actor.AgencyId &&
            context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId),
            cancellationToken);
        if (person is null)
            throw new InvalidOperationException("This consumer was not found in your agency.");
        if (person.Revision != expectedRevision)
            throw new InvalidOperationException(
                "This consumer changed after you selected them. Refresh and review the current record before trying again.");
        if (!ConsumerDeletionRules.IsWithinDeletionWindow(person.CreatedAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException(ConsumerDeletionRules.OutsideWindowMessage);

        // A3: legal hold. Checked before any child row changes, and refused on anything but an
        // explicit Clear — Active, Unavailable, and any registry exception all fail closed.
        var holdStatus = await legalHoldRegistry.GetStatusAsync(actor.AgencyId, personId, cancellationToken);
        if (holdStatus != LegalHoldStatus.Clear)
        {
            throw new InvalidOperationException(holdStatus == LegalHoldStatus.Active
                ? ConsumerDeletionRules.LegalHoldActiveMessage
                : ConsumerDeletionRules.LegalHoldUnavailableMessage);
        }

        // A1: billing integrity. Draft and synthetic billing is deletable inside the window;
        // only billing that actually reached a payer blocks.
        var billingPeriodIds = await context.ClaimLines.AsNoTracking()
            .Where(claimLine => context.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
            .Select(claimLine => claimLine.BillingPeriodId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var billingFacts = new BillingIntegrityFacts(
            HasTransmittedBillingSubmissionEvent: billingPeriodIds.Count > 0 &&
                await context.BillingSubmissionEvents.AsNoTracking().AnyAsync(submissionEvent =>
                    billingPeriodIds.Contains(submissionEvent.BillingPeriodId) &&
                    !submissionEvent.IsSynthetic &&
                    submissionEvent.Stage >= BillingSubmissionStage.Transmitted,
                    cancellationToken),
            HasNonSyntheticRemittanceClaimOutcome: billingPeriodIds.Count > 0 &&
                await context.RemittanceClaimOutcomes.AsNoTracking().AnyAsync(outcome =>
                    outcome.BillingPeriodId != null &&
                    billingPeriodIds.Contains(outcome.BillingPeriodId.Value) &&
                    !outcome.IsSynthetic,
                    cancellationToken),
            HasSubmittedOrNonDraftBillingPeriod: billingPeriodIds.Count > 0 &&
                await context.BillingPeriods.AsNoTracking().AnyAsync(period =>
                    billingPeriodIds.Contains(period.Id) &&
                    (period.SubmittedAt != null || period.Status != BillingStatus.Draft),
                    cancellationToken));
        if (ConsumerDeletionRules.HasTransmittedBilling(billingFacts))
            throw new InvalidOperationException(ConsumerDeletionRules.TransmittedBillingMessage);

        // Itemized tombstone, captured before any delete. Ids, dates, and types only — never
        // narrative, name, MaineCareId, birth date, or address. This is the one remaining
        // evidence the record existed; see AUDIT_EVENTS.md's exclusion principle.
        var noteRows = await context.Notes.AsNoTracking()
            .Where(note => note.PersonId == personId)
            .Select(note => new { note.Id, note.EventDate, note.Status, note.Minutes, note.NoteType })
            .ToListAsync(cancellationToken);
        var noteInventory = noteRows.Select(note => new
        {
            note.Id, eventDate = note.EventDate, status = note.Status?.ToString(),
            note.Minutes, noteType = note.NoteType?.ToString()
        }).ToList();

        var claimLineRows = await context.ClaimLines.AsNoTracking()
            .Where(claimLine => context.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
            .Select(claimLine => new
            {
                claimLine.Id, claimLine.DateOfService, claimLine.ProcedureCode,
                claimLine.ProcedureModifier, claimLine.Units, claimLine.ChargeAmount,
                claimLine.BillingPeriodId
            })
            .ToListAsync(cancellationToken);

        var formRows = await context.Forms.AsNoTracking()
            .Where(form => form.PersonId == personId)
            .Select(form => new { form.Id, form.Type, form.DueDate })
            .ToListAsync(cancellationToken);
        var formInventory = formRows.Select(form => new
        { form.Id, type = form.Type.ToString(), dueDate = form.DueDate }).ToList();

        var reviewRows = await context.ReviewItems.AsNoTracking()
            .Where(review => review.PersonId == personId)
            .Select(review => new { review.Id, review.Category, review.RequestedDate })
            .ToListAsync(cancellationToken);
        var reviewInventory = reviewRows.Select(review => new
        { review.Id, category = review.Category.ToString(), requestedDate = review.RequestedDate }).ToList();

        var assessmentRows = await context.ComprehensiveAssessments.AsNoTracking()
            .Where(assessment => assessment.PersonId == personId)
            .Select(assessment => new { assessment.Id, assessment.Status, assessment.CreatedAt })
            .ToListAsync(cancellationToken);
        var assessmentInventory = assessmentRows.Select(assessment => new
        { assessment.Id, status = assessment.Status.ToString(), createdAt = assessment.CreatedAt }).ToList();

        var atRequestRows = await context.ATRequests.AsNoTracking()
            .Where(request => request.PersonId == personId)
            .Select(request => new { request.Id, request.Status, request.SubmittedDate })
            .ToListAsync(cancellationToken);
        var atRequestInventory = atRequestRows.Select(request => new
        { request.Id, status = request.Status.ToString(), submittedDate = request.SubmittedDate }).ToList();

        var contactRows = await context.PersonContacts.AsNoTracking()
            .Where(contact => contact.PersonId == personId)
            .Select(contact => new { contact.Id, contact.Kind })
            .ToListAsync(cancellationToken);
        var contactInventory = contactRows.Select(contact => new
        { contact.Id, kind = contact.Kind.ToString() }).ToList();

        var personVersionInventory = await context.PersonVersions.AsNoTracking()
            .Where(version => version.PersonId == personId)
            .Select(version => new { version.Id, version.ChangeKind, version.ChangedAtUtc })
            .ToListAsync(cancellationToken);

        // Cascade delete, in dependency order. ClaimLines before Notes: A1 permits draft and
        // synthetic claim lines inside the window, unlike test-consumer deletion, which never
        // has to delete one because it refuses whenever any claim line exists at all.
        var appointmentsDeleted = await context.Appointments
            .Where(appointment => context.ReviewItems.Any(review =>
                review.Id == appointment.ReviewItemId && review.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var reviewsDeleted = await context.ReviewItems
            .Where(review => review.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var contactsDeleted = await context.PersonContacts
            .Where(contact => contact.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var personProvidersDeleted = await context.PersonProviders
            .Where(link => link.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var documentArtifactsDeleted = await context.DocumentArtifacts
            .Where(artifact => artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId)
            .ExecuteDeleteAsync(cancellationToken);
        var formAttestationsDeleted = await context.FormAttestations
            .Where(formAttestation => context.Forms.Any(form =>
                form.Id == formAttestation.FormId && form.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var formsDeleted = await context.Forms
            .Where(form => form.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var atRequestItemsDeleted = await context.ATRequestItems
            .Where(item => context.ATRequests.Any(request =>
                request.Id == item.ATRequestId && request.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var atRequestsDeleted = await context.ATRequests
            .Where(request => request.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var assessmentsDeleted = await context.ComprehensiveAssessments
            .Where(assessment => assessment.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var claimLinesDeleted = await context.ClaimLines
            .Where(claimLine => context.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var notesDeleted = await context.Notes
            .Where(note => note.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);

        // PersonVersion is normally append-only. This is the one narrow exception, shared with
        // test-consumer deletion: the version ledger contains copies of the deleted record and
        // has a restrictive FK, while the independent AuditEvent ledger remains intact.
        var personVersionsDeleted = await context.PersonVersions
            .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
            .ExecuteDeleteAsync(cancellationToken);

        var peopleDeleted = await context.People
            .Where(candidate => candidate.Id == personId && candidate.Revision == expectedRevision &&
                candidate.AgencyId == actor.AgencyId &&
                context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
            .ExecuteDeleteAsync(cancellationToken);
        if (peopleDeleted != 1)
        {
            throw new InvalidOperationException(
                "This consumer changed while deletion was in progress. Refresh before trying again.");
        }

        var result = new ConsumerDeletionResultDto(
            personId, formsDeleted, notesDeleted, contactsDeleted, reviewsDeleted, appointmentsDeleted,
            assessmentsDeleted, atRequestsDeleted, atRequestItemsDeleted, personVersionsDeleted,
            personProvidersDeleted, formAttestationsDeleted, documentArtifactsDeleted, claimLinesDeleted);

        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.ConsumerDeletedInWindow,
            "Person",
            personId,
            JsonSerializer.Serialize(new
            {
                attestationVersion = ConsumerDeletionRules.ConsumerAttestation,
                reason,
                createdAtUtc = person.CreatedAtUtc,
                deletedAtUtc = DateTime.UtcNow,
                billingIntegrityCheck = billingFacts,
                counts = result,
                notes = noteInventory,
                claimLines = claimLineRows,
                forms = formInventory,
                reviews = reviewInventory,
                assessments = assessmentInventory,
                atRequests = atRequestInventory,
                contacts = contactInventory,
                personVersions = personVersionInventory
            }));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<byte[]> ExportAuditCsvAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        var start = fromUtc.ToUniversalTime();
        var end = toUtc.ToUniversalTime();
        reason = reason?.Trim() ?? string.Empty;
        if (end < start || (end - start).TotalDays > 366 || end > DateTime.UtcNow.AddMinutes(5) ||
            reason.Length is < 10 or > 250)
        {
            throw new ArgumentException(
                "Use a window no longer than one year ending no later than now and provide a 10-250 character reason.",
                nameof(reason));
        }

        await using var context = contextFactory.CreateDbContext();
        var rows = await (
            from auditEvent in context.AuditEvents.AsNoTracking()
            join user in context.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where auditEvent.AgencyId == actor.AgencyId &&
                  auditEvent.OccurredAtUtc >= start && auditEvent.OccurredAtUtc <= end
            orderby auditEvent.OccurredAtUtc, auditEvent.Id
            select new LocalAuditExportRow(
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
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.AuditExported,
            "AuditEvent",
            metadataJson: JsonSerializer.Serialize(new
            {
                fromUtc = start,
                toUtc = end,
                rowCount = rows.Count
            }));
        await context.SaveChangesAsync(cancellationToken);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(BuildAuditCsv(rows, reason, exportedAt));
    }
    public async Task<List<AdminPersonListItemDto>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        return await (
            from person in context.People.AsNoTracking()
            join user in context.Users.AsNoTracking() on person.UserId equals user.Id
            where person.AgencyId == actor.AgencyId && user.AgencyId == actor.AgencyId
            orderby person.LastName, person.FirstName, person.Id
            select new AdminPersonListItemDto(
                person.Id,
                ((person.LastName ?? string.Empty) + ", " + (person.FirstName ?? string.Empty)).Trim(' ', ','),
                person.Revision,
                user.Id,
                user.DisplayName,
                person.IsTestData,
                person.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<TestConsumerDeletionResultDto> DeleteTestConsumerAsync(
        int personId,
        int expectedRevision,
        string attestation,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (personId <= 0 || expectedRevision <= 0)
            throw new ArgumentException("Select a current consumer record and try again.");
        if (!TestDataDeletionRules.HasValidConsumerAttestation(attestation))
            throw new ArgumentException("The required test-data affirmation was not supplied.", nameof(attestation));

        await using var context = contextFactory.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var actorIsCurrentAdmin = await context.Users.AsNoTracking().AnyAsync(candidate =>
            candidate.Id == actor.Id && candidate.AgencyId == actor.AgencyId &&
            (candidate.Permissions & UserPermissions.Administration) != 0,
            cancellationToken);
        if (!actorIsCurrentAdmin)
            throw new UnauthorizedAccessException("Only a current Admin can delete test consumer data.");

        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == personId && candidate.AgencyId == actor.AgencyId &&
            context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId),
            cancellationToken);
        if (person is null)
            throw new InvalidOperationException("This consumer was not found in your agency.");
        if (!person.IsTestData)
            throw new InvalidOperationException(
                "This consumer was not marked as Test when created and cannot be deleted with the test-data tool.");
        if (person.Revision != expectedRevision)
            throw new InvalidOperationException(
                "This consumer changed after you selected them. Refresh the Admin dashboard and review the current record before trying again.");

        var claimLineCount = await context.ClaimLines.AsNoTracking().CountAsync(claimLine =>
            context.Notes.Any(note => note.Id == claimLine.NoteId && note.PersonId == personId),
            cancellationToken);
        if (claimLineCount > 0)
            throw new InvalidOperationException(TestDataDeletionRules.ConsumerHasClaimsMessage);

        var appointmentsDeleted = await context.Appointments
            .Where(appointment => context.ReviewItems.Any(review =>
                review.Id == appointment.ReviewItemId && review.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var reviewsDeleted = await context.ReviewItems
            .Where(review => review.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var contactsDeleted = await context.PersonContacts
            .Where(contact => contact.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var personProvidersDeleted = await context.PersonProviders
            .Where(link => link.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var documentArtifactsDeleted = await context.DocumentArtifacts
            .Where(artifact => artifact.PersonId == personId && artifact.AgencyId == actor.AgencyId)
            .ExecuteDeleteAsync(cancellationToken);
        var formAttestationsDeleted = await context.FormAttestations
            .Where(attestation => context.Forms.Any(form =>
                form.Id == attestation.FormId && form.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var formsDeleted = await context.Forms
            .Where(form => form.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var atRequestItemsDeleted = await context.ATRequestItems
            .Where(item => context.ATRequests.Any(request =>
                request.Id == item.ATRequestId && request.PersonId == personId))
            .ExecuteDeleteAsync(cancellationToken);
        var atRequestsDeleted = await context.ATRequests
            .Where(request => request.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var assessmentsDeleted = await context.ComprehensiveAssessments
            .Where(assessment => assessment.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
        var notesDeleted = await context.Notes
            .Where(note => note.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);

        // PersonVersion is normally append-only. This is the one narrow exception:
        // the version ledger contains copies of a consumer's test PHI and has a
        // restrictive FK, while the independent AuditEvent ledger remains intact.
        var personVersionsDeleted = await context.PersonVersions
            .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
            .ExecuteDeleteAsync(cancellationToken);

        var peopleDeleted = await context.People
            .Where(candidate => candidate.Id == personId && candidate.Revision == expectedRevision &&
                candidate.AgencyId == actor.AgencyId && candidate.IsTestData &&
                context.Users.Any(user => user.Id == candidate.UserId && user.AgencyId == actor.AgencyId))
            .ExecuteDeleteAsync(cancellationToken);
        if (peopleDeleted != 1)
            throw new InvalidOperationException(
                "This consumer changed while deletion was in progress. Refresh the Admin dashboard before trying again.");

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
            personProvidersDeleted,
            formAttestationsDeleted,
            documentArtifactsDeleted);
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.TestConsumerDeleted,
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
                formAttestationsDeleted = result.FormAttestationsDeleted,
                documentArtifactsDeleted = result.DocumentArtifactsDeleted,
                reviewsDeleted = result.ReviewsDeleted,
                appointmentsDeleted = result.AppointmentsDeleted,
                assessmentsDeleted = result.AssessmentsDeleted,
                atRequestsDeleted = result.AtRequestsDeleted,
                atRequestItemsDeleted = result.AtRequestItemsDeleted,
                personVersionsDeleted = result.PersonVersionsDeleted
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<List<AdminActivityDto>> GetActivityAsync(
        int days = 30,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        if (days is < 1 or > 366 || take is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(days), "Use 1-366 days and request 1-500 rows.");

        await using var context = contextFactory.CreateDbContext();
        var start = DateTime.UtcNow.AddDays(-days);
        return await (
            from auditEvent in context.AuditEvents.AsNoTracking()
            join user in context.Users.AsNoTracking() on auditEvent.ActorUserId equals user.Id into users
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
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PersonVersionDto>> GetPersonHistoryAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var person = await LoadPersonAsync(context, actor, personId, cancellationToken)
            ?? throw new InvalidOperationException("This Person was not found in your agency.");
        await PersonLifecycleLedger.EnsureBaselineAsync(context, person, cancellationToken);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.PersonHistoryViewed, "Person", personId);
        await context.SaveChangesAsync(cancellationToken);
        return await LoadHistoryAsync(context, actor, personId, cancellationToken);
    }

    public async Task<byte[]> ExportPersonHistoryPdfAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = CurrentAdmin();
        await using var context = contextFactory.CreateDbContext();
        var person = await LoadPersonAsync(context, actor, personId, cancellationToken)
            ?? throw new InvalidOperationException("This Person was not found in your agency.");
        await PersonLifecycleLedger.EnsureBaselineAsync(context, person, cancellationToken);
        LocalAuditTrail.Record(
            context, actor, LocalAuditActions.PersonHistoryPdfGenerated, "Person", personId);
        await context.SaveChangesAsync(cancellationToken);
        var history = await LoadHistoryAsync(context, actor, personId, cancellationToken);
        var agency = await context.Agencies.AsNoTracking().SingleAsync(
            candidate => candidate.Id == actor.AgencyId,
            cancellationToken);
        return pdfExporter.Generate(person, history, agency, actor, DateTime.UtcNow);
    }

    // Format and escaping are owned by Sati.Contracts AuditCsv, shared with the
    // API export. Do not hand-build this file here.
    private static string BuildAuditCsv(
        IReadOnlyList<LocalAuditExportRow> rows,
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

    private sealed record LocalAuditExportRow(
        Guid EventId,
        DateTime OccurredAtUtc,
        int ActorUserId,
        string ActorDisplayName,
        string Action,
        string ResourceType,
        string? ResourceId,
        string CorrelationId);
    private static Task<Person?> LoadPersonAsync(
        SatiContext context,
        User actor,
        int personId,
        CancellationToken cancellationToken) =>
        context.People.SingleOrDefaultAsync(person =>
            person.Id == personId && person.AgencyId == actor.AgencyId &&
            context.Users.Any(user => user.Id == person.UserId && user.AgencyId == actor.AgencyId),
            cancellationToken);

    private static async Task<List<PersonVersionDto>> LoadHistoryAsync(
        SatiContext context,
        User actor,
        int personId,
        CancellationToken cancellationToken) =>
        (await context.PersonVersions.AsNoTracking()
            .Where(version => version.PersonId == personId && version.AgencyId == actor.AgencyId)
            .OrderBy(version => version.Version)
            .ToListAsync(cancellationToken))
        .Select(PersonLifecycleLedger.ToDto)
        .ToList();

    private User CurrentAdmin()
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required.");
        if (!actor.HasAdminPermissions)
            throw new UnauthorizedAccessException("Only an Admin can open this dashboard.");
        return actor;
    }
}
