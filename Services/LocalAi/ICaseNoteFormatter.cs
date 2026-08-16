using Sati.Models;

namespace Sati.Services.LocalAi
{
    public sealed record CaseNoteFormattingRequest(
        int PersonId,
        string RawNarrative,
        NoteType? NoteType,
        FormType? FormType,
        string CaseManagerFullName,
        string? ConsumerFirstName,
        string FallbackFollowUp,
        string ClientContext,
        string StructuredVisitFacts);

    public sealed record CaseNoteFormattingProgress(
        string Message,
        double? Percent = null);

    public sealed record CaseNoteFormattingResult(
        string DraftNarrative,
        IReadOnlyList<string> Warnings);

    public interface ICaseNoteFormatter
    {
        bool IsEnabled { get; }
        int MaxInputWords { get; }

        Task<CaseNoteFormattingResult> FormatAsync(
            CaseNoteFormattingRequest request,
            IProgress<CaseNoteFormattingProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
