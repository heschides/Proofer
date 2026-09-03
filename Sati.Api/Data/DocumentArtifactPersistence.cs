using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sati.Api.Data;

internal static class DocumentArtifactPersistence
{
    public static Task<ServerDocumentArtifact> StageGeneratedAsync(
        ApiDbContext db,
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
        int? templateVersion = null) =>
        StageReplacementAsync(db, new ServerDocumentArtifact
        {
            PersonId = personId,
            AgencyId = agencyId,
            Kind = kind.ToString(),
            CycleStart = cycleStart.Date,
            Origin = origin.ToString(),
            GeneratedAtUtc = generatedAtUtc,
            GeneratedByUserId = generatedByUserId,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(content)),
            ByteCount = content.LongLength,
            SuggestedFileName = suggestedFileName,
            TemplateOwner = templateOwner,
            TemplateKey = templateKey,
            TemplateVersion = templateVersion,
            BlankFieldsJson = JsonSerializer.Serialize(
                (blankFields ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order().ToArray())
        }, cancellationToken);

    public static Task<ServerDocumentArtifact> StageExternalAsync(
        ApiDbContext db,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        DateTime cycleStart,
        DateTime recordedAtUtc,
        int recordedByUserId,
        string note,
        CancellationToken cancellationToken)
    {
        var noteError = AnnualDocumentRules.ValidateExternalNote(note);
        if (noteError is not null)
            throw new ArgumentException(noteError, nameof(note));
        return StageReplacementAsync(db, new ServerDocumentArtifact
        {
            PersonId = personId,
            AgencyId = agencyId,
            Kind = kind.ToString(),
            CycleStart = cycleStart.Date,
            Origin = DocumentArtifactOrigin.RecordedAsExternal.ToString(),
            GeneratedAtUtc = recordedAtUtc,
            GeneratedByUserId = recordedByUserId,
            BlankFieldsJson = "[]",
            ExternalNote = note.Trim()
        }, cancellationToken);
    }

    public static DocumentArtifactDto ToDto(ServerDocumentArtifact artifact) => new(
        artifact.Id, artifact.PersonId, artifact.AgencyId, artifact.Kind,
        artifact.CycleStart, artifact.Origin, artifact.GeneratedAtUtc,
        artifact.GeneratedByUserId, artifact.ContentSha256, artifact.ByteCount,
        artifact.SuggestedFileName,
        JsonSerializer.Deserialize<string[]>(artifact.BlankFieldsJson) ?? [],
        artifact.ExternalNote,
        artifact.TemplateOwner, artifact.TemplateKey, artifact.TemplateVersion);

    private static async Task<ServerDocumentArtifact> StageReplacementAsync(
        ApiDbContext db,
        ServerDocumentArtifact replacement,
        CancellationToken cancellationToken)
    {
        var prior = await db.DocumentArtifacts.SingleOrDefaultAsync(candidate =>
            candidate.PersonId == replacement.PersonId &&
            candidate.Kind == replacement.Kind &&
            candidate.CycleStart == replacement.CycleStart &&
            candidate.SupersededByArtifactId == null,
            cancellationToken);
        if (prior is not null)
        {
            prior.SupersededByArtifactId = prior.Id;
            await db.SaveChangesAsync(cancellationToken);
        }

        db.DocumentArtifacts.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        if (prior is not null)
            prior.SupersededByArtifactId = replacement.Id;
        return replacement;
    }
}
