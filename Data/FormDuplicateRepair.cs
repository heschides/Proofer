using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Collapses duplicate <see cref="Form"/> rows — same person, same type, same due
/// date — down to one, merging the state the copies carry.
///
/// WHY THEY EXIST. Before 57af6fa, PersonService.GetAllPeopleAsync ran
/// EnsureCurrentCycleForms + SaveChangesAsync on every caseload load, each call on
/// its own DbContext. Person.AddMissingFormsForCycle decides whether to insert by
/// reading the person's own Forms collection, which is a check-then-insert with
/// nothing holding the gap, and dbo.Forms had no unique constraint. Startup issued
/// those loads concurrently, so three loaders each passed the check and each
/// inserted a full set. 57af6fa closed the mechanism; the rows it had already
/// written stayed.
///
/// WHY THEY MATTER. Duplicates are invisible until one ages past its due date.
/// Person.GetCurrentCycleForm returns a single row — OrderByDescending(DueDate)
/// .FirstOrDefault() over a tie — so the checkbox, the matrix and the task board
/// read one copy, while Person.EvaluateComplianceGate projects every row in
/// Person.Forms and sees the copies nobody can reach. A completed form therefore
/// keeps blocking billing, and completing it again rewrites the reachable copy and
/// changes nothing the gate reads.
///
/// WHY THIS RUNS WITHOUT A CONFIRMATION LATCH, unlike FormBulkCompletion and
/// FormDueDateBackfill. Those two INVENT data — a completion date the record never
/// held — which is why they demand a dry run and a typed-back count. This one
/// invents nothing. It merges the union of what the copies already assert and
/// deletes rows that assert nothing the survivor does not. A group where the copies
/// genuinely disagree is not merged at all; see IsConflicted. That difference is
/// what makes it safe to run unattended, and it is the only reason it is.
///
/// ORDERING. This must run BEFORE the migration that adds
/// IX_Forms_PersonId_Type_DueDate, because that index cannot be created while
/// duplicates exist. LocalDatabaseUpdater calls it between the pre-migration backup
/// and MigrateAsync for exactly that reason. It is idempotent and costs one grouped
/// read once the data is clean.
/// </summary>
public static class FormDuplicateRepair
{
    // No one is signed in when this runs at startup — it happens before the login
    // window — so the audit events carry the same "no actor" sentinel
    // PersonLifecycleLedger already uses. AdminService left-joins the actor, so an
    // unmatched id renders as "User 0" rather than dropping the row.
    public const int SystemActorUserId = 0;

    /// <summary>One duplicated (PersonId, Type, DueDate) group.</summary>
    public sealed record DuplicateGroup(
        int PersonId,
        FormType Type,
        DateTime DueDate,
        IReadOnlyList<int> FormIds,
        IReadOnlyList<DateTime> DistinctCompletedDates)
    {
        /// <summary>
        /// The copies hold two or more DIFFERENT completion dates, so merging would
        /// have to pick one, and CompletedDate is date-keyed into
        /// BillingComplianceGate.IsBillingWindowBlocked — the choice decides whether
        /// past service dates were billable. That is a billing decision, so these
        /// groups are reported and left exactly as they are.
        ///
        /// Note what is NOT a conflict: some copies holding a date and the rest
        /// holding none. That is the ordinary shape — one copy was edited and the
        /// others are untouched generation defaults — and the union has exactly one
        /// completion fact in it, so there is nothing to choose.
        /// </summary>
        public bool IsConflicted => DistinctCompletedDates.Count > 1;

        public int SurplusRows => FormIds.Count - 1;
    }

    public sealed record RepairPlan(
        IReadOnlyList<DuplicateGroup> Groups)
    {
        public IReadOnlyList<DuplicateGroup> Mergeable =>
            Groups.Where(group => !group.IsConflicted).ToList();

        public IReadOnlyList<DuplicateGroup> Conflicted =>
            Groups.Where(group => group.IsConflicted).ToList();

        public int RowsToRemove => Mergeable.Sum(group => group.SurplusRows);

        public bool HasWork => Mergeable.Count > 0;

        /// <summary>
        /// True when duplicates would survive this repair. The unique-index migration
        /// cannot be applied while this is true.
        /// </summary>
        public bool LeavesDuplicates => Conflicted.Count > 0;
    }

    public sealed record RepairResult(
        int GroupsMerged,
        int RowsRemoved,
        int GroupsLeftConflicted,
        IReadOnlyList<DuplicateGroup> Conflicts);

    /// <summary>
    /// Read-only. Returns every duplicated group and how it classifies. Writes
    /// nothing, so it is safe to call for reporting alone.
    /// </summary>
    public static async Task<RepairPlan> PlanAsync(
        SatiContext context, CancellationToken cancellationToken = default)
    {
        var forms = await context.Forms.AsNoTracking().ToListAsync(cancellationToken);
        return Plan(forms);
    }

    /// <summary>
    /// Merges every non-conflicted duplicate group and deletes the surplus rows, in
    /// one transaction, recording an audit event per removed row. Conflicted groups
    /// are left untouched and returned so the caller can surface them.
    ///
    /// Idempotent: a second call against clean data finds no groups and writes
    /// nothing.
    /// </summary>
    public static async Task<RepairResult> ApplyAsync(
        SatiContext context, CancellationToken cancellationToken = default)
    {
        var forms = await context.Forms.ToListAsync(cancellationToken);
        var plan = Plan(forms);

        if (!plan.HasWork)
        {
            return new RepairResult(0, 0, plan.Conflicted.Count, plan.Conflicted);
        }

        // AgencyId is needed for the audit event and lives on the Person, not the
        // Form. One lookup keyed by person, rather than a join per removed row.
        var personIds = plan.Mergeable.Select(group => group.PersonId).Distinct().ToList();
        var agencyByPerson = await context.People
            .Where(person => personIds.Contains(person.Id))
            .Select(person => new { person.Id, person.AgencyId })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.AgencyId, cancellationToken);

        var byId = forms.ToDictionary(form => form.Id);
        var rowsRemoved = 0;

        foreach (var group in plan.Mergeable)
        {
            var copies = group.FormIds.Select(id => byId[id]).ToList();
            var survivor = ChooseSurvivor(copies);

            // Merge the union onto the survivor. Choosing the survivor by how much
            // state it already carries means the forbidden "compliant with no date"
            // shape is never CONSTRUCTED here — at most it is preserved on a row that
            // already held it, which is the documented generation exception in
            // Form.cs and a separate defect from this one.
            if (group.DistinctCompletedDates.Count == 1 &&
                survivor.CompletedDate?.Date != group.DistinctCompletedDates[0])
            {
                survivor.MarkComplete(group.DistinctCompletedDates[0]);
            }

            var earliestOpened = copies
                .Where(copy => copy.OpenedDate.HasValue)
                .Select(copy => copy.OpenedDate!.Value)
                .DefaultIfEmpty()
                .Min();
            if (earliestOpened != default &&
                (survivor.OpenedDate is null || earliestOpened < survivor.OpenedDate))
            {
                survivor.OpenedDate = earliestOpened;
            }

            foreach (var duplicate in copies.Where(copy => copy.Id != survivor.Id))
            {
                context.Forms.Remove(duplicate);
                rowsRemoved++;

                context.AuditEvents.Add(new AuditEvent
                {
                    // Person.AgencyId is nullable for records that predate agency
                    // scoping; 0 reads as "unattributed" the same way the actor does.
                    AgencyId = agencyByPerson.TryGetValue(group.PersonId, out var agencyId)
                        ? agencyId ?? 0
                        : 0,
                    ActorUserId = SystemActorUserId,
                    Action = LocalAuditActions.FormDuplicateRemoved,
                    ResourceType = "Form",
                    ResourceId = duplicate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CorrelationId = $"desktop-form-dedup-{Guid.NewGuid():N}",
                    MetadataJson = DescribeRemoval(group, duplicate, survivor)
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new RepairResult(
            plan.Mergeable.Count, rowsRemoved, plan.Conflicted.Count, plan.Conflicted);
    }

    /// <summary>
    /// The copy that already carries the most state, so merging never has to
    /// manufacture a state no row held: a copy with a completion date first, then one
    /// asserting compliance, then the lowest Id for a stable, repeatable answer.
    /// </summary>
    private static Form ChooseSurvivor(IReadOnlyList<Form> copies) =>
        copies
            .OrderByDescending(copy => copy.CompletedDate.HasValue)
            .ThenByDescending(copy => copy.IsCompliant)
            .ThenBy(copy => copy.Id)
            .First();

    /// <summary>
    /// The classifier, as a pure function over forms, so the merge rules can be
    /// tested without a database.
    /// </summary>
    public static RepairPlan Plan(IReadOnlyList<Form> forms)
    {
        var groups = forms
            .GroupBy(form => (form.PersonId, form.Type, DueDate: form.DueDate.Date))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.Key.PersonId,
                group.Key.Type,
                group.Key.DueDate,
                group.Select(form => form.Id).OrderBy(id => id).ToList(),
                group
                    .Where(form => form.CompletedDate.HasValue)
                    .Select(form => form.CompletedDate!.Value.Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList()))
            .OrderBy(group => group.PersonId)
            .ThenBy(group => group.DueDate)
            .ThenBy(group => group.Type)
            .ToList();

        return new RepairPlan(groups);
    }

    private static string DescribeRemoval(DuplicateGroup group, Form removed, Form survivor) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            reason = "duplicate-compliance-form",
            personId = group.PersonId,
            type = group.Type.ToString(),
            dueDate = group.DueDate.ToString("yyyy-MM-dd"),
            removedFormId = removed.Id,
            removedCompletedDate = removed.CompletedDate?.ToString("yyyy-MM-dd"),
            removedIsCompliant = removed.IsCompliant,
            survivingFormId = survivor.Id,
            survivingCompletedDate = survivor.CompletedDate?.ToString("yyyy-MM-dd")
        });
}
