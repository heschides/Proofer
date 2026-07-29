namespace Sati.Models
{
    // One line item on an ATRequest: a purchasable item with unit cost and
    // quantity. This is an editable grid row the user fills before submission,
    // so properties are mutable and construction is open — unlike identity
    // entities (Person, User) that gate birth through a factory. Id is private
    // set: assigned by EF, never by hand.
    public class ATRequestItem
    {
        public int Id { get; private set; }

        // FK + nav to the owning request. Nullable nav mirrors Person.User: EF
        // populates it; nothing builds a detached item in normal flow.
        public int ATRequestId { get; set; }
        public ATRequest? ATRequest { get; set; }

        public string? Name { get; set; }
        public decimal ItemCost { get; set; }
        public int Quantity { get; set; } = 1;

        // "Total for Line": unit cost × quantity. Get-only computed, so EF ignores
        // it by convention (same as Person.FullName) — never stored.
        public decimal LineTotal => ItemCost * Quantity;
    }
}