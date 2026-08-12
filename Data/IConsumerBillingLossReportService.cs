namespace Sati.Data
{
    public sealed record ConsumerBillingLossRow(
        int PersonId,
        string ConsumerName,
        int BillableDays,
        int NonBillableDays,
        int BillableUnits,
        int NonBillableUnits,
        decimal? LostWorkPercentage);

    public sealed record ConsumerBillingLossReport(
        IReadOnlyList<ConsumerBillingLossRow> Consumers,
        int TotalBillableUnits,
        int TotalNonBillableUnits,
        decimal? LostWorkPercentage);

    public interface IConsumerBillingLossReportService
    {
        Task<ConsumerBillingLossReport> GetAsync(
            int userId,
            DateTime windowStart,
            DateTime windowEnd);
    }
}
