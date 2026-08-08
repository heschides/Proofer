using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// Single source of truth for form due-date math. Each Form represents
    /// the document or review FOR its cycle — not prep work for the next cycle.
    ///
    /// Two families, anchored to OPPOSITE ends of the same cycle:
    ///
    ///   - Quarterly reviews count FORWARD from cycleStart. Q1R/Q2R/Q3R are
    ///     fixed +90/+180/+270 offsets. Q4R is the exception: it counts
    ///     BACKWARD from cycleEnd by Settings.Q4RDaysBeforeAnniversary, so it
    ///     lands just inside the half-open [cycleStart, cycleEnd) range that
    ///     cycle queries use, and stays leap-year correct.
    ///
    ///   - Annual documents (PCP, ComprehensiveAssessment, Reclassification,
    ///     SafetyPlan, PrivacyPractices, all Releases) count BACKWARD from
    ///     cycleEnd by their own Settings.*DaysBeforeAnniversary offset. A
    ///     form set to 0 is due ON the anniversary (cycleEnd); Comp is 60
    ///     days earlier, Reclassification 30, the rest currently 0. Each form
    ///     reads its OWN setting — nothing is hardcoded — so a different
    ///     agency can retune any deadline from Settings without code changes.
    ///
    /// Prep deadlines (open dates, late tolerance) are not due dates. They
    /// live on Settings (*OpenDaysBefore, *DaysAfterDue) and are derived
    /// relative to DueDate at display time by UpcomingEventService.
    /// </summary>
    public static class FormDueDateCalculator
    {
        public static DateTime Compute(FormType type, DateTime cycleStart, DateTime cycleEnd, Settings settings)
        {
            return type switch
            {
                // Quarterly reviews — count forward from cycleStart, except Q4R
                FormType.Q1R => cycleStart.AddDays(90),
                FormType.Q2R => cycleStart.AddDays(180),
                FormType.Q3R => cycleStart.AddDays(270),
                FormType.Q4R => cycleEnd.AddDays(-settings.Q4RDaysBeforeAnniversary),

                // Annual documents — count backward from cycleEnd by each
                // form's own anniversary offset (0 = due on the anniversary)
                FormType.PCP => cycleEnd.AddDays(-settings.PcpDaysBeforeAnniversary),
                FormType.ComprehensiveAssessment => cycleEnd.AddDays(-settings.CompAssessmentDaysBeforeAnniversary),
                FormType.Reclassification => cycleEnd.AddDays(-settings.ReclassificationDaysBeforeAnniversary),
                FormType.SafetyPlan => cycleEnd.AddDays(-settings.SafetyPlanDaysBeforeAnniversary),
                FormType.PrivacyPractices => cycleEnd.AddDays(-settings.PrivacyPracticesDaysBeforeAnniversary),
                FormType.Release_Agency => cycleEnd.AddDays(-settings.ReleaseAgencyDaysBeforeAnniversary),
                FormType.Release_DHHS => cycleEnd.AddDays(-settings.ReleaseDhhsDaysBeforeAnniversary),
                FormType.Release_Medical => cycleEnd.AddDays(-settings.ReleaseMedicalDaysBeforeAnniversary),

                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unhandled FormType in FormDueDateCalculator.")
            };
        }
    }
}
