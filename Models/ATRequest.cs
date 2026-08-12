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
        public decimal SalesTax { get; set; }

        // Manual for now. When status transitions become gated (OADS path), this
        // gets stamped inside SetStatus instead of set by hand.
        public DateTime? SubmittedDate { get; set; }
        public DateTime? DecisionDate { get; set; }

        // ---- Status: single sanctioned writer, ungated today ----
        public ATRequestStatus Status { get; private set; } = ATRequestStatus.Development;

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

        // The single sanctioned writer of Status — see note above. Ungated today;
        // the future OADS version adds permission + signature logic here alone.
        public void SetStatus(ATRequestStatus status)
        {
            Status = status;
        }
    }
}
