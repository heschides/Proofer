using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;

namespace Sati.Models
{
    // An Authorized Payment (AT) request: the app-side record behind the OADS
    // "Authorized Payment Information Form." Always tied to one client.
    //
    // SNAPSHOT SEMANTICS (deliberate departure from Sati's compute-don't-store
    // default): the client and case-manager fields are COPIED onto this record
    // at creation, not pulled live at render. A payment request is a document of
    // record — it must re-render months later exactly as submitted, even if the
    // client's name or the CM's phone number has since changed. Live sources
    // (Person.EvergreenId, User.Email/Phone, etc.) exist only to populate these
    // at birth. See CreateForClient.
    public class ATRequest
    {
        public int Id { get; private set; }
        public int Revision { get; set; } = 1;

        // Live link to the client, kept for navigation/filtering. The snapshot
        // columns below are what actually print — this FK is NOT the render
        // source, by design.
        public int PersonId { get; set; }
        public Person? Person { get; set; }

        // ---- Snapshot: client (frozen at creation) ----
        public string? ClientName { get; private set; }
        public string? ClientEvergreenId { get; private set; }

        // ---- Snapshot: case manager (frozen at creation) ----
        public string? CaseManagerName { get; private set; }
        public string? CaseManagerEmail { get; private set; }
        public string? CaseManagerPhone { get; private set; }
        public string? CaseManagerAgency { get; private set; }

        // ---- Request entry (mutable until submitted; user-filled) ----
        public string? VendorName { get; set; }
        public string? VendorBillingLocation { get; set; }
        public string? VendorProgramContact { get; set; }
        public string? VendorBillingContact { get; set; }
        // Sales tax is normally derived from the agency's configured rate and the
        // items subtotal, but the stored value is still the AMOUNT, not the rate:
        // an AT request is a document of record and must re-render months later
        // exactly as filed, even after the agency changes its rate.
        public decimal SalesTax { get; set; }

        // The passthrough rate this request was PUBLISHED under, frozen at
        // publication. Null while the request is a draft, where the live agency
        // rate is the right one to show.
        //
        // Sales tax freezes as an amount because it is entered; the passthrough
        // rate freezes as a RATE because the document prints it — "Passthrough fee
        // (15.00%)" — and a stored amount alone could not reproduce that label.
        // Both halves of the line have to come from the record, or regenerating a
        // filed request after the agency renegotiates its rate would quietly
        // restate money somebody already attested to.
        public decimal? PassthroughRate { get; private set; }

        // The rate to apply: the frozen one once published, the caller's current
        // agency rate while still a draft.
        public decimal EffectivePassthroughRate(decimal currentRate) => PassthroughRate ?? currentRate;

        // True when a case manager has deliberately typed a figure that differs
        // from the calculated one — a vendor quoting tax-inclusive pricing, an
        // exempt item, an out-of-state vendor. While false, editing the items
        // recalculates the tax; while true, the typed amount is left alone.
        public bool SalesTaxOverridden { get; set; }

        // Manual for now. When status transitions become gated (OADS path), this
        // gets stamped inside SetStatus instead of set by hand.
        public DateTime? SubmittedDate { get; set; }
        public DateTime? DecisionDate { get; set; }

        // ---- Status: single sanctioned writer ----
        public ATRequestStatus Status { get; private set; } = ATRequestStatus.Development;

        // ---- Attestation: written only by Publish / cleared only by ReopenForCorrection ----
        // Who published this request, and when. Captured from the authenticated
        // session rather than typed, so the recorded name cannot disagree with the
        // account that performed the act. Null until published.
        //
        // The statement is frozen alongside them on purpose: AtRequestPublication
        // owns the current wording, but a request published last year must keep
        // rendering the wording its signer actually read.
        public string? SignedByName { get; private set; }
        public string? SignedByRole { get; private set; }
        public int? SignedByUserId { get; private set; }
        public DateTime? SignedAtUtc { get; private set; }
        public string? AttestationStatement { get; private set; }

        // The lock. Delegates to the shared rule owner rather than testing a field
        // here, so the desktop and the API cannot disagree about what "published"
        // means. See AtRequestPublication.
        public bool IsPublished => AtRequestPublication.IsPublished(SignedByName, SignedAtUtc);

        // On-file evidence: a rasterized PNG of the form as first exported. The
        // canonical PDF is never stored (regenerated on demand); this is the
        // glance-able proof-of-document, viewable inline. Null until first export.
        // private set — written only through AttachSnapshot, never scribbled on
        // directly. HEAVY COLUMN: service reads MUST project without it and fetch
        // it separately (GetSnapshotAsync), or every queue load drags the blobs.
        public byte[]? SnapshotPng { get; private set; }

        public List<ATRequestItem> Items { get; set; } = [];

        // Get-only computed → EF ignores it (same convention as Person.FullName
        // and ATRequestItem.LineTotal). Never stored; always derived. The pre-tax,
        // pre-passthrough sum of the line items — the raw input the calculator
        // needs, and a meaningful figure in its own right.
        public decimal ItemsTotal => Items.Sum(i => i.LineTotal);

        // Money math delegates to ATRequestCalculator — the single owner — rather
        // than inlining the formula here. These take the rate because the entity
        // can't see Settings; callers pass Settings.PassthroughRate. Methods, not
        // properties, precisely BECAUSE they need an argument: a parameterless
        // TotalCost would have to reach for global state to find the rate, which
        // is the coupling we're avoiding.
        public decimal PassthroughFee(decimal rate) => ATRequestCalculator.Passthrough(ItemsTotal, SalesTax, rate);
        public decimal TotalCost(decimal rate) => ATRequestCalculator.Total(ItemsTotal, SalesTax, rate);

        // EF materialization ctor.
        protected ATRequest() { }

        public static ATRequest Rehydrate(int id, int personId, string? clientName, string? clientEvergreenId,
            string? caseManagerName, string? caseManagerEmail, string? caseManagerPhone, string? caseManagerAgency,
            ATRequestStatus status)
        {
            return new ATRequest
            {
                Id = id, PersonId = personId, ClientName = clientName, ClientEvergreenId = clientEvergreenId,
                CaseManagerName = caseManagerName, CaseManagerEmail = caseManagerEmail,
                CaseManagerPhone = caseManagerPhone, CaseManagerAgency = caseManagerAgency, Status = status
            };
        }

        // Adopt the key the server assigned. Needed when a request is created and
        // published in one operation over HTTP: the caller's instance is still
        // Id 0 while the saved record has a real key, and the open editor must end
        // up pointing at the saved one. No-op once an Id is set — reassigning an
        // existing key would silently repoint the editor at a different record.
        public void RehydrateIdentity(int id)
        {
            if (Id == 0)
                Id = id;
        }

        // Rehydration of the attestation block, for the mappers that rebuild a
        // request from a DTO or a stored row. Separate from Rehydrate so the
        // common case does not carry five more positional arguments, and so the
        // one place that reconstructs a signature is easy to find.
        public void RehydrateAttestation(
            string? signedByName, string? signedByRole, int? signedByUserId,
            DateTime? signedAtUtc, string? attestationStatement, decimal? passthroughRate)
        {
            SignedByName = signedByName;
            SignedByRole = signedByRole;
            SignedByUserId = signedByUserId;
            SignedAtUtc = signedAtUtc;
            AttestationStatement = attestationStatement;
            PassthroughRate = passthroughRate;
        }

        // Factory: takes the live client + CM and freezes their fields onto the
        // new request. This is the ONLY place snapshot columns are written — they
        // have private set precisely so nothing else can. New requests start in
        // Development with an empty item grid the user fills in.
        public static ATRequest CreateForClient(Person person, User caseManager)
        {
            return new ATRequest
            {
                PersonId = person.Id,
                ClientName = person.FullName,
                ClientEvergreenId = person.EvergreenId,
                CaseManagerName = caseManager.DisplayName,
                CaseManagerEmail = caseManager.Email,
                CaseManagerPhone = caseManager.Phone,
                CaseManagerAgency = caseManager.Agency?.Name
            };
        }

        // The single sanctioned writer of Status — see note above. Gated only for
        // publication: a published request's status may still advance through the
        // OADS decision path (Review -> Approved/Denied/Appeal/Received), which is
        // the reviewer's act, not an edit to what the case manager attested to.
        public void SetStatus(ATRequestStatus status)
        {
            Status = status;
        }

        // Publish: the case manager's attestation, and the point the request stops
        // being a draft. Takes the acting user rather than a typed name so the
        // recorded signer is the authenticated session, and takes the clock as an
        // argument so tests are not at the mercy of DateTime.UtcNow.
        //
        // Callers persist afterwards; this only mutates the in-memory record. The
        // lock is enforced where it has to be — in the services, against the STORED
        // row — because an in-memory guard protects nothing from a second client.
        public void Publish(User caseManager, DateTime signedAtUtc, decimal passthroughRate)
        {
            if (IsPublished)
                throw new InvalidOperationException("This AT request has already been published.");

            // Frozen here, alongside the signature, because this is the moment the
            // figures stop being a working estimate and become what was attested to.
            PassthroughRate = passthroughRate;
            SignedByName = caseManager.DisplayName;
            SignedByRole = caseManager.Role.ToString();
            SignedByUserId = caseManager.Id;
            SignedAtUtc = signedAtUtc;
            AttestationStatement = AtRequestPublication.AttestationStatement;

            // The date the request left the case manager's hands. Previously set by
            // hand; publication is now the event that stamps it.
            SubmittedDate = signedAtUtc.Date;
            SetStatus(ATRequestStatus.Review);
        }

        // Reopen a published request for correction. Discards the attestation —
        // deliberately, because the alternative is a signature that stays attached
        // while the document beneath it changes.
        //
        // The previously exported PDF is not retained — deliberately. While a
        // request stays published it cannot change, so the document is a pure
        // function of this record and regenerating it reproduces exactly what was
        // attested to. Reopening ends that: the request becomes editable again,
        // and the earlier version is gone apart from SnapshotPng, which keeps the
        // form as FIRST published. See "The PDF is regenerated, never retained"
        // in DECISIONS.md.
        public void ReopenForCorrection()
        {
            if (!IsPublished)
                throw new InvalidOperationException("This AT request has not been published.");

            SignedByName = null;
            SignedByRole = null;
            SignedByUserId = null;
            SignedAtUtc = null;
            AttestationStatement = null;
            SubmittedDate = null;
            // Back to a draft, so the live agency rate governs again. Keeping the
            // old frozen rate would silently apply last year's terms to a request
            // being rewritten today.
            PassthroughRate = null;
            SetStatus(ATRequestStatus.Development);
        }

        // The sole writer of SnapshotPng — the reason that property has a private
        // set. First export wins: the snapshot is evidence of the document as
        // first published, so a later regeneration must not quietly replace it.
        public void AttachSnapshot(byte[] png)
        {
            ArgumentNullException.ThrowIfNull(png);
            SnapshotPng ??= png;
        }
    }
}
