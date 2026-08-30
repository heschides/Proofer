namespace Sati
{
    // Ordered ring for the task board's date filter. Declaration order IS the click
    // order — CycleDateFilter walks Enum.GetValues<T>() and wraps, so inserting a
    // value here changes the cycle with no other edit.
    //
    // The week values are upper bounds only. They deliberately have no lower bound,
    // so overdue items stay visible inside every window; a compliance board that
    // hides overdue work when you narrow the view is a liability.
    //
    // This ring also replaces the old DefaultLookaheadDays cap in BuildFormRows —
    // the filter is now the only thing bounding how far ahead the board looks.
    public enum BoardDateFilter
    {
        Overdue,
        TwoWeeks,
        FourWeeks,
        SixWeeks,
        EightWeeks,
        TenWeeks,
        TwelveWeeks,
        All
    }
}