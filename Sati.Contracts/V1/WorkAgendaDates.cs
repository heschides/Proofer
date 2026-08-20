namespace Sati.Contracts.V1;

/// <summary>
/// Owns the calendar rule that maps Tomorrow's Agenda to its durable work date.
/// Weekends are not agenda days, so Friday, Saturday, and Sunday all resolve to
/// Monday. Holidays remain ordinary work dates until Sati has an authoritative
/// agency holiday-calendar policy for agenda rollover.
/// </summary>
public static class WorkAgendaDates
{
    public static DateTime NextWorkday(DateTime date)
    {
        var day = date.Date;
        return day.DayOfWeek switch
        {
            DayOfWeek.Friday => day.AddDays(3),
            DayOfWeek.Saturday => day.AddDays(2),
            _ => day.AddDays(1)
        };
    }
}
