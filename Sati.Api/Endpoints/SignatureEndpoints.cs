using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;
using Sati.Signatures;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapSignatures(RouteGroupBuilder api)
    {
        api.MapGet("/signatures/availability", (SignatureFeature feature, SignatureOptions options, HttpContext context) =>
        {
            PreventSensitiveResponseCaching(context);
            return Results.Ok(new SignatureAvailabilityDto(feature.Enabled,
                feature.Enabled ? "Electronic signing is for consumers explicitly marked as Test when created. Use only fictional records; do not relabel an existing real consumer. Delivery remains controlled by this environment."
                    : "Electronic signing is unavailable in this environment. Continue with the paper or assisted process.",
                feature.Enabled && options.EmailEnabled ? "RestrictedTestRecipients" : "Suppressed"));
        });
        api.MapGet("/signatures/catalog", (HttpContext context) =>
        { PreventSensitiveResponseCaching(context); return Results.Ok(SignatureMeaningCatalog.All); });
        var signatures = api.MapGroup("").AddEndpointFilter<SignatureEnabledFilter>();
        signatures.MapGet("/people/{personId:int}/signature-signers", GetSignatureSigners);
        signatures.MapGet("/people/{personId:int}/signature-requests", ListSignatureRequests);
        signatures.MapPost("/people/{personId:int}/documents/{artifactId:int}/freeze", FreezeSignatureDocument);
        signatures.MapPost("/signature-requests", CreateStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/replace", ReplaceStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/revoke", RevokeStaffSignatureRequest);
        signatures.MapPost("/signature-requests/{requestId:int}/withdraw-authorization", WithdrawStaffSignatureAuthorization);
        signatures.MapGet("/signature-requests/{requestId:int}/original.pdf", DownloadOriginalSignatureDocument);
        signatures.MapGet("/signature-requests/{requestId:int}/signed.pdf", DownloadSignedSignatureDocument);
    }

    private static Task<IResult> SignatureTransaction(ApiDbContext db, Func<Task<IResult>> operation, CancellationToken ct) =>
        new SignatureStaffSingleAttempt(db).ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await operation();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    private static SignatureActor SigningActor(Actor actor) => new(actor.AgencyId, actor.UserId);

    private static Task<IResult> GetSignatureSigners(int personId, ClaimsPrincipal principal, ApiDbContext db,
        AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, personId, ct) is not { } person) return Results.NotFound();
        var contacts = await db.PersonContacts.AsNoTracking().Where(x => x.PersonId == personId && x.IsActive &&
            (x.Kind == "Guardian" || x.Kind == "AuthorizedRepresentative")).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(ct);
        var signers = new List<SignatureSignerDto> { new(SignerCapacity.Consumer, null, SigningName(person.FirstName, person.LastName), person.Email?.Trim()) };
        signers.AddRange(contacts.Select(x => new SignatureSignerDto(Enum.Parse<SignerCapacity>(x.Kind), x.Id, SigningName(x.FirstName, x.LastName), x.Email?.Trim())));
        audit.Record(actor, "signature.staff-signers-released", "Person", personId, JsonSerializer.Serialize(new { count = signers.Count }));
        return Results.Ok(signers);
    }, ct);

    private static Task<IResult> ListSignatureRequests(int personId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSafetyPerson(db, actor, personId, ct) is null) return Results.NotFound();
        var result = await runtime.Workflow.ListAsync(SigningActor(actor), personId, ct);
        audit.Record(actor, "signature.staff-history-released", "Person", personId, JsonSerializer.Serialize(new { count = result.Count }));
        return Results.Ok(result);
    }, ct);

    private static Task<IResult> FreezeSignatureDocument(int personId, int artifactId, FreezeSignatureDocumentRequest input,
        ClaimsPrincipal principal, ApiDbContext db, SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, personId, ct) is null) return Results.NotFound();
        var frozen = await runtime.Workflow.FreezeAsync(SigningActor(actor), personId, artifactId, input, ct);
        audit.Record(actor, "signature.document-frozen", "DocumentArtifact", artifactId,
            JsonSerializer.Serialize(new { frozen.Id, frozen.ContentSha256, frozen.ByteCount }));
        return Results.Ok(frozen);
    }, ct);

    private static Task<IResult> CreateStaffSignatureRequest(CreateSignatureRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSigningPerson(db, actor, input.PersonId, ct) is not { } person) return Results.NotFound();
        var signer = await ResolveSignatureSigner(db, person, input.SignerCapacity, input.SignerContactId, ct);
        ConfirmSignatureSnapshot(signer, input.ExpectedSignerName, input.ExpectedDeliveryEmail);
        return Results.Ok(await runtime.Workflow.CreateAsync(SigningActor(actor), input, signer, ct));
    }, ct);

    private static Task<IResult> ReplaceStaffSignatureRequest(int requestId, ReplaceSignatureRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        var request = await AccessibleSignatureRequest(db, actor, requestId, ct);
        if (request is null) return Results.NotFound();
        var person = await AccessibleSigningPerson(db, actor, request.PersonId, ct);
        if (person is null || !Enum.TryParse<SignerCapacity>(request.SignerCapacity, out var capacity)) return Results.NotFound();
        var signer = await ResolveSignatureSigner(db, person, capacity, request.SignerContactId, ct);
        ConfirmSignatureSnapshot(signer, input.ExpectedSignerName, input.ExpectedDeliveryEmail);
        // A fresh request snapshots the currently verified record; the old identity is never rewritten.
        return Results.Ok(await runtime.Workflow.ReplaceAsync(SigningActor(actor), requestId, input, signer, ct));
    }, ct);

    private static Task<IResult> RevokeStaffSignatureRequest(int requestId, SignatureReasonRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is not { } request ||
            await AccessibleSigningPerson(db, actor, request.PersonId, ct) is null) return Results.NotFound();
        return Results.Ok(await runtime.Workflow.RevokeAsync(SigningActor(actor), requestId, input, ct));
    }, ct);

    private static Task<IResult> WithdrawStaffSignatureAuthorization(int requestId, SignatureReasonRequest input, ClaimsPrincipal principal,
        ApiDbContext db, SignatureStaffRuntime runtime, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is not { } request ||
            await AccessibleSigningPerson(db, actor, request.PersonId, ct) is null) return Results.NotFound();
        return Results.Ok(await runtime.Workflow.WithdrawAuthorizationAsync(SigningActor(actor), requestId, input, ct));
    }, ct);

    private static Task<IResult> DownloadOriginalSignatureDocument(int requestId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => DownloadStaffSignatureDocument(requestId, false, principal, db, runtime, audit, ct);
    private static Task<IResult> DownloadSignedSignatureDocument(int requestId, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => DownloadStaffSignatureDocument(requestId, true, principal, db, runtime, audit, ct);
    private static Task<IResult> DownloadStaffSignatureDocument(int requestId, bool signed, ClaimsPrincipal principal, ApiDbContext db,
        SignatureStaffRuntime runtime, AuditTrail audit, CancellationToken ct) => SignatureTransaction(db, async () =>
    {
        var actor = Actor.From(principal);
        if (await AccessibleSignatureRequest(db, actor, requestId, ct) is null) return Results.NotFound();
        var bytes = await runtime.Workflow.DownloadAsync(SigningActor(actor), requestId, signed, ct);
        audit.Record(actor, "signature.staff-document-released", "SignatureRequest", requestId,
            JsonSerializer.Serialize(new { signed, hash = SignatureSecrets.Hash(bytes), byteCount = bytes.LongLength }));
        return Results.File(bytes, "application/pdf", $"Signature-{requestId}-{(signed ? "signed" : "original")}.pdf");
    }, ct);

    private static async Task<Sati.Models.SignatureRequest?> AccessibleSignatureRequest(ApiDbContext db, Actor actor, int requestId, CancellationToken ct)
    {
        var request = await db.SignatureRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId && x.AgencyId == actor.AgencyId, ct);
        return request is not null && await AccessibleSafetyPerson(db, actor, request.PersonId, ct) is not null ? request : null;
    }
    private static async Task<ServerPerson?> AccessibleSigningPerson(ApiDbContext db, Actor actor, int personId, CancellationToken ct)
    {
        var person = await AccessibleSafetyPerson(db, actor, personId, ct);
        if (person is not null && !person.IsTestData)
            throw new SignatureWorkflowException("signature_test_consumer_required",
                "Use a fictional consumer explicitly marked as Test when created. Electronic signing is unavailable for ordinary consumer records.");
        return person;
    }
    private static async Task<VerifiedSignatureSigner> ResolveSignatureSigner(ApiDbContext db, ServerPerson person,
        SignerCapacity capacity, int? contactId, CancellationToken ct)
    {
        string name; string? email; DateTime? birthDate = null;
        if (capacity == SignerCapacity.Consumer && contactId is null)
        { name = SigningName(person.FirstName, person.LastName); email = person.Email; birthDate = person.BirthDate; }
        else if (capacity is SignerCapacity.Guardian or SignerCapacity.AuthorizedRepresentative && contactId is > 0)
        {
            var contact = await db.PersonContacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contactId && x.PersonId == person.Id &&
                x.IsActive && x.Kind == capacity.ToString(), ct);
            if (contact is null) throw InvalidSignatureSigner();
            name = SigningName(contact.FirstName, contact.LastName); email = contact.Email;
        }
        else throw InvalidSignatureSigner();
        email = email?.Trim();
        if (name.Length is < 1 or > 120 || email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)) throw InvalidSignatureSigner();
        return new(name, email, birthDate);
    }
    private static string SigningName(string? first, string? last) => $"{first?.Trim()} {last?.Trim()}".Trim();
    private static void ConfirmSignatureSnapshot(VerifiedSignatureSigner current, string? expectedName, string? expectedEmail)
    {
        if (string.IsNullOrWhiteSpace(expectedName) || string.IsNullOrWhiteSpace(expectedEmail) ||
            !string.Equals(current.Name, expectedName, StringComparison.Ordinal) || !string.Equals(current.Email, expectedEmail, StringComparison.Ordinal))
            throw new SignatureWorkflowException("signature_signer_changed",
                "The signer's name or preferred email changed. Reload, choose the intended signer, and confirm their current details before continuing.", 409);
    }
    private static SignatureWorkflowException InvalidSignatureSigner() => new("signature_signer_invalid",
        "Choose an active guardian or representative from this consumer's contacts, or the consumer. Correct the name and preferred email in that record before continuing.");
}
