namespace Sati.Models
{
    public class Form
    {
        public int Id { get; set; }
        public FormType Type { get; set; }
        public DateTime DueDate { get; set; }
        public Person Person { get; set; } = null!;
        public int PersonId { get; set; }
        public DateTime? CompletedDate { get; private set; }
        public DateTime? OpenedDate { get; set; }
        public List<FormAttestation> Attestations { get; private set; } = [];

        // Compliance IS the completion date. It used to be a separate stored column,
        // and the two could disagree: 147 rows in SatiProduction held IsCompliant = 1
        // with no date. Every screen reads this property, while BillingComplianceGate
        // reads CompletedDate alone, so those rows rendered complete and blocked
        // billing at the same time — indistinguishable, to the person looking at it,
        // from the duplicate-row defect that shared the symptom.
        //
        // Two writers kept in step by convention is a rule with no owner. Deriving it
        // means there is one fact, so there is nothing left to disagree.
        public bool IsCompliant => CompletedDate.HasValue;

        /// <summary>
        /// Whether this document is satisfied *as of* a date — a completion date is
        /// recorded and it has arrived.
        ///
        /// This is not the same question as <see cref="IsCompliant"/>, and conflating
        /// them is the second way these two readers drifted apart. A form completed on
        /// a date that has not arrived yet holds a real record (IsCompliant) while not
        /// yet being in force (this). BillingComplianceGate has always decided
        /// billing on the second question, so anything whose answer depends on today —
        /// overdue colouring, late-review events, task rows — has to ask this one, or
        /// it will disagree with the gate on exactly those rows.
        ///
        /// Same predicate as the completion half of
        /// BillingComplianceGate.IsIncompleteAndOverdue, stated once here so the
        /// desktop readers cannot express it a second, subtly different way.
        /// </summary>
        public bool IsSatisfiedAsOf(DateTime asOf) =>
            CompletedDate is DateTime completed && completed.Date <= asOf.Date;

        // EF Core needs a parameterless constructor to materialize entities from the
        // database; it's protected so application code can't bypass the constructor
        // below.
        protected Form() { }

        /// <summary>
        /// Creates a form. <paramref name="completedOn"/> is the completion date if
        /// this document is already satisfied at creation — an annual document in
        /// force when its cycle began — and null if it is outstanding.
        ///
        /// It takes a date rather than a bool precisely because the bool was the
        /// defect: "compliant, date unknown" was expressible, and it is the one state
        /// the gate cannot act on. A caller that believes a document is in force must
        /// now say since when.
        /// </summary>
        public Form(FormType type, DateTime dueDate, DateTime? completedOn = null)
        {
            Type = type;
            DueDate = dueDate;
            CompletedDate = completedOn;
        }

        // The named doors for changing completion state. CompletedDate has a private
        // setter so these stay the only way in, but they no longer carry an invariant
        // between two fields — there is only one field. What they still buy is a
        // greppable list of everywhere completion is decided.
        //
        // Attest takes a ledger row whose date the caller explicitly captured. It is
        // not synthesized here, because the late-vs-on-time distinction
        // (CompletedDate vs DueDate) is a billing fact the entity has no business
        // guessing.
        public void Attest(FormAttestation attestation)
        {
            ArgumentNullException.ThrowIfNull(attestation);
            if (attestation.Kind != FormAttestationKind.Attested ||
                attestation.CompletedOn is not DateTime completedOn)
            {
                throw new ArgumentException("An attestation row is required.", nameof(attestation));
            }

            Attestations.Add(attestation);
            MarkComplete(completedOn);
        }

        public void RevokeAttestation(FormAttestation revocation)
        {
            ArgumentNullException.ThrowIfNull(revocation);
            if (revocation.Kind != FormAttestationKind.Revoked)
                throw new ArgumentException("A revocation row is required.", nameof(revocation));

            Attestations.Add(revocation);
            Reset();
        }

        /// <summary>
        /// Adjusts the admission-time assumption before a new Person graph has ever
        /// been persisted. Existing forms cannot use this seam; they require an
        /// append-only attestation or revocation.
        /// </summary>
        public void SetInitialCompletion(DateTime? completedOn)
        {
            if (Id != 0 || Attestations.Count != 0)
                throw new InvalidOperationException(
                    "Only a new, unattested form can change its initial completion assumption.");
            CompletedDate = completedOn?.Date;
        }

        private void MarkComplete(DateTime completedOn) => CompletedDate = completedOn.Date;
        private void Reset() => CompletedDate = null;
    }
}
