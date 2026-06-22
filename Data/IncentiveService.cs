using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Helpers;

namespace Sati.Data
{
    public class IncentiveService : IIncentiveService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;

        public IncentiveService(IDbContextFactory<SatiContext> context, ISettingsService settingsService)
        {
            _contextFactory = context;
            _settingsService = settingsService;
        }

        public async Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year)
        {

            await using var context = _contextFactory.CreateDbContext();
            var settings = await _settingsService.LoadAsync();

            var incentive = await context.Incentives
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.UserId == userId &&
                                          i.Month == month &&
                                          i.Year == year);

            if (incentive is null)
            {
                var daysScheduled = CalculateDaysScheduled(month, year, settings);
                incentive = new Incentive
                {
                    UserId = userId,
                    Month = month,
                    Year = year,
                    DaysScheduled = daysScheduled,
                    BaseIncentive = settings.BaseIncentive,
                    PerUnitIncentive = settings.PerUnitIncentive,
                    UnitsPerDay = settings.ProductivityThreshold
                };
                context.Incentives.Add(incentive);
                await context.SaveChangesAsync();
                return (incentive, true);
            }

            // Sync DaysScheduled and UnitsPerDay on every load so stale values
            // (e.g. written by the old scheduler code) self-correct at startup.
            var correctDays = CalculateDaysScheduled(month, year, settings);
            var needsUpdate = false;

            if (incentive.UnitsPerDay == 0)
            {
                incentive.UnitsPerDay = settings.ProductivityThreshold;
                needsUpdate = true;
            }

            if (incentive.DaysScheduled != correctDays)
            {
                incentive.DaysScheduled = correctDays;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                context.Incentives.Update(incentive);
                await context.SaveChangesAsync();
            }

            return (incentive, false);
        }

        public async Task SaveAsync(Incentive incentive)
        {
            await using var context = _contextFactory.CreateDbContext();

            context.Incentives.Update(incentive);
            await context.SaveChangesAsync();
        }

        private int CalculateDaysScheduled(int month, int year, Settings settings, DateTime? cap = null)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var count = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                if (cap.HasValue && date > cap.Value) break;
                var dow = date.DayOfWeek;

                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) continue;
                if (WorkdayHelper.IsAlwaysExcludedWorkday(date, settings)) continue;

                count++;
            }

            return count;
        }

        public async Task<int> GetDaysWorkedToDateAsync(int month, int year, DateTime? asOf = null)
        {
            var settings = await _settingsService.LoadAsync();
            return CalculateDaysScheduled(month, year, settings, cap: asOf ?? DateTime.Today);
        }

        public async Task<int> GetRemainingEligibleDaysAsync(int month, int year, HashSet<DateTime> daysAlreadyWorked, HashSet<DateTime> exemptDates)
        {
            var settings = await _settingsService.LoadAsync();
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var count = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var dow = date.DayOfWeek;

                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) continue;
                if (WorkdayHelper.IsAlwaysExcludedWorkday(date, settings)) continue;
                if (exemptDates.Contains(date)) continue;
                if (daysAlreadyWorked.Contains(date)) continue;

                count++;
            }

            return count;
        }

        public async Task<List<Incentive>> GetHistoryAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();

            return await context.Incentives
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .OrderBy(i => i.Year)
                .ThenBy(i => i.Month)
                .ToListAsync();
        }
    }
}
