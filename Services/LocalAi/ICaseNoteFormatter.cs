using Sati.Models;
using Sati.Contracts.V1;

namespace Sati.Services.LocalAi
{
    public sealed record CaseNoteFormattingRequest(
        int PersonId,
        string RawNarrative,
        NoteType? NoteType,
        FormType? FormType,
        string CaseManagerFullName,
        string? ConsumerFirstName,
        string SourceFingerprint,
        IReadOnlyList<CaseNoteDraftFact> Facts);

    public sealed record CaseNoteFormattingProgress(
        string Message,
        double? Percent = null);

    public sealed record CaseNoteFormattingResult(
        string DraftNarrative,
        IReadOnlyList<string> Warnings,
        string SourceFingerprint,
        IReadOnlySet<string> UsedFactIds);

    public sealed class CaseNoteDraftRejectedException(
        string message,
        IReadOnlyList<string> errors) : InvalidOperationException(message)
    {
        public IReadOnlyList<string> Errors { get; } = errors;
    }

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
