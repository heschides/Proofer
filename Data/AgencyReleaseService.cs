using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using System.Text.Json;

namespace Sati.Data;

/// <summary>Local Production generation. All profile reads and rendering stay on the workstation.</summary>
public sealed class AgencyReleaseService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    AgencyReleasePdfGenerator generator) : IAgencyReleaseService
{
    public async Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        AgencyReleaseRules.EnsureValid(request);
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An agency release cannot be generated without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == personId &&
                         candidate.UserId == actor.Id &&
                         candidate.AgencyId == actor.AgencyId,
            cancellationToken)
            ?? throw new InvalidOperationException("That consumer is not on your caseload.");
        var agency = await context.Agencies.AsNoTracking().SingleAsync(
            candidate => candidate.Id == actor.AgencyId,
            cancellationToken);

        var generatedAtUtc = DateTime.UtcNow;
        var subject = new AgencyReleaseSubject(
            person.Id,
            person.FullName,
            person.BirthDate,
            person.HasGuardian ? person.GuardianName : null,
            agency.Name,
            ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
            agency.EdiContactPhone,
            actor.DisplayName,
            actor.Role.ToString());
        var pdf = generator.Generate(subject, request, generatedAtUtc);

        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.AgencyReleaseGenerated,
            "Person",
            personId,
            JsonSerializer.Serialize(new
            {
                Scope = request.Scope,
                StaffAttestation = request.ConfirmedObtainedRoi,
                Revocation = request.IsRevocation,
            }));
        await context.SaveChangesAsync(cancellationToken);

        return new AgencyReleaseResult(
            pdf,
            SuggestedFileName(person.Id, person.LastName, person.FirstName, request.IsRevocation));
    }

    internal static string SuggestedFileName(
        int personId,
        string? lastName,
        string? firstName,
        bool revocation)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        var prefix = revocation ? "Agency-Release-Revocation" : "Agency-Release";
        return string.IsNullOrEmpty(name)
            ? $"{prefix}-{personId}.pdf"
            : $"{prefix}-{personId}-{name}.pdf";
    }

    internal static string? ComposeAddress(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return present.Length == 0 ? null : string.Join(", ", present);
    }
}
