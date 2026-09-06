using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudProductivityReportService(CloudApiClient api)
    : IProductivityReportService
{
    public async Task<IReadOnlyList<ProductivityMonthUnits>> GetUnitsAsync(
        DateTime windowStart,
        DateTime windowEnd)
    {
        var start = windowStart.ToString(
            "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var end = windowEnd.ToString(
            "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var rows = await api.GetAsync<List<ProductivityMonthUnitsDto>>(
            $"/api/v1/reports/productivity-units?start={start}&end={end}");

        return rows
            .Select(row => new ProductivityMonthUnits(row.Year, row.Month, row.Units))
            .ToList();
    }
}
