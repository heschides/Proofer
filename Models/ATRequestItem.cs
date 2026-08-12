namespace Sati.Models
{
    public class ATRequestItem
    {
        public int Id { get; private set; }

        public int ATRequestId { get; set; }
        public ATRequest? ATRequest { get; set; }

        public string? Name { get; set; }
        public decimal ItemCost { get; set; }
        public int Quantity { get; set; } = 1;

        // Purchase URL. Stored on the item now; consumed by the future
        // screenshots-with-clickable-links page. NOT rendered on the page-1 OADS
        // form (the state form has no URL column). Nullable — items may have none.
        public string? Url { get; set; }

        public decimal LineTotal => ItemCost * Quantity;

        public static ATRequestItem Rehydrate(int id)
        {
            var item = new ATRequestItem();
            item.Id = id;
            return item;
        }
    }
}
