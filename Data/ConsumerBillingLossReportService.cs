using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public sealed class ConsumerBillingLossReportService : IConsumerBillingLossReportService
    {
        private static readonly NoteStatus[] WorkPerformedStatuses =
        [
            NoteStatus.Pending,
            NoteStatus.Logged,
            NoteStatus.HeldForCompliance,
            NoteStatus.Approved,
            NoteStatus.Returned,
            NoteStatus.ComplianceBlocked
        ];

        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public ConsumerBillingLossReportService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<ConsumerBillingLossReport> GetAsync(
            int userId,
            DateTime windowStart,
            DateTime windowEnd)
        {
            var start = windowStart.Date;
            var end = windowEnd.Date;
            if (end < start)
                throw new ArgumentException("The report window end must not precede its start.");

            await using var context = _contextFactory.CreateDbContext();

            // Keep this reporting read free of Person.Bio/Journal and Note.Narrative.
            // Those unbounded columns are not needed to classify days or total units.
            var people = await context.People
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .Select(p => new PersonReportItem(
                    p.Id,
                    ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim(),
                    p.EffectiveDate))
                .ToListAsync();

            var personIds = people.Select(p => p.Id).ToList();
            if (personIds.Count == 0)
                return new ConsumerBillingLossReport([], 0, 0, null);

            var forms = await context.Forms
                .AsNoTracking()
                .Where(f => personIds.Contains(f.PersonId)
                         && f.DueDate < end
                         && (f.CompletedDate == null || f.CompletedDate > start))
                .Select(f => new FormReportItem(
                    f.PersonId,
                    f.Type,
                    f.DueDate,
                    f.CompletedDate))
                .ToListAsync();

            var notes = await context.Notes
                .AsNoTracking()
                .Where(n => personIds.Contains(n.PersonId)
                         && n.EventDate.HasValue
                         && n.EventDate.Value >= start
                         && n.EventDate.Value < end.AddDays(1)
                         && n.Status.HasValue
                         && WorkPerformedStatuses.Contains(n.Status.Value))
                .Select(n => new NoteReportItem(
                    n.PersonId,
                    n.EventDate!.Value,
                    n.Minutes))
                .ToListAsync();

            var formsByPerson = forms
                .GroupBy(f => f.PersonId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var notesByPerson = notes
                .GroupBy(n => n.PersonId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var rows = new List<ConsumerBillingLossRow>(people.Count);
            foreach (var person in people)
            {
                var activeStart = person.EffectiveDate is DateTime effectiveDate
                    ? Max(start, effectiveDate.Date)
                    : start;

                var totalDays = activeStart <= end
                    ? (end - activeStart).Days + 1
                    : 0;

                var blockedDates = new HashSet<DateTime>();
                if (totalDays > 0 && formsByPerson.TryGetValue(person.Id, out var personForms))
                {
                    for (var date = activeStart; date <= end; date = date.AddDays(1))
                    {
                        if (personForms.Any(f => Person.IsBillingWindowBlocked(
                                f.Type, f.DueDate, f.CompletedDate, date)))
                        {
                            blockedDates.Add(date);
                        }
                    }
                }

                var billableUnits = 0;
                var nonBillableUnits = 0;
                if (notesByPerson.TryGetValue(person.Id, out var personNotes))
                {
                    foreach (var note in personNotes.Where(n => n.EventDate.Date >= activeStart))
                    {
                        var units = Note.CalculateUnits(note.Minutes) ?? 0;
                        if (blockedDates.Contains(note.EventDate.Date))
                            nonBillableUnits += units;
                        else
                            billableUnits += units;
                    }
                }

                var totalUnits = billableUnits + nonBillableUnits;
                decimal? lostPercentage = totalUnits > 0
                    ? Math.Round(100m * nonBillableUnits / totalUnits, 1)
                    : null;

                rows.Add(new ConsumerBillingLossRow(
                    person.Id,
                    string.IsNullOrWhiteSpace(person.Name) ? $"Consumer #{person.Id}" : person.Name,
                    totalDays - blockedDates.Count,
                    blockedDates.Count,
                    billableUnits,
                    nonBillableUnits,
                    lostPercentage));
            }

            var totalBillableUnits = rows.Sum(r => r.BillableUnits);
            var totalNonBillableUnits = rows.Sum(r => r.NonBillableUnits);
            var totalWorkUnits = totalBillableUnits + totalNonBillableUnits;

            return new ConsumerBillingLossReport(
                rows,
                totalBillableUnits,
                totalNonBillableUnits,
                totalWorkUnits > 0
                    ? Math.Round(100m * totalNonBillableUnits / totalWorkUnits, 1)
                    : null);
        }

        private static DateTime Max(DateTime left, DateTime right) =>
            left >= right ? left : right;

        private sealed record PersonReportItem(int Id, string Name, DateTime? EffectiveDate);
        private sealed record FormReportItem(
            int PersonId,
            FormType Type,
            DateTime DueDate,
            DateTime? CompletedDate);
        private sealed record NoteReportItem(int PersonId, DateTime EventDate, int? Minutes);
    }
}
