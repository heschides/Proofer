namespace Sati.Models.Billing
{
    public class ClaimLine
    {
        public int Id { get; set; }
        public int NoteId { get; set; }
        public int BillingPeriodId { get; set; }

        public DateTime DateOfService { get; set; }
        public string ProcedureCode { get; set; } = string.Empty;
        public string? ProcedureModifier { get; set; }
        public decimal? Units { get; set; }
        public decimal ChargeAmount { get; set; }
        public string ClientMaineCareId { get; set; } = string.Empty;
        public string RenderingProviderNpi { get; set; } = string.Empty;
        public string DiagnosisCode { get; set; } = string.Empty;
        public int PlaceOfService { get; set; }

        // Append-only financial snapshot used by 837 generation. Nullable only so legacy rows can
        // migrate safely; generation fails closed when a submitted line has no snapshot.
        public string? ClaimSnapshotJson { get; set; }

        public bool IsComplianceException { get; set; }
        public string? ComplianceExceptionReason { get; set; }

        // Server/local readiness projection for the billing submission preview. These values are
        // derived from the immutable snapshot and never become persistence columns.
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string ClientName { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public IReadOnlyList<string> ReadinessErrors { get; set; } = [];

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsReadyForSubmission => ReadinessErrors.Count == 0;

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string ReadinessStatus => IsReadyForSubmission ? "Ready" : "Needs correction";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string ReadinessSummary => IsReadyForSubmission
            ? "Ready"
            : string.Join("; ", ReadinessErrors);

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string ProcedureDisplay => string.IsNullOrWhiteSpace(ProcedureModifier)
            ? ProcedureCode
            : $"{ProcedureCode}-{ProcedureModifier}";

        public BillingPeriod BillingPeriod { get; set; } = null!;
        public Note Note { get; set; } = null!;
    }
}
