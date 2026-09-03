using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

internal static class DocumentArtifactStore
{
    public static async Task<DocumentArtifact> StageGeneratedAsync(
        SatiContext context,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DocumentArtifactOrigin origin,
        DateTime generatedAtUtc,
        int generatedByUserId,
        byte[] content,
        string suggestedFileName,
        IReadOnlyCollection<string>? blankFields,
        CancellationToken cancellationToken,
        string? templateOwner = null,
        string? templateKey = null,
        int? templateVersion = null)
    {
        var artifact = DocumentArtifact.Generated(
            personId, agencyId, kind, cycleStart, origin, generatedAtUtc,
            generatedByUserId, content, suggestedFileName, blankFields,
            templateOwner, templateKey, templateVersion);
        return await StageReplacementAsync(context, artifact, personId, kind, cycleStart, cancellationToken);
    }

    public static async Task<DocumentArtifact> StageExternalAsync(
        SatiContext context,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DateTime recordedAtUtc,
        int recordedByUserId,
        string note,
        CancellationToken cancellationToken)
    {
        var artifact = DocumentArtifact.External(
            personId, agencyId, kind, cycleStart, recordedAtUtc, recordedByUserId, note);
        return await StageReplacementAsync(context, artifact, personId, kind, cycleStart, cancellationToken);
    }

    public static DocumentArtifactDto ToDto(DocumentArtifact artifact) => new(
        artifact.Id,
        artifact.PersonId,
        artifact.AgencyId,
        artifact.Kind.ToString(),
        artifact.CycleStart,
        artifact.Origin.ToString(),
        artifact.GeneratedAtUtc,
        artifact.GeneratedByUserId,
        artifact.ContentSha256,
        artifact.ByteCount,
        artifact.SuggestedFileName,
        JsonSerializer.Deserialize<string[]>(artifact.BlankFieldsJson) ?? [],
        artifact.ExternalNote,
        artifact.TemplateOwner,
        artifact.TemplateKey,
        artifact.TemplateVersion);

    private static async Task<DocumentArtifact> StageReplacementAsync(
        SatiContext context,
        DocumentArtifact replacement,
        int personId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        CancellationToken cancellationToken)
    {
        var prior = await context.DocumentArtifacts.SingleOrDefaultAsync(candidate =>
            candidate.PersonId == personId && candidate.Kind == kind &&
            candidate.CycleStart == cycleStart.Date && candidate.SupersededByArtifactId == null,
            cancellationToken);

        if (prior is not null)
        {
            // Release the filtered unique slot inside the caller's transaction. The
            // self-reference is replaced with the real successor id before commit.
            prior.MarkSuperseded(prior.Id);
            await context.SaveChangesAsync(cancellationToken);
        }

        context.DocumentArtifacts.Add(replacement);
        await context.SaveChangesAsync(cancellationToken);
        if (prior is not null)
            prior.MarkSuperseded(replacement.Id);
        return replacement;
    }
}
