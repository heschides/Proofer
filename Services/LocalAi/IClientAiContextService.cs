namespace Sati.Services.LocalAi
{
    public sealed record ClientAiContextSource(
        string Category,
        string Description);

    public sealed record ClientAiContext(
        string PromptText,
        IReadOnlyList<ClientAiContextSource> Sources);

    /// <summary>
    /// Builds a purpose-limited, permission-checked snapshot for local case-note
    /// formatting. The snapshot is transient and is never persisted as a prompt.
    /// </summary>
    public interface IClientAiContextService
    {
        Task<ClientAiContext> BuildAsync(
            int personId,
            int requestingUserId,
            string roughNarrative,
            int? excludedNoteId = null,
            CancellationToken cancellationToken = default);
    }
}
