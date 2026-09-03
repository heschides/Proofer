using Sati.Models;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Data;

/// <summary>
/// The local Production implementation: everything happens on the workstation.
///
/// No network call, no PHI in transit, and nothing here needs the API to be
/// reachable — a case manager with no connectivity still produces a form. That is
/// the requirement this class exists to satisfy, and it is why the filler lives in
/// the shared <c>Sati.Forms</c> library rather than inside <c>Sati.Api</c>: one
/// implementation of the stamping, two processes that run it.
///
/// AN SSN IS NEVER FILLED HERE. Social Security numbers are cloud-only, encrypted
/// under a Key Vault key this process has no access to, and deliberately declared as
/// shadow properties on <c>SatiContext</c> so the local model has no property to read
/// them through. The SSN box therefore prints blank in local Production, and the
/// result says so rather than leaving the case manager to notice on paper. See
/// DECISIONS.md, "An SSN is cloud-only".
/// </summary>
public sealed class DhhsFormService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    LocalSsnStore ssnStore) : IDhhsFormService
{
    /// <summary>
    /// Local Production stores SSNs, protected by the Windows user's DPAPI key.
    ///
    /// This reverses the original cloud-only decision, and the reason is workflow
    /// rather than architecture: filling the Appointment form is occasional, but
    /// reading a consumer's number to the Social Security Administration on their
    /// behalf is routine, and a case manager cannot do that from a blank box. The
    /// protection is real but its limits are narrower than the cloud path's — see
    /// <see cref="DpapiKeyWrapper"/>.
    /// </summary>
    public bool SupportsSsnStorage => true;

    /// <summary>
    /// Local Production can show the number. That is the point of storing it here:
    /// reading it to the Social Security Administration on a consumer's behalf is
    /// routine work and cannot be done from a mask.
    /// </summary>
    public bool SupportsSsnReveal => true;

    public async Task<SsnStatusDto> GetSsnStatusAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        return new SsnStatusDto(
            LocalSsnStore.MaskFor(context, person),
            LocalSsnStore.IsOnFile(context, person));
    }

    public async Task<SsnStatusDto> UpdateSsnAsync(
        int personId,
        string? socialSecurityNumber,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An SSN cannot be stored without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        var normalized = SsnMask.Normalize(socialSecurityNumber);
        await ssnStore.SetAsync(context, person, normalized, cancellationToken);

        // The action, never the value. An audit row naming what changed is the point;
        // one containing the number would defeat the column it describes.
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.PersonSsnUpdated,
            "Person",
            personId);
        await context.SaveChangesAsync(cancellationToken);

        return new SsnStatusDto(
            LocalSsnStore.MaskFor(context, person),
            LocalSsnStore.IsOnFile(context, person));
    }

    /// <summary>
    /// Reveals the stored number for the caller to read aloud or transcribe, and
    /// records that it was read.
    ///
    /// Audited separately from any document it might feed, because a disclosure is
    /// the read itself — the same reason the API records `person.ssn-decrypted`
    /// alongside the form it generated.
    /// </summary>
    public async Task<string> RevealSsnAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An SSN cannot be read without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        var ssn = await ssnStore.RevealAsync(context, person, cancellationToken);

        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.PersonSsnRevealed,
            "Person",
            personId);
        await context.SaveChangesAsync(cancellationToken);

        return ssn;
    }

    /// <summary>
    /// Scoped to the signed-in case manager's own caseload and agency, the same
    /// restriction the API route applies. A transitional local service repeats the
    /// rule rather than relying on being the only caller.
    /// </summary>
    private async Task<Person> LoadOwnPersonAsync(
        SatiContext context,
        int personId,
        CancellationToken cancellationToken)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("No user is signed in.");

        return await context.People.SingleOrDefaultAsync(
            candidate => candidate.Id == personId &&
                         candidate.UserId == actor.Id &&
                         candidate.AgencyId == actor.AgencyId,
            cancellationToken)
            ?? throw new InvalidOperationException("That consumer is not on your caseload.");
    }

    public async Task<DhhsFormResult> GenerateAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("A form cannot be filled without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var agencyId = actor.AgencyId;

        // Scoped to the signed-in case manager's own caseload and agency, the same
        // restriction the API route applies. A transitional local service repeats the
        // rule rather than relying on being the only caller.
        // Tracked, not AsNoTracking: the encrypted SSN lives in shadow properties, and
        // shadow values are held by the change tracker. An untracked entity has none,
        // so the number would silently read as absent and the box would print blank.
        var person = await context.People
            .SingleOrDefaultAsync(
                candidate => candidate.Id == personId &&
                             candidate.UserId == actor.Id &&
                             candidate.AgencyId == actor.AgencyId,
                cancellationToken)
            ?? throw new InvalidOperationException("That consumer is not on your caseload.");

        var agency = await context.Agencies.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);

        // Decrypted only if one is on file, and recorded as a disclosure in its own
        // right — the same separation the API keeps between reading a number and the
        // document that occasioned the read.
        string? ssn = null;
        if (LocalSsnStore.IsOnFile(context, person))
        {
            ssn = await ssnStore.RevealAsync(context, person, cancellationToken);
            LocalAuditTrail.Record(
                context, actor, LocalAuditActions.PersonSsnRevealed, "Person", personId);
        }

        var subject = new DhhsFormDefinition.Subject(
                FullName: $"{person.LastName}, {person.FirstName}".Trim(' ', ','),
                BirthDate: person.BirthDate,
                Address: person.Address,
                PhoneNumber: person.PhoneNumber,
                SocialSecurityNumber: ssn,
                RepresentativeName: null,
                RepresentativeAddress: null,
                RepresentativePhone: null,
                RepresentativeEmail: null)
            .WithRepresentative(
                actor.DisplayName,
                actor.Phone,
                actor.Email,
                agency?.Street,
                agency?.City,
                agency?.State,
                agency?.Zip);

        var pdf = new DhhsFormFiller().Fill(form, subject, selections);
        var blankFields = DhhsFormDefinition.UnfilledFields(form, subject).ToList();
        if (form == DhhsFormDefinition.FormKey.AuthorizationToRelease)
        {
            var isDraft = (selections.Checks?.Count ?? 0) == 0 &&
                (selections.Text?.Count ?? 0) == 0;
            if (isDraft)
                blankFields.Add("Consumer authorization choices");
            var fileName = SuggestedFileName(form, person.LastName, person.FirstName, personId);
            var cycleStart = AnnualDocumentCycle.CurrentStart(
                person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
                DateTime.Today);
            await DocumentArtifactStore.StageGeneratedAsync(
                context,
                personId,
                actor.AgencyId,
                AnnualDocumentKind.ReleaseDhhs,
                cycleStart,
                isDraft ? DocumentArtifactOrigin.Draft : DocumentArtifactOrigin.GeneratedInSati,
                DateTime.UtcNow,
                actor.Id,
                pdf,
                fileName,
                blankFields,
                cancellationToken);
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.DocumentGenerated,
                "Person",
                personId,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = AnnualDocumentKind.ReleaseDhhs.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    origin = isDraft ? DocumentArtifactOrigin.Draft.ToString() : DocumentArtifactOrigin.GeneratedInSati.ToString()
                }));
        }

        // Generating a release form is a disclosure whichever environment produced it,
        // so the local path records the same action name the API route does.
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.DhhsFormGenerated,
            "Person",
            personId,
            $"{{\"Form\":\"{form}\"}}");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DhhsFormResult(
            pdf,
            SuggestedFileName(form, person.LastName, person.FirstName, personId),
            blankFields);
    }

    internal static string SuggestedFileName(
        DhhsFormDefinition.FormKey form,
        string? lastName,
        string? firstName,
        int personId)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrEmpty(name)
            ? $"{form}-{personId}.pdf"
            : $"{form}-{personId}-{name}.pdf";
    }
}
