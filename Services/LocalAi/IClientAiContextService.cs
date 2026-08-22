namespace Sati.Services.LocalAi
{
    public sealed record ClientAiContextSource(
        string Category,
        string Description);

    public sealed record ClientAiContext(
        int PersonId,
        string? ConsumerFirstName,
        IReadOnlyList<ClientAiContextSource> Sources);

    /// <summary>
    /// Validates the selected-client boundary and returns only the minimum identity used by local
    /// drafting. Historical record content is intentionally outside this contract.
    /// </summary>
    public interface IClientAiContextService
    {
        Task<ClientAiContext> BuildAsync(
            int personId,
            CancellationToken cancellationToken = default);
    }
}
