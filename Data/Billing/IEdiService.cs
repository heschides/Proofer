namespace Sati.Edi
{
    public interface IEdiService
    {
        // Generates an 837P file for the given billing period and writes it
        // to the configured output directory. Returns the full file path.
        Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey);
    }
}