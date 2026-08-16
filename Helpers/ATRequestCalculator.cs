using Sati.Models;

namespace Sati.Helpers
{
    // Single owner of AT payment-request money math — the FormDueDateCalculator
    // pattern applied to currency. Every place that needs a passthrough fee or a
    // grand total routes through here: the ATRequest entity's methods, the
    // service's queue projection (as a hand-mirrored SQL expression, since EF
    // can't translate a method call), and the PDF/PNG render. If the formula
    // ever changes, it changes here and the SQL mirror — nowhere else.
    //
    // THE RULE THAT BITES: the 15% passthrough applies to the TAX-INCLUSIVE
    // subtotal, not the pre-tax amount. The paper form's row ordering (fee listed
    // above Sales Tax) implies the opposite; it lies. Fee = (subtotal + tax) * rate.
    //
    //   itemsSubtotal = Σ (ItemCost × Quantity)
    //   taxed         = itemsSubtotal + salesTax
    //   passthrough   = taxed × rate
    //   total         = taxed + passthrough  ==  taxed × (1 + rate)
    //
    // rate is a fraction (0.15 = 15%), sourced from Settings.PassthroughRate.
    public static class ATRequestCalculator
    {
        // Sales tax on the pre-passthrough items subtotal, at the agency's
        // configured rate (0.055 = 5.5%, Maine's general rate). Rounded to cents
        // here rather than at render, because the result is stored on the request
        // as the filed amount. A case manager may override it; this is only the
        // default, and the override is recorded on the request itself.
        public static decimal SalesTax(decimal itemsSubtotal, decimal rate)
            => Math.Round(itemsSubtotal * rate, 2, MidpointRounding.AwayFromZero);

        // The agency's cut: 15% of the tax-inclusive subtotal.
        public static decimal Passthrough(decimal itemsSubtotal, decimal salesTax, decimal rate)
            => (itemsSubtotal + salesTax) * rate;

        // Grand total the vendor/agency is paid: tax-inclusive subtotal plus the
        // passthrough on top. Algebraically (subtotal + tax) × (1 + rate) — the
        // form multiplied out — but written as taxed + Passthrough(...) so the
        // two public methods can't drift from each other.
        public static decimal Total(decimal itemsSubtotal, decimal salesTax, decimal rate)
        {
            var taxed = itemsSubtotal + salesTax;
            return taxed + Passthrough(itemsSubtotal, salesTax, rate);
        }
    }
}