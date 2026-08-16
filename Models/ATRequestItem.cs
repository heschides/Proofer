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

        // Purchase URL. NOT rendered in the page-1 line-item listing (the state
        // form has no URL column) — it appears on the screenshot page, beside the
        // clip it belongs to. Nullable: items may have none.
        public string? Url { get; set; }

        // A pasted screenshot of the item as listed by the vendor: the evidence
        // that the price and product are what the request says they are.
        //
        // HEAVY COLUMN. Queue reads MUST project without it (see
        // ATRequestService.GetAllForUserAsync, which never materializes items at
        // all). Opening one request does load these, which is the point — the
        // editor shows thumbnails and the PDF embeds them.
        //
        // Unlike ATRequest.SnapshotPng, this has a public setter and no
        // first-write-wins rule. That blob is EVIDENCE of a published document
        // and must not be replaced; this is draft CONTENT the case manager is
        // still assembling, no different from the item's name or price. What
        // stops it changing after publication is the publication lock, not the
        // property. Size and format are gated by AtRequestScreenshot.
        public byte[]? ScreenshotPng { get; set; }

        public bool HasScreenshot => ScreenshotPng is { Length: > 0 };

        public decimal LineTotal => ItemCost * Quantity;

        public static ATRequestItem Rehydrate(int id)
        {
            var item = new ATRequestItem();
            item.Id = id;
            return item;
        }
    }
}
