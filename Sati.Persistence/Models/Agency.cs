namespace Sati.Models
{
    public class Agency
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Npi {  get; set; }
        public string? TaxId { get; set; }
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? BillingProcedureCode { get; set; }
        public string? BillingModifier { get; set; }
        public decimal? BillingUnitRate { get; set; }
        public string? EdiSubmitterId { get; set; }
        public string? EdiPayerName { get; set; }
        public string? EdiPayerId { get; set; }
        public string? EdiContactName { get; set; }
        public string? EdiContactPhone { get; set; }
    }
}
