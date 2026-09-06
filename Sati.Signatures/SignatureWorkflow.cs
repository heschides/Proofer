using System.Data;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Signatures;

public sealed record SignatureActor(int AgencyId, int UserId);
public sealed record VerifiedSignatureSigner(string Name, string Email, DateTime? BirthDate = null);
public sealed record SignatureAuthentication(string SessionToken, DateTime ExpiresAtUtc);
public sealed record SignaturePortalDetails(string SignerName, string Capacity, string DocumentName, string DisclosureVersion,
    string DisclosureText, string IntentText, string State, bool HasConsent, bool DocumentReleased, bool AccessAcknowledged,
    DateTime SessionExpiresAtUtc, DateTime RequestExpiresAtUtc, bool HasPackage, string SessionBinding);

/// <summary>Shared authoritative workflow. Staff adapters recheck permissions inside this same context/transaction.</summary>
public sealed class SignatureWorkflow(DbContext db, SignatureFeature feature, SignatureOptions options,
    ISignatureBlobStore blobs, SigningPinProtector pins, SignatureOutboxProtector outbox, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static SignatureWorkflowException Invalid(string message) => new("invalid_signature_request", message);
    private static SignatureWorkflowException Unavailable() => new("signature_link_unavailable", "This signing link is unavailable. Contact your case manager for help or a paper copy.", 404);
    private static SignatureWorkflowException Conflict() => new("signature_changed", "This signing request has changed. Refresh its status before continuing.", 409);

    private async Task<T> Atomic<T>(Func<Task<T>> action, CancellationToken ct)
    {
        feature.RequireEnabled();
        var owns = db.Database.CurrentTransaction is null;
        await using var transaction = owns ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
    }

    private static void Actor(SignatureActor actor)
    {
        if (actor.AgencyId <= 0 || actor.UserId <= 0) throw new SignatureWorkflowException("signature_forbidden", "You cannot access this signing request.", 403);
    }

    private async Task<SignatureSourceDocument> Source(int agency, int person, int artifact, CancellationToken ct)
    {
        var source = await db.Set<SignatureSourceDocument>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == artifact && x.AgencyId == agency && x.PersonId == person, ct);
        if (source is null || source.SupersededByArtifactId is not null) throw Invalid("The original document is no longer current. Generate and review a new document.");
        bool complete;
        try { using var blanks = JsonDocument.Parse(source.BlankFieldsJson); complete = blanks.RootElement.ValueKind == JsonValueKind.Array && blanks.RootElement.GetArrayLength() == 0; }
        catch (JsonException) { complete = false; }
        if (source.Origin != nameof(DocumentArtifactOrigin.GeneratedInSati) || !complete || source.ContentSha256?.Length != 64 || source.ByteCount is null or <= 0 or > SignatureRules.MaximumPdfBytes)
            throw Invalid("Only a complete generated document can be sent for signing. A draft or an externally recorded document cannot be used.");
        return source;
    }

    private async Task<SignatureSourceDocument> Current(SignatureRequest request, CancellationToken ct)
    {
        var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleAsync(x => x.Id == request.FrozenDocumentId && x.AgencyId == request.AgencyId, ct);
        var source = await Source(request.AgencyId, request.PersonId, frozen.DocumentArtifactId, ct);
        if (source.ContentSha256 != frozen.ContentSha256 || source.ByteCount != frozen.ByteCount) throw Invalid("The retained document no longer matches its source.");
        return source;
    }

    public Task<FrozenSignatureDocumentDto> FreezeAsync(SignatureActor actor, int personId, int artifactId, FreezeSignatureDocumentRequest input, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        if (!input.CompletenessReviewed || input.ClientRequestId == Guid.Empty) throw Invalid("Confirm you have reviewed the complete document.");
        var source = await Source(actor.AgencyId, personId, artifactId, ct);
        if (!Enum.TryParse<AnnualDocumentKind>(source.Kind, out var kind) || SignatureMeaningCatalog.Find(kind)?.PolicyStatus != SignaturePolicyStatus.SyntheticTestingOnly)
            throw Invalid("Electronic signing is not available for this document type.");
        var pdf = input.Pdf;
        if (pdf is null || pdf.Length == 0 || pdf.Length > SignatureRules.MaximumPdfBytes || pdf.LongLength != source.ByteCount || SignatureSecrets.Hash(pdf) != source.ContentSha256)
            throw Invalid("Choose the exact saved PDF generated for this document. Its contents must match the recorded original.");
        try { using var stream = new MemoryStream(pdf, false); using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import); if (document.PageCount == 0) throw Invalid("The PDF has no pages."); }
        catch (SignatureWorkflowException) { throw; }
        catch { throw Invalid("The original PDF cannot be opened. Generate a new document before continuing."); }
        var existing = await db.Set<FrozenSignatureDocument>().SingleOrDefaultAsync(x => x.DocumentArtifactId == artifactId && x.AgencyId == actor.AgencyId, ct);
        if (existing is not null) return FrozenDto(existing);
        var frozen = new FrozenSignatureDocument { AgencyId = actor.AgencyId, PersonId = personId, DocumentArtifactId = artifactId,
            ContentSha256 = source.ContentSha256!, ByteCount = pdf.LongLength, BlobPath = $"originals/{actor.AgencyId}/{artifactId}/{Guid.NewGuid():N}.pdf", StoredAtUtc = Now, StoredByUserId = actor.UserId };
        await blobs.WriteOnceAsync(frozen.BlobPath, pdf, ct);
        db.Add(frozen);
        await db.SaveChangesAsync(ct);
        return FrozenDto(frozen);
    }, ct);

    private static FrozenSignatureDocumentDto FrozenDto(FrozenSignatureDocument value) => new(value.Id, value.DocumentArtifactId, value.ContentSha256, value.ByteCount, value.StoredAtUtc);

    public Task<SignatureRequestDto> CreateAsync(SignatureActor actor, CreateSignatureRequest input, VerifiedSignatureSigner signer, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        var existing = await db.Set<SignatureRequest>().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.IssuedByUserId == actor.UserId && x.ClientRequestId == input.ClientRequestId, ct);
        if (existing is not null)
        {
            var frozen = await db.Set<FrozenSignatureDocument>().SingleAsync(x => x.Id == existing.FrozenDocumentId, ct);
            if (existing.PersonId != input.PersonId || frozen.DocumentArtifactId != input.DocumentArtifactId || existing.SignerCapacity != input.SignerCapacity.ToString() || existing.SignerContactId != input.SignerContactId)
                throw Conflict();
            if (input.ExpiryHours is < SignatureRules.MinimumExpiryHours or > SignatureRules.MaximumExpiryHours || existing.SignerName != signer.Name.Trim() || existing.DeliveryEmail != signer.Email || existing.AuthorityEvidence != input.AuthorityEvidence?.Trim() ||
                existing.ExpiresAtUtc != existing.IssuedAtUtc.AddHours(input.ExpiryHours) || input.Pin != input.ConfirmPin || !input.IdentityConfirmed || !input.EmailConfirmed || !await pins.VerifyAsync(existing, input.Pin ?? "", ct)) throw Conflict();
            return await Dto(existing, ct);
        }
        var request = await Issue(actor, input, signer, null, ct);
        return await Dto(request, ct);
    }, ct);

    private async Task<SignatureRequest> Issue(SignatureActor actor, CreateSignatureRequest input, VerifiedSignatureSigner signer, int? replaces, CancellationToken ct)
    {
        if (input.ClientRequestId == Guid.Empty || !input.IdentityConfirmed || !input.EmailConfirmed || input.Pin != input.ConfirmPin || !SigningPinRules.IsValid(input.Pin, signer.BirthDate))
            throw Invalid("Confirm the signer's identity and email, then enter and confirm a fresh 8 to 12 digit signing code. Avoid dates of birth and counting sequences.");
        if (input.ExpiryHours is < SignatureRules.MinimumExpiryHours or > SignatureRules.MaximumExpiryHours) throw Invalid("The signing link must last between 24 hours and seven days.");
        if (string.IsNullOrWhiteSpace(signer.Name) || signer.Name.Length > 120 || signer.Email.Length > 254 || !MailAddress.TryCreate(signer.Email, out var email) || email.Address != signer.Email || signer.Email.Any(char.IsControl))
            throw Invalid("The signer needs a current name and a valid, confirmed email address.");
        if (input.SignerCapacity == SignerCapacity.Consumer && input.SignerContactId is not null || input.SignerCapacity != SignerCapacity.Consumer && (input.SignerContactId is null || string.IsNullOrWhiteSpace(input.AuthorityEvidence)))
            throw Invalid("Record the representative's current authority before sending a request.");
        if (input.AuthorityEvidence?.Length > 500) throw Invalid("Keep the authority explanation within 500 characters.");
        var source = await Source(actor.AgencyId, input.PersonId, input.DocumentArtifactId, ct);
        if (!Enum.TryParse<AnnualDocumentKind>(source.Kind, out var kind) || !SignatureMeaningCatalog.CanRequest(kind, input.SignerCapacity)) throw Invalid("This document or signer capacity is not cleared for this test workflow.");
        var frozen = await db.Set<FrozenSignatureDocument>().SingleOrDefaultAsync(x => x.DocumentArtifactId == input.DocumentArtifactId && x.AgencyId == actor.AgencyId && x.PersonId == input.PersonId, ct)
            ?? throw Invalid("Retain the exact reviewed PDF before requesting a signature.");
        if (source.ContentSha256 != frozen.ContentSha256 || source.ByteCount != frozen.ByteCount) throw Invalid("The original document no longer matches the retained copy.");
        if (await db.Set<SignatureRequest>().AnyAsync(x => x.FrozenDocumentId == frozen.Id && (x.State == "Issued" || x.State == "Viewed") && x.ExpiresAtUtc > Now, ct))
            throw Conflict();
        if (!Uri.TryCreate(options.PortalBaseUri, UriKind.Absolute, out var portal) || portal.Scheme != "https" || portal.AbsolutePath != "/" || !string.IsNullOrEmpty(portal.UserInfo) || !string.IsNullOrEmpty(portal.Query) || !string.IsNullOrEmpty(portal.Fragment))
            throw new SignatureWorkflowException("signature_portal_unavailable", "The secure signing website has not been configured.", 503);
        var token = SignatureSecrets.NewToken();
        var issuedAt = Now;
        var request = new SignatureRequest { AgencyId = actor.AgencyId, PersonId = input.PersonId, FrozenDocumentId = frozen.Id, ClientRequestId = input.ClientRequestId,
            SignerCapacity = input.SignerCapacity.ToString(), SignerContactId = input.SignerContactId, SignerName = signer.Name.Trim(), DeliveryEmail = signer.Email,
            AuthorityEvidence = input.AuthorityEvidence?.Trim(), TokenSha256 = SignatureSecrets.Hash(token), IssuedAtUtc = issuedAt, IssuedByUserId = actor.UserId,
            ExpiresAtUtc = issuedAt.AddHours(input.ExpiryHours), ReplacesRequestId = replaces, DisclosureVersion = SignatureRules.DisclosureVersion,
            DisclosureText = SignatureRules.DisclosureText, IntentText = SignatureMeaningCatalog.Find(kind)!.IntentText };
        await pins.SetAsync(request, input.Pin, ct);
        db.Add(request);
        await db.SaveChangesAsync(ct);
        Event(request, "Issued", "Staff", actor.UserId, null, false, new { identityConfirmed = true, emailConfirmed = true, sourceReviewed = true });
        var delivery = new SignatureOutbox { AgencyId = actor.AgencyId, RequestId = request.Id, Purpose = "Invitation", NextAttemptAtUtc = Now };
        db.Add(delivery);
        await db.SaveChangesAsync(ct);
        await outbox.ProtectAsync(delivery, new SignatureEmail(signer.Email, new Uri(portal, $"s/{token}").AbsoluteUri, "Invitation"), ct);
        delivery.Revision++;
        await db.SaveChangesAsync(ct);
        return request;
    }

    public Task<SignatureRequestDto> ReplaceAsync(SignatureActor actor, int requestId, ReplaceSignatureRequest input, VerifiedSignatureSigner signer, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        var replay = await db.Set<SignatureRequest>().SingleOrDefaultAsync(x => x.AgencyId == actor.AgencyId && x.IssuedByUserId == actor.UserId && x.ClientRequestId == input.ClientRequestId, ct);
        if (replay is not null)
        {
            if (replay.ReplacesRequestId != requestId || replay.SignerName != signer.Name.Trim() || replay.DeliveryEmail != signer.Email ||
                input.Pin != input.ConfirmPin || !input.IdentityConfirmed || !input.EmailConfirmed || !await pins.VerifyAsync(replay, input.Pin ?? "", ct)) throw Conflict();
            return await Dto(replay, ct);
        }
        var prior = await StaffRequest(actor, requestId, ct);
        Revision(prior, input.ExpectedRevision);
        if (prior.State == "Signed") throw Invalid("A signed document cannot be resent for a replacement signature. Prepare a new document if changes are needed.");
        if (await pins.VerifyAsync(prior, input.Pin ?? "", ct)) throw Invalid("Choose a different signing code for the replacement request.");
        var reason = Reason(input.Reason);
        if (SignatureRules.IsOpen(prior.State))
        {
            Close(prior, "Revoked", reason);
            Event(prior, "Replaced", "Staff", actor.UserId);
            await db.SaveChangesAsync(ct);
        }
        var frozen = await db.Set<FrozenSignatureDocument>().SingleAsync(x => x.Id == prior.FrozenDocumentId, ct);
        var created = await Issue(actor, new CreateSignatureRequest(input.ClientRequestId, prior.PersonId, frozen.DocumentArtifactId,
            Enum.Parse<SignerCapacity>(prior.SignerCapacity), prior.SignerContactId, input.Pin ?? "", input.ConfirmPin, input.IdentityConfirmed, input.EmailConfirmed, prior.AuthorityEvidence), signer, prior.Id, ct);
        return await Dto(created, ct);
    }, ct);

    public Task<IReadOnlyList<SignatureRequestDto>> ListAsync(SignatureActor actor, int personId, CancellationToken ct = default) => Atomic<IReadOnlyList<SignatureRequestDto>>(async () =>
    {
        Actor(actor);
        var requests = await db.Set<SignatureRequest>().AsNoTracking().Where(x => x.AgencyId == actor.AgencyId && x.PersonId == personId).OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);
        var results = new List<SignatureRequestDto>();
        foreach (var request in requests) results.Add(await Dto(request, ct));
        return results;
    }, ct);

    private Task<SignatureRequest> StaffRequest(SignatureActor actor, int id, CancellationToken ct) => db.Set<SignatureRequest>().SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == actor.AgencyId, ct)
        .ContinueWith(t => t.GetAwaiter().GetResult() ?? throw Unavailable(), ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public Task<SignatureRequestDto> RevokeAsync(SignatureActor actor, int requestId, SignatureReasonRequest input, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        var request = await StaffRequest(actor, requestId, ct);
        Revision(request, input.ExpectedRevision);
        if (!SignatureRules.IsOpen(request.State)) throw Conflict();
        Close(request, "Revoked", Reason(input.Reason));
        Event(request, "Revoked", "Staff", actor.UserId);
        await db.SaveChangesAsync(ct);
        return await Dto(request, ct);
    }, ct);

    public Task<SignatureRequestDto> WithdrawAuthorizationAsync(SignatureActor actor, int requestId, SignatureReasonRequest input, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        var request = await StaffRequest(actor, requestId, ct);
        Revision(request, input.ExpectedRevision);
        var frozen = await db.Set<FrozenSignatureDocument>().SingleAsync(x => x.Id == request.FrozenDocumentId, ct);
        var source = await db.Set<SignatureSourceDocument>().AsNoTracking().SingleAsync(x => x.Id == frozen.DocumentArtifactId, ct);
        if (request.State != "Signed" || request.AuthorizationRevokedAtUtc is not null || !Enum.TryParse<AnnualDocumentKind>(source.Kind, out var kind) || SignatureMeaningCatalog.Find(kind)?.Meaning != SignatureMeaning.Authorization)
            throw Invalid("Only a signed authorization can have its permission withdrawn here.");
        request.AuthorizationRevokedAtUtc = Now;
        request.AuthorizationRevocationReason = Reason(input.Reason);
        Event(request, "AuthorizationWithdrawn", "Staff", actor.UserId);
        await db.SaveChangesAsync(ct);
        return await Dto(request, ct);
    }, ct);

    public Task<byte[]> DownloadAsync(SignatureActor actor, int requestId, bool signed, CancellationToken ct = default) => Atomic(async () =>
    {
        Actor(actor);
        var request = await StaffRequest(actor, requestId, ct);
        return await ReadPdf(request, signed, ct);
    }, ct);

    public Task<SignatureAuthentication> AuthenticateAsync(string token, string pin, CancellationToken ct = default) => AuthenticateCore(token, pin, false, ct);
    public Task<SignatureAuthentication> AuthenticateReceiptAsync(string token, string pin, CancellationToken ct = default) => AuthenticateCore(token, pin, true, ct);
    private async Task<SignatureAuthentication> AuthenticateCore(string token, string pin, bool receipt, CancellationToken ct)
    {
        // Rejection is returned from the transaction so a failed attempt is committed before the neutral error.
        var result = await Atomic<SignatureAuthentication?>(async () =>
        {
            if (!SignatureSecrets.IsToken(token)) return null;
            var hash = SignatureSecrets.Hash(token);
            // Serialize attempts before verifying the secret, so parallel guesses cannot each spend the same remaining attempt.
            var requests = db.Database.IsSqlServer()
                ? db.Set<SignatureRequest>().FromSqlInterpolated($"SELECT * FROM dbo.SignatureRequests WITH (UPDLOCK, HOLDLOCK) WHERE TokenSha256 = {hash}")
                : db.Set<SignatureRequest>().Where(x => x.TokenSha256 == hash);
            var request = await requests.SingleOrDefaultAsync(ct);
            if (request is null || request.ExternalAccessRevokedAtUtc is not null || request.LockedAtUtc is not null || (receipt ? request.State != "Signed" || request.ExpiresAtUtc <= Now : !Usable(request))) return null;
            if (!receipt) await Current(request, ct);
            if (!await pins.VerifyAsync(request, pin ?? "", ct))
            {
                request.FailedPinAttempts++;
                if (request.FailedPinAttempts >= SigningPinRules.MaximumAttempts) { request.LockedAtUtc = Now; request.AuthenticationVersion++; }
                Event(request, request.LockedAtUtc is null ? "PinRejected" : "PinLocked", "Signer");
                await db.SaveChangesAsync(ct);
                return null;
            }
            var authenticatedAt = Now;
            if (request.ExpiresAtUtc <= authenticatedAt) return null;
            var secret = SignatureSecrets.NewToken();
            var session = Session(request, secret, receipt ? "Receipt" : "Signing", authenticatedAt);
            db.Add(session);
            await db.SaveChangesAsync(ct);
            if (!receipt) request.State = "Viewed";
            Event(request, receipt ? "ReceiptAuthenticated" : "Authenticated", "Signer", sessionId: session.Id);
            await db.SaveChangesAsync(ct);
            return new SignatureAuthentication(secret, session.ExpiresAtUtc);
        }, ct);
        return result ?? throw Unavailable();
    }

    private bool Usable(SignatureRequest request) => SignatureRules.IsOpen(request.State) && request.ExpiresAtUtc > Now;
    private static SignatureSession Session(SignatureRequest request, string token, string purpose, DateTime issuedAt)
    {
        var expiresAt = Earlier(issuedAt.AddMinutes(SignatureRules.SessionMinutes), request.ExpiresAtUtc);
        if (expiresAt <= issuedAt) throw Unavailable();
        return new() { AgencyId = request.AgencyId, RequestId = request.Id, TokenSha256 = SignatureSecrets.Hash(token),
            Purpose = purpose, AuthenticationVersion = request.AuthenticationVersion, IssuedAtUtc = issuedAt, ExpiresAtUtc = expiresAt };
    }
    private static DateTime Earlier(DateTime first, DateTime second) => first < second ? first : second;

    private async Task<(SignatureRequest Request, SignatureSession Session)> Authorized(string token, bool receipt, CancellationToken ct)
    {
        if (!SignatureSecrets.IsToken(token)) throw Unavailable();
        var hash = SignatureSecrets.Hash(token);
        var session = await db.Set<SignatureSession>().SingleOrDefaultAsync(x => x.TokenSha256 == hash, ct);
        if (session is null || session.ExpiresAtUtc <= Now) throw Unavailable();
        var request = await db.Set<SignatureRequest>().SingleAsync(x => x.Id == session.RequestId && x.AgencyId == session.AgencyId, ct);
        if (session.AuthenticationVersion != request.AuthenticationVersion || request.ExternalAccessRevokedAtUtc is not null || request.LockedAtUtc is not null ||
            (receipt ? session.Purpose != "Receipt" || request.State != "Signed" : session.Purpose != "Signing" || !Usable(request))) throw Unavailable();
        if (!receipt) await Current(request, ct);
        RequireCurrentDeadline(request, session, Now);
        return (request, session);
    }

    private static void RequireCurrentDeadline(SignatureRequest request, SignatureSession session, DateTime now)
    {
        if (now >= session.ExpiresAtUtc || now >= request.ExpiresAtUtc) throw Unavailable();
    }

    public Task<SignaturePortalDetails> DetailsAsync(string token, bool receipt = false, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, receipt, ct);
        var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleAsync(x => x.Id == request.FrozenDocumentId, ct);
        var source = await db.Set<SignatureSourceDocument>().AsNoTracking().SingleAsync(x => x.Id == frozen.DocumentArtifactId, ct);
        return new SignaturePortalDetails(request.SignerName, request.SignerCapacity, DocumentName(source.Kind), request.DisclosureVersion, request.DisclosureText, request.IntentText, request.State,
            await db.Set<SignatureConsent>().AnyAsync(x => x.SessionId == session.Id, ct), session.DocumentReleasedAtUtc is not null, session.AccessAcknowledgedAtUtc is not null,
            session.ExpiresAtUtc, request.ExpiresAtUtc, await db.Set<SignaturePackage>().AnyAsync(x => x.RequestId == request.Id, ct), SignatureSecrets.PageBinding(token));
    }, ct);

    public Task<byte[]> PortalDocumentAsync(string token, bool receipt = false, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, receipt, ct);
        var bytes = await ReadPdf(request, receipt, ct);
        var releasedAt = Now;
        RequireCurrentDeadline(request, session, releasedAt);
        if (!receipt && session.DocumentReleasedAtUtc is null)
        {
            session.DocumentReleasedAtUtc = releasedAt;
            session.Revision++;
            Event(request, "DocumentReleased", "Signer", sessionId: session.Id, occurredAt: releasedAt);
            await db.SaveChangesAsync(ct);
        }
        return bytes;
    }, ct);

    public Task<bool> ConsentAsync(string token, bool canAccessAndRetain, bool acceptsElectronicRecords, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, false, ct);
        if (!canAccessAndRetain || !acceptsElectronicRecords || session.DocumentReleasedAtUtc is null) throw Invalid("Open the PDF, confirm you can read and keep it, and choose whether to use electronic records.");
        var exists = await db.Set<SignatureConsent>().AnyAsync(x => x.SessionId == session.Id, ct);
        var acceptedAt = Now;
        RequireCurrentDeadline(request, session, acceptedAt);
        if (exists) return true;
        session.AccessAcknowledgedAtUtc = acceptedAt;
        session.Revision++;
        db.Add(new SignatureConsent { AgencyId = request.AgencyId, RequestId = request.Id, SessionId = session.Id, DisclosureVersion = request.DisclosureVersion, DisclosureText = request.DisclosureText, AcceptedAtUtc = acceptedAt });
        Event(request, "ElectronicConsent", "Signer", sessionId: session.Id, occurredAt: acceptedAt);
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task<SignatureAuthentication> CompleteAsync(string token, string typedName, bool agreesToIntent, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, false, ct);
        var consent = await db.Set<SignatureConsent>().SingleOrDefaultAsync(x => x.SessionId == session.Id && x.RequestId == request.Id && x.AgencyId == request.AgencyId, ct);
        if (consent is null || session.AccessAcknowledgedAtUtc is null || session.DocumentReleasedAtUtc is null || !agreesToIntent || typedName?.Length > 120 || !SignatureRules.NamesMatch(request.SignerName, typedName))
            throw Invalid("Review the document and electronic-record disclosure, confirm your agreement, and type your own name as recorded. Contact your case manager if it needs correction.");
        var signedAt = Now;
        // The consent read above can finish after a session deadline. Never persist a late
        // decision that the evidence-package validator must subsequently refuse.
        RequireCurrentDeadline(request, session, signedAt);
        db.Add(new SignatureCompletion { AgencyId = request.AgencyId, RequestId = request.Id, FrozenDocumentId = request.FrozenDocumentId, SessionId = session.Id, ConsentId = consent.Id,
            TypedSignerName = typedName!.Trim(), IntentText = request.IntentText, SignedAtUtc = signedAt });
        Close(request, "Signed", null, signedAt);
        Event(request, "Signed", "Signer", sessionId: session.Id, occurredAt: signedAt);
        var secret = SignatureSecrets.NewToken();
        var receipt = Session(request, secret, "Receipt", signedAt);
        db.Add(receipt);
        await db.SaveChangesAsync(ct);
        return new SignatureAuthentication(secret, receipt.ExpiresAtUtc);
    }, ct);

    public Task<bool> DecideAsync(string token, string decision, string reason, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, false, ct);
        var state = decision switch { "decline" => "Declined", "changes" => "ChangesRequested", "withdraw" => "Revoked", _ => throw Invalid("Choose a supported signing action.") };
        Close(request, state, Reason(reason));
        Event(request, decision == "withdraw" ? "ElectronicConsentWithdrawn" : state, "Signer", sessionId: session.Id);
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task<DateTime> ExtendSessionAsync(string token, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, false, ct);
        var extendedAt = Now;
        RequireCurrentDeadline(request, session, extendedAt);
        var expiry = Earlier(extendedAt.AddMinutes(SignatureRules.SessionMinutes), request.ExpiresAtUtc);
        if (expiry > session.ExpiresAtUtc)
        {
            session.ExpiresAtUtc = expiry;
            session.Revision++;
            Event(request, "SessionExtended", "Signer", sessionId: session.Id, occurredAt: extendedAt);
            await db.SaveChangesAsync(ct);
        }
        return session.ExpiresAtUtc;
    }, ct);

    public Task<bool> EndSessionAsync(string token, bool receipt, CancellationToken ct = default) => Atomic(async () =>
    {
        var (request, session) = await Authorized(token, receipt, ct);
        request.AuthenticationVersion++;
        Event(request, "SessionEnded", "Signer", sessionId: session.Id);
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private async Task<byte[]> ReadPdf(SignatureRequest request, bool signed, CancellationToken ct)
    {
        string path, hash; long length;
        if (signed)
        {
            var package = await db.Set<SignaturePackage>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId, ct)
                ?? throw new SignatureWorkflowException("signature_copy_preparing", "Your signed copy is being prepared. Please check again shortly or request a copy from your case manager.", 409);
            (path, hash, length) = (package.BlobPath, package.ContentSha256, package.ByteCount);
        }
        else
        {
            var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleAsync(x => x.Id == request.FrozenDocumentId && x.AgencyId == request.AgencyId, ct);
            (path, hash, length) = (frozen.BlobPath, frozen.ContentSha256, frozen.ByteCount);
        }
        var bytes = await blobs.ReadAsync(path, ct);
        if (bytes.LongLength != length || SignatureSecrets.Hash(bytes) != hash) throw new SignatureWorkflowException("signature_integrity_failed", "The retained document could not be verified. Please contact your case manager.", 503);
        return bytes;
    }

    private void Close(SignatureRequest request, string state, string? reason, DateTime? occurredAt = null)
    {
        request.State = state; request.TerminalReason = reason; request.CompletedAtUtc = occurredAt ?? Now; request.AuthenticationVersion++;
    }
    private void Event(SignatureRequest request, string kind, string actorKind, int? userId = null, long? sessionId = null, bool advance = true, object? detail = null, DateTime? occurredAt = null)
    {
        if (advance) request.Revision++;
        db.Add(new SignatureEvent { AgencyId = request.AgencyId, RequestId = request.Id, Sequence = request.Revision, Kind = kind, ActorKind = actorKind,
            ActorUserId = userId, SessionId = sessionId, OccurredAtUtc = occurredAt ?? Now, DetailJson = detail is null ? "{}" : JsonSerializer.Serialize(detail) });
    }
    private static string Reason(string? value) => string.IsNullOrWhiteSpace(value) || value.Length > 500 ? throw Invalid("Give a brief reason within 500 characters.") : value.Trim();
    private static void Revision(SignatureRequest request, long revision) { if (request.Revision != revision) throw Conflict(); }
    private static string DocumentName(string value) => Enum.TryParse<AnnualDocumentKind>(value, out var kind) ? SignatureMeaningCatalog.Find(kind)?.DisplayName ?? "Document" : "Document";
    private async Task<SignatureRequestDto> Dto(SignatureRequest request, CancellationToken ct)
    {
        var frozen = await db.Set<FrozenSignatureDocument>().AsNoTracking().SingleAsync(x => x.Id == request.FrozenDocumentId && x.AgencyId == request.AgencyId, ct);
        var source = await db.Set<SignatureSourceDocument>().AsNoTracking().SingleAsync(x => x.Id == frozen.DocumentArtifactId, ct);
        var events = await db.Set<SignatureEvent>().AsNoTracking().Where(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId).OrderByDescending(x => x.Sequence).Take(200)
            .Select(x => new SignatureEventDto(x.Sequence, x.Kind, x.ActorKind, x.OccurredAtUtc)).ToListAsync(ct);
        var delivery = await db.Set<SignatureOutbox>().AsNoTracking().Where(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId && x.Purpose == "Invitation")
            .OrderByDescending(x => x.Generation).Select(x => x.State).FirstOrDefaultAsync(ct) ?? "Pending";
        var receiptDelivery = await db.Set<SignatureOutbox>().AsNoTracking().Where(x => x.RequestId == request.Id && x.AgencyId == request.AgencyId && x.Purpose == "Receipt")
            .OrderByDescending(x => x.Generation).Select(x => x.State).FirstOrDefaultAsync(ct) ?? "NotQueued";
        return new SignatureRequestDto(request.Id, request.ClientRequestId, request.PersonId, frozen.DocumentArtifactId, DocumentName(source.Kind),
            Enum.TryParse<AnnualDocumentKind>(source.Kind, out var kind) ? SignatureMeaningCatalog.Find(kind)?.Meaning.ToString() ?? "None" : "None",
            request.SignerName, request.SignerCapacity, request.DeliveryEmail, Usable(request) ? request.State : SignatureRules.IsOpen(request.State) ? "Expired" : request.State,
            request.Revision, request.IssuedAtUtc, request.ExpiresAtUtc, request.FailedPinAttempts, request.LockedAtUtc is not null, delivery,
            await db.Set<SignaturePackage>().AnyAsync(x => x.RequestId == request.Id, ct), request.CompletedAtUtc, request.AuthorizationRevokedAtUtc, request.TerminalReason, events, request.SignerContactId,
            receiptDelivery, request.ExternalAccessRevokedAtUtc);
    }
}
