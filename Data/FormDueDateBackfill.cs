using Microsoft.EntityFrameworkCore;
using Sati;            // Person lives in namespace `Sati` (not Sati.Models)
using Sati.Models;     // Form, FormType, Settings
using System;          // Environment
using System.IO;       // Path, File
using System.Text;     // StringBuilder

namespace Sati.Data
{
    /// <summary>
    /// One-time maintenance tool that corrects stored Form.DueDate values from the
    /// OLD (cycleStart-anchored) calculator output to the NEW convention computed by
    /// FormDueDateCalculator. Two phases:
    ///
    ///   DryRunAsync  — computes every change and writes a report. Touches NOTHING.
    ///   CommitAsync  — recomputes and writes the corrected dates in one transaction.
    ///
    /// It ONLY ever writes Form.DueDate. IsCompliant and CompletedDate are never
    /// touched, so this is orthogonal to the duplicate/compliance cleanup that
    /// follows it. Triplicated forms are moved too — all copies land on the same
    /// correct date, staying duplicates (the dedup step handles those separately).
    ///
    /// Correctness hinges on bucketing each stored form into the cycle that PRODUCED
    /// it, derived from Person.EffectiveDate — never from the (wrong) stored date as
    /// a cycle signal. The new date then comes from the real FormDueDateCalculator,
    /// so there is exactly one source of date-math truth; this class adds only the
    /// bucketing, which is the one genuinely new piece of logic.
    /// </summary>
    public class FormDueDateBackfill
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;

        // The two-key commit latch. A dry run records here how many forms it WOULD
        // change; CommitAsync refuses to run unless the caller passes back that exact
        // number. You cannot supply a number you haven't seen, and you can't see it
        // without running the harmless dry run first — so an accidental commit is
        // structurally impossible, not merely discouraged. The latch lives on the
        // instance, and the service is registered transient, so a fresh instance
        // starts un-armed: a stale dry run from a previous open can't authorize a
        // commit.
        private bool _dryRunCompleted;
        private int _dryRunChangeCount;

        public FormDueDateBackfill(IDbContextFactory<SatiContext> contextFactory,
                                   ISettingsService settingsService)
        {
            _contextFactory = contextFactory;
            _settingsService = settingsService;
        }

        // A planned change for one form. Holds the live Form reference so CommitAsync
        // can mutate the tracked entity, plus the old/new dates captured by value so
        // the report is stable even after the form is mutated. CycleIndex is null
        // when the form could not be matched to any cycle — an anomaly we report and
        // refuse to touch rather than guess at.
        private sealed record PlanRow(
            Form Form, int PersonId, FormType Type,
            DateTime OldDueDate, DateTime NewDueDate, int? CycleIndex)
        {
            public bool Matched => CycleIndex is not null;
            public bool Changes => Matched && NewDueDate.Date != OldDueDate.Date;
        }

        public record BackfillReport(
            bool Committed, int PersonsProcessed, int PersonsSkipped,
            int FormsTotal, int FormsChanged, int FormsUnchanged,
            int FormsAnomalous, int DuplicateCells, string ReportFilePath);

        // ----------------------------------------------------------------------
        // Phase 1 — DRY RUN. Read-only. AsNoTracking() because we will not save,
        // so there's no reason to pay for change tracking on every loaded form.
        // ----------------------------------------------------------------------
        public async Task<BackfillReport> DryRunAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            var people = await context.People
                .AsNoTracking()
                .Include(p => p.Forms)
                .ToListAsync();

            var settings = await _settingsService.LoadAsync();
            var rows = ComputePlan(people, settings);
            var report = await WriteReportAsync(rows, people, committed: false);

            // Arm the latch with exactly what the report shows under "Forms changed".
            _dryRunChangeCount = report.FormsChanged;
            _dryRunCompleted = true;
            return report;
        }

        // ----------------------------------------------------------------------
        // Phase 2 — COMMIT. Tracked load (no AsNoTracking), so setting
        // form.DueDate marks the entity modified and SaveChangesAsync persists it.
        // Only matched, actually-changing rows are written.
        // ----------------------------------------------------------------------
        public async Task<BackfillReport> CommitAsync(int expectedChangeCount)
        {
            if (!_dryRunCompleted)
                throw new InvalidOperationException(
                    "Refusing to commit: run DryRunAsync first and read its report.");

            if (expectedChangeCount != _dryRunChangeCount)
                throw new InvalidOperationException(
                    $"Refusing to commit: you passed {expectedChangeCount}, but the dry run found "
                    + $"{_dryRunChangeCount} forms to change. Pass the exact number from the dry-run report.");

            await using var context = _contextFactory.CreateDbContext();
            var people = await context.People
                .Include(p => p.Forms)
                .ToListAsync();

            var settings = await _settingsService.LoadAsync();
            var rows = ComputePlan(people, settings);

            foreach (var row in rows)
                if (row.Changes)
                    row.Form.DueDate = row.NewDueDate;

            await context.SaveChangesAsync();
            return await WriteReportAsync(rows, people, committed: true);
        }

        // ----------------------------------------------------------------------
        // The plan: for every form, which cycle does it belong to, and what should
        // its date become. Pure computation over already-loaded entities — no DB,
        // no mutation — so DryRun and Commit share identical logic and can never
        // diverge in what they'd do.
        // ----------------------------------------------------------------------
        private static List<PlanRow> ComputePlan(List<Person> people, Settings settings)
        {
            var rows = new List<PlanRow>();

            foreach (var person in people)
            {
                if (person.EffectiveDate is null)
                    continue; // no anchor → no cycles derivable; counted as skipped later

                var eff = person.EffectiveDate.Value;

                foreach (var form in person.Forms)
                {
                    var impliedStart = ImpliedOldCycleStart(form.Type, form.DueDate);
                    var n = FindCycleIndex(eff, impliedStart);

                    if (n is null)
                    {
                        // Could not place this form on any anniversary of its person's
                        // effective date. Record it unchanged and flag it; we do not
                        // invent a cycle for a date that doesn't fit the pattern.
                        rows.Add(new PlanRow(form, person.Id, form.Type,
                                             form.DueDate, form.DueDate, null));
                        continue;
                    }

                    // Derive BOTH boundaries from effective.AddYears(...) — matching
                    // GetCurrentCycleBoundaries — rather than cycleStart.AddYears(1).
                    // AddYears is not associative around Feb 29, so deriving cycleEnd
                    // independently from effective keeps the backfill's boundaries
                    // identical to the ones the app uses at retrieval time.
                    var cs = eff.AddYears(n.Value);
                    var ce = eff.AddYears(n.Value + 1);
                    var newDue = FormDueDateCalculator.Compute(form.Type, cs, ce, settings);

                    rows.Add(new PlanRow(form, person.Id, form.Type,
                                         form.DueDate, newDue, n));
                }
            }

            return rows;
        }

        // Back out the cycleStart a stored date implies UNDER THE OLD RULE. This is
        // the only place the old offsets appear, and they are used solely to bucket
        // pre-fix data — never to compute new dates. (New dates come from Compute.)
        //   old annual forms were dated exactly cycleStart;
        //   old Q1R/Q2R/Q3R were cycleStart + 90/180/270;
        //   old Q4R was cycleEnd - 1 = cycleStart.AddYears(1).AddDays(-1).
        private static DateTime ImpliedOldCycleStart(FormType type, DateTime oldDueDate) => type switch
        {
            FormType.Q1R => oldDueDate.AddDays(-90),
            FormType.Q2R => oldDueDate.AddDays(-180),
            FormType.Q3R => oldDueDate.AddDays(-270),
            FormType.Q4R => oldDueDate.AddDays(1).AddYears(-1),
            _ => oldDueDate
        };

        // Find n such that effective.AddYears(n) lands on the implied cycleStart.
        // Compares on .Date so a stray time-of-day component can't cause a miss.
        // Range is generous; real data sits within a handful of cycles of admission.
        private static int? FindCycleIndex(DateTime effective, DateTime impliedCycleStart)
        {
            for (int n = -5; n <= 25; n++)
                if (effective.AddYears(n).Date == impliedCycleStart.Date)
                    return n;
            return null;
        }

        // ----------------------------------------------------------------------
        // Report writer. Produces a timestamped .txt on the Desktop and returns a
        // summary the UI can surface. Anomalies and the duplicate census sit at the
        // top so they're seen first; full per-form detail follows for study.
        // ----------------------------------------------------------------------
        private static async Task<BackfillReport> WriteReportAsync(
            List<PlanRow> rows, List<Person> people, bool committed)
        {
            var personsSkipped = people.Count(p => p.EffectiveDate is null);
            var personsProcessed = people.Count - personsSkipped;
            var changed = rows.Count(r => r.Changes);
            var unchanged = rows.Count(r => r.Matched && !r.Changes);
            var anomalous = rows.Count(r => !r.Matched);

            // Duplicate census: (person, cycle, type) cells holding more than one form.
            var dupCells = rows
                .Where(r => r.Matched)
                .GroupBy(r => (r.PersonId, r.CycleIndex, r.Type))
                .Where(g => g.Count() > 1)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  SATI FORM DUE-DATE BACKFILL — {(committed ? "COMMIT" : "DRY RUN")}");
            sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("================================================================");
            sb.AppendLine();
            sb.AppendLine($"  Persons processed ........ {personsProcessed}");
            sb.AppendLine($"  Persons skipped (no date)  {personsSkipped}");
            sb.AppendLine($"  Forms total .............. {rows.Count}");
            sb.AppendLine($"  Forms changed ............ {changed}");
            sb.AppendLine($"  Forms unchanged .......... {unchanged}");
            sb.AppendLine($"  Forms ANOMALOUS (skipped)  {anomalous}");
            sb.AppendLine($"  Duplicate cells .......... {dupCells.Count}");
            sb.AppendLine();

            if (anomalous > 0)
            {
                sb.AppendLine("---- ANOMALIES (could not be matched to a cycle; NOT modified) ----");
                foreach (var r in rows.Where(r => !r.Matched).OrderBy(r => r.PersonId))
                    sb.AppendLine($"   Person {r.PersonId}  {r.Type}  stored {r.OldDueDate:yyyy-MM-dd}");
                sb.AppendLine();
            }

            if (dupCells.Count > 0)
            {
                sb.AppendLine("---- DUPLICATE CENSUS (person / cycle / type : copies) ----");
                foreach (var g in dupCells.OrderBy(g => g.Key.PersonId).ThenBy(g => g.Key.CycleIndex))
                    sb.AppendLine($"   Person {g.Key.PersonId}  cycle {g.Key.CycleIndex}  {g.Key.Type} : {g.Count()} copies");
                sb.AppendLine();
            }

            sb.AppendLine("---- PER-FORM DETAIL ----");
            foreach (var personGroup in rows.GroupBy(r => r.PersonId).OrderBy(g => g.Key))
            {
                sb.AppendLine($"Person {personGroup.Key}:");
                foreach (var r in personGroup.OrderBy(r => r.CycleIndex).ThenBy(r => r.Type.ToString()))
                {
                    var change = r.Matched
                        ? (r.Changes ? $"{r.OldDueDate:yyyy-MM-dd} -> {r.NewDueDate:yyyy-MM-dd}"
                                     : $"{r.OldDueDate:yyyy-MM-dd}  (unchanged)")
                        : $"{r.OldDueDate:yyyy-MM-dd}  ** ANOMALY — not matched **";
                    sb.AppendLine($"   cycle {r.CycleIndex,2}  {r.Type,-24} {change}");
                }
                sb.AppendLine();
            }

            var fileName = $"Sati_Backfill_{(committed ? "Commit" : "DryRun")}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            await File.WriteAllTextAsync(path, sb.ToString());

            return new BackfillReport(
                committed, personsProcessed, personsSkipped, rows.Count,
                changed, unchanged, anomalous, dupCells.Count, path);
        }
    }
}