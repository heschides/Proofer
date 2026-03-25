using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class IncentiveService : IIncentiveService
    {
        private readonly SatiContext _context;
        private readonly ISettingsService _settingsService;

        public IncentiveService(SatiContext context, ISettingsService settingsService)
        {
            _context = context;
            _settingsService = settingsService;
        }

        public async Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year)
        {
            var incentive = await _context.Incentives
                .FirstOrDefaultAsync(i => i.UserId == userId &&
                                         i.Month == month &&
                                         i.Year == year);

            if (incentive is null)
            {
                var settings = await _settingsService.LoadAsync();
                var daysScheduled = CalculateDaysScheduled(month, year, settings);

                incentive = new Incentive
                {
                    UserId = userId,
                    Month = month,
                    Year = year,
                    DaysScheduled = daysScheduled,
                    BaseIncentive = settings.BaseIncentive,
                    PerUnitIncentive = settings.PerUnitIncentive
                };

                _context.Incentives.Add(incentive);
                await _context.SaveChangesAsync();
                return (incentive, false);
            
            }

            return (incentive, false);
        }

        public async Task SaveAsync(Incentive incentive)
        {
            _context.Incentives.Update(incentive);
            await _context.SaveChangesAsync();
        }

        private int CalculateDaysScheduled(int month, int year, Settings settings)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var count = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var dow = date.DayOfWeek;

                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) continue;
                if (settings.ExcludeMonday && dow == DayOfWeek.Monday) continue;
                if (settings.ExcludeTuesday && dow == DayOfWeek.Tuesday) continue;
                if (settings.ExcludeWednesday && dow == DayOfWeek.Wednesday) continue;
                if (settings.ExcludeThursday && dow == DayOfWeek.Thursday) continue;
                if (settings.ExcludeFriday && dow == DayOfWeek.Friday) continue;
                if (IsExcludedHoliday(date, settings)) continue;

                count++;
            }

            return count;
        }

        private bool IsExcludedHoliday(DateTime date, Settings settings)
        {
            var m = date.Month;
            var d = date.Day;
            var dow = date.DayOfWeek;

            if (settings.ExcludeNewYearsDay && m == 1 && d == 1) return true;
            if (settings.ExcludeMLKDay && m == 1 && dow == DayOfWeek.Monday && IsNthWeekday(date, 3)) return true;
            if (settings.ExcludePresidentsDay && m == 2 && dow == DayOfWeek.Monday && IsNthWeekday(date, 3)) return true;
            if (settings.ExcludeMemorialDay && m == 5 && dow == DayOfWeek.Monday && IsLastMonday(date)) return true;
            if (settings.ExcludeJuneteenth && m == 6 && d == 19) return true;
            if (settings.ExcludeIndependenceDay && m == 7 && d == 4) return true;
            if (settings.ExcludeLaborDay && m == 9 && dow == DayOfWeek.Monday && IsNthWeekday(date, 1)) return true;
            if (settings.ExcludeIndigenousPeoplesDay && m == 10 && dow == DayOfWeek.Monday && IsNthWeekday(date, 2)) return true;
            if (settings.ExcludeVeteransDay && m == 11 && d == 11) return true;
            if (settings.ExcludeThanksgiving && m == 11 && dow == DayOfWeek.Thursday && IsNthWeekday(date, 4)) return true;
            if (settings.ExcludeChristmas && m == 12 && d == 25) return true;

            return false;
        }

        private bool IsNthWeekday(DateTime date, int n)
        {
            return (date.Day - 1) / 7 + 1 == n;
        }

        private bool IsLastMonday(DateTime date)
        {
            return date.AddDays(7).Month != date.Month;
        }
    }
}
