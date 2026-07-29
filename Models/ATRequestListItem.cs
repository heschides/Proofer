namespace Sati.Models
{
    // Read-model for the AT request queue. NOT an entity — it's the projected
    // shape of a list row, deliberately excluding the SnapshotPng blob so the
    // queue query never drags binary data over the wire (the whole point of the
    // metadata/detail split). TotalCost is computed in SQL at projection time;
    // HasSnapshot is a cheap != null bool, so the row knows evidence exists
    // without loading it. Immutable record: a read row is never mutated.
    public record ATRequestListItem
    {
        public int Id { get; init; }
        public string? ClientName { get; init; }
        public ATRequestStatus Status { get; init; }
        public decimal TotalCost { get; init; }
        public DateTime? SubmittedDate { get; init; }
        public string? VendorName { get; init; }
        public string? CaseManagerName { get; init; }   // frozen submitter — survives client transfer
        public bool HasSnapshot { get; init; }
    }
}