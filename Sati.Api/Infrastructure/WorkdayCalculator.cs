using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

internal static class WorkdayCalculator
{
    public static int Count(DateTime startInclusive, DateTime endInclusive, ServerSettings settings)
    {
        var count = 0;
        for (var date = startInclusive.Date; date <= endInclusive.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            if (IsExcluded(date, settings))
                continue;
            count++;
        }
        return count;
    }

    public static bool IsExcluded(DateTime date, ServerSettings settings)
    {
        var dow = date.DayOfWeek;
        if (settings.ExcludeMonday && dow == DayOfWeek.Monday) return true;
        if (settings.ExcludeTuesday && dow == DayOfWeek.Tuesday) return true;
        if (settings.ExcludeWednesday && dow == DayOfWeek.Wednesday) return true;
        if (settings.ExcludeThursday && dow == DayOfWeek.Thursday) return true;
        if (settings.ExcludeFriday && dow == DayOfWeek.Friday) return true;

        var month = date.Month;
        var day = date.Day;
        if (settings.ExcludeNewYearsDay && month == 1 && day == 1) return true;
        if (settings.ExcludeMLKDay && month == 1 && dow == DayOfWeek.Monday && IsNth(date, 3)) return true;
        if (settings.ExcludePresidentsDay && month == 2 && dow == DayOfWeek.Monday && IsNth(date, 3)) return true;
        if (settings.ExcludeMemorialDay && month == 5 && dow == DayOfWeek.Monday && date.AddDays(7).Month != month) return true;
        if (settings.ExcludeJuneteenth && month == 6 && day == 19) return true;
        if (settings.ExcludeIndependenceDay && month == 7 && day == 4) return true;
        if (settings.ExcludeLaborDay && month == 9 && dow == DayOfWeek.Monday && IsNth(date, 1)) return true;
        if (settings.ExcludeIndigenousPeoplesDay && month == 10 && dow == DayOfWeek.Monday && IsNth(date, 2)) return true;
        if (settings.ExcludeVeteransDay && month == 11 && day == 11) return true;
        if (settings.ExcludeThanksgiving && month == 11 && dow == DayOfWeek.Thursday && IsNth(date, 4)) return true;
        if (settings.ExcludeDayAfterThanksgiving && month == 11 && dow == DayOfWeek.Friday && IsNth(date.AddDays(-1), 4)) return true;
        if (settings.ExcludeChristmas && month == 12 && day == 25) return true;
        return false;
    }

    private static bool IsNth(DateTime date, int n) => (date.Day - 1) / 7 + 1 == n;
}
