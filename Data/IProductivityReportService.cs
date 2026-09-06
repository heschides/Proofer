namespace Sati.Data;

/// <summary>
/// A narrative-free monthly projection for the Statistics workspace. The full
/// note entity can contain a very large clinical narrative, which this report
/// neither displays nor needs to transfer from the database.
/// </summary>
public sealed record ProductivityMonthUnits(int Year, int Month, int Units);

public interface IProductivityReportService
{
    Task<IReadOnlyList<ProductivityMonthUnits>> GetUnitsAsync(
        DateTime windowStart,
        DateTime windowEnd);
}
