using Microsoft.EntityFrameworkCore;
using Sati;            // Person
using Sati.Models;     // Form
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Sati.Data
{
    /// <summary>
    /// TEMPORARY one-time maintenance. Marks every form whose DueDate is on or before
    /// a cutoff, and which has no completion date, as complete using one explicitly
    /// captured completion date for the batch.
    ///
    /// Writes compliance state through the append-only attestation ledger, so the
    /// IsCompliant/CompletedDate invariant and its provenance are preserved. Only touches forms that are
    /// NOT already compliant, so existing recorded completions are never overwritten.
    ///
    /// This includes legacy rows that say IsCompliant=true but have no CompletedDate;
    /// those rows cannot support historical billing-window evaluation until reconciled.
    /// Existing recorded completion dates are never overwritten.
    ///
    /// Two-phase with the same latch as FormDueDateBackfill: DryRunAsync reports what
    /// it would mark and arms the latch with the count AND the cutoff; CommitAsync
    /// refuses unless a dry run ran this session and both the count and cutoff match.
    /// Delete this class with the rest of the migration scaffolding when done.
    /// </summary>
    public class FormBulkCompletion
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        // Latch — see FormDueDateBackfill for the rationale. Here it pins BOTH the
        // count, cutoff, and completion date, so the commit cannot silently change
        // any of the facts that were reviewed in the dry run.
        private bool _dryRunCompleted;
        private int _dryRunCount;
        private DateTime _dryRunCutoff;
        private DateTime _dryRunCompletionDate;

        public FormBulkCompletion(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public record BulkCompletionReport(
            bool Committed, DateTime Cutoff, DateTime CompletionDate, int FormsMarked,
            int AlreadyCompleted, int LegacyCompliantMissingDate,
            string ReportFilePath);

        public async Task<BulkCompletionReport> DryRunAsync(
            DateTime cutoffInclusive,
            DateTime completionDate)
        {
            await using var context = _contextFactory.CreateDbContext();
            var forms = await context.Forms.AsNoTracking()
                .Include(f => f.Person)
                .ToListAsync();

            ValidateCompletionDate(forms, cutoffInclusive, completionDate);
            var report = WriteReport(forms, cutoffInclusive, completionDate, committed: false);

            _dryRunCount = report.FormsMarked;
            _dryRunCutoff = cutoffInclusive.Date;
            _dryRunCompletionDate = completionDate.Date;
            _dryRunCompleted = true;
            return report;
        }

        public async Task<BulkCompletionReport> CommitAsync(
            DateTime cutoffInclusive,
            DateTime completionDate,
            int expectedCount)
        {
            if (!_dryRunCompleted)
                throw new InvalidOperationException(
                    "Refusing to commit: run DryRunAsync first and read its report.");

            if (cutoffInclusive.Date != _dryRunCutoff)
                throw new InvalidOperationException(
                    $"Refusing to commit: cutoff {cutoffInclusive:yyyy-MM-dd} does not match the "
                    + $"dry run's {_dryRunCutoff:yyyy-MM-dd}.");

            if (completionDate.Date != _dryRunCompletionDate)
                throw new InvalidOperationException(
                    $"Refusing to commit: completion date {completionDate:yyyy-MM-dd} does not match the "
                    + $"dry run's {_dryRunCompletionDate:yyyy-MM-dd}.");

            if (expectedCount != _dryRunCount)
                throw new InvalidOperationException(
                    $"Refusing to commit: you passed {expectedCount}, but the dry run found "
                    + $"{_dryRunCount} forms to mark. Pass the exact number from the report.");

            await using var context = _contextFactory.CreateDbContext();
            var forms = await context.Forms
                .Include(f => f.Person)
                .ToListAsync();
            ValidateCompletionDate(forms, cutoffInclusive, completionDate);

            foreach (var form in Eligible(forms, cutoffInclusive))
            {
                var attestation = FormAttestation.Attested(
                    completionDate,
                    Sati.Contracts.V1.AttestationActorKind.System,
                    actorUserId: null,
                    recordedAtUtc: DateTime.UtcNow,
                    prerequisiteStateJson: Sati.Contracts.V1.FormAttestationRules.NoPrerequisitesStateJson,
                    reason: "bulk completion reconciliation");
                form.Attest(attestation);
                context.AuditEvents.Add(new AuditEvent
                {
                    AgencyId = form.Person?.AgencyId ?? 0,
                    ActorUserId = -1,
                    Action = LocalAuditActions.FormAttested,
                    ResourceType = "Form",
                    ResourceId = form.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CorrelationId = $"desktop-form-bulk-{Guid.NewGuid():N}",
                    MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        formType = form.Type.ToString(),
                        completedOn = completionDate.Date,
                        actorKind = "System"
                    })
                });
            }

            await context.SaveChangesAsync();
            return WriteReport(forms, cutoffInclusive, completionDate, committed: true);
        }

        // The selection rule, in one place so dry run and commit can't diverge:
        // due on or before the cutoff, and missing its historical completion date.
        private static IEnumerable<Form> Eligible(IEnumerable<Form> forms, DateTime cutoffInclusive)
            => forms.Where(f => f.DueDate.Date <= cutoffInclusive.Date
                             && f.CompletedDate is null);

        private static void ValidateCompletionDate(
            IEnumerable<Form> forms,
            DateTime cutoffInclusive,
            DateTime completionDate)
        {
            foreach (var form in Eligible(forms, cutoffInclusive))
            {
                var effectiveDate = form.Person?.EffectiveDate
                    ?? throw new InvalidOperationException(
                        $"Form {form.Id} has no effective date and cannot be attested.");
                var cycle = Sati.Contracts.V1.FormAttestationRules.ResolveCycle(
                    effectiveDate, form.DueDate)
                    ?? throw new InvalidOperationException(
                        $"Form {form.Id} is not attached to a valid compliance cycle.");
                var decision = Sati.Contracts.V1.FormAttestationRules.Evaluate(
                    form.Type.ToString(), completionDate, cycle.CycleStart, DateTime.Today,
                    Sati.Contracts.V1.AttestationActorKind.System, []);
                if (!decision.Accepted)
                {
                    throw new InvalidOperationException(
                        $"Form {form.Id} cannot use that completion date: {decision.DateError}");
                }
            }
        }

        private static BulkCompletionReport WriteReport(
            List<Form> forms, DateTime cutoffInclusive, DateTime completionDate, bool committed)
        {
            var eligible = Eligible(forms, cutoffInclusive).ToList();
            var alreadyCompleted = forms.Count(f =>
                f.DueDate.Date <= cutoffInclusive.Date && f.CompletedDate.HasValue);
            // Always zero since AddDerivedFormCompliance: compliance IS the completion
            // date, so a row without one cannot claim to be compliant. Kept so the
            // report shape does not change mid-migration; drop it with this class.
            const int legacyCompliantMissingDate = 0;

            var sb = new StringBuilder();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  SATI BULK FORM COMPLETION — {(committed ? "COMMIT" : "DRY RUN")}");
            sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Cutoff (inclusive): {cutoffInclusive:yyyy-MM-dd}");
            sb.AppendLine($"  Completion date: {completionDate:yyyy-MM-dd}");
            sb.AppendLine("================================================================");
            sb.AppendLine();
            sb.AppendLine($"  Forms marked complete .......... {eligible.Count}");
            sb.AppendLine($"  Already completed (untouched) .. {alreadyCompleted}");
            sb.AppendLine($"  Legacy compliant/missing date .. {legacyCompliantMissingDate}");
            sb.AppendLine();
            sb.AppendLine("---- FORMS MARKED COMPLETE ----");
            foreach (var f in eligible
                         .OrderBy(f => f.PersonId).ThenBy(f => f.DueDate))
            {
                var who = f.Person?.FullName ?? $"Person {f.PersonId}";
                sb.AppendLine($"   {who,-28} {Person.FormDisplayName(f.Type),-12} due {f.DueDate:yyyy-MM-dd}");
            }
            sb.AppendLine();

            var fileName = $"Sati_BulkComplete_{(committed ? "Commit" : "DryRun")}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            File.WriteAllText(path, sb.ToString());

            return new BulkCompletionReport(
                committed, cutoffInclusive.Date, completionDate.Date, eligible.Count, alreadyCompleted,
                legacyCompliantMissingDate, path);
        }
    }
}
