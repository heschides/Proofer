using Sati.Helpers;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Sales tax on an AT request is calculated from the agency's configured rate
/// unless a case manager deliberately overrides it. The calculated figure is
/// STORED on the request rather than derived at render, because an AT request is
/// a document of record: changing the agency rate next year must not silently
/// restate a request filed this year.
/// </summary>
public sealed class SalesTaxTests
{
    private const decimal MaineRate = 0.055m;   // 5.5%

    // ATRequest's constructor is private on purpose — snapshot columns are written
    // only by the sanctioned factory — so tests build requests the same way the app
    // does.
    private static ATRequest NewRequest()
    {
        var person = Person.Rehydrate(1, 1);
        person.FirstName = "Test";
        person.LastName = "Client";
        var caseManager = User.Create(1, "cm", "Case Manager", string.Empty, string.Empty,
            UserRole.CaseManager, null, 1);
        return ATRequest.CreateForClient(person, caseManager);
    }

    private static ATRequestEditorViewModel NewEditor(decimal rate = MaineRate) =>
        new(NewRequest(), 0.15m, rate, [], null);

    private static void AddItem(ATRequestEditorViewModel editor, decimal cost, int quantity)
    {
        editor.AddItemCommand.Execute(null);
        var row = editor.Items[^1];
        row.ItemCost = cost;
        row.Quantity = quantity;
    }

    [Fact]
    public void CalculatorRoundsToCents()
    {
        // 117.96 * 0.055 = 6.4878 -> 6.49
        Assert.Equal(6.49m, ATRequestCalculator.SalesTax(117.96m, MaineRate));
        Assert.Equal(0m, ATRequestCalculator.SalesTax(0m, MaineRate));
    }

    [Fact]
    public void TaxIsCalculatedFromTheConfiguredRateAsItemsAreAdded()
    {
        var editor = NewEditor();

        AddItem(editor, 25.99m, 3);      // 77.97
        Assert.Equal(ATRequestCalculator.SalesTax(77.97m, MaineRate), editor.SalesTax);

        AddItem(editor, 39.99m, 1);      // 117.96
        Assert.Equal(117.96m, editor.ItemsTotal);
        Assert.Equal(6.49m, editor.SalesTax);
    }

    [Fact]
    public void OverrideStopsRecalculation()
    {
        var editor = NewEditor();
        AddItem(editor, 100m, 1);
        Assert.Equal(5.50m, editor.SalesTax);

        editor.SalesTaxOverridden = true;
        editor.SalesTax = 0m;            // vendor quoted tax-inclusive pricing

        AddItem(editor, 50m, 1);         // items change; the typed figure must survive
        Assert.Equal(150m, editor.ItemsTotal);
        Assert.Equal(0m, editor.SalesTax);
    }

    [Fact]
    public void ClearingTheOverrideRestoresTheCalculatedFigure()
    {
        var editor = NewEditor();
        AddItem(editor, 100m, 1);

        editor.SalesTaxOverridden = true;
        editor.SalesTax = 42m;
        Assert.Equal(42m, editor.SalesTax);

        editor.SalesTaxOverridden = false;
        Assert.Equal(5.50m, editor.SalesTax);
    }

    [Fact]
    public void TurningTheOverrideOnKeepsTheCalculatedValueAsAStartingPoint()
    {
        // The user edits from the calculated number rather than from an empty box.
        var editor = NewEditor();
        AddItem(editor, 100m, 1);

        editor.SalesTaxOverridden = true;

        Assert.Equal(5.50m, editor.SalesTax);
    }

    [Fact]
    public void AnOverriddenRequestIsNotRecalculatedWhenReopened()
    {
        // The filed amount must survive a later change to the agency's rate.
        var filed = NewRequest();
        filed.SalesTax = 42m;
        filed.SalesTaxOverridden = true;
        filed.Items.Add(new ATRequestItem { ItemCost = 100m, Quantity = 1 });

        var editor = new ATRequestEditorViewModel(filed, 0.15m, 0.09m, [], null);

        Assert.Equal(42m, editor.SalesTax);
    }

    [Fact]
    public void ASavedRequestKeepsItsFiledTaxEvenWhenNotOverridden()
    {
        // Id != 0 means this came back from the database. Recalculating on open
        // would restate a filed document if the agency rate had changed since.
        var filed = ATRequest.Rehydrate(77, 1, "Client", null, "CM", null, null, null,
            ATRequestStatus.Development);
        filed.SalesTax = 5.50m;
        filed.Items.Add(new ATRequestItem { ItemCost = 100m, Quantity = 1 });

        var editor = new ATRequestEditorViewModel(filed, 0.15m, 0.09m, [], null);

        Assert.Equal(5.50m, editor.SalesTax);
    }

    [Fact]
    public void PassthroughStillAppliesToTheTaxInclusiveSubtotal()
    {
        // Guards the interaction: the calculated tax must feed the passthrough,
        // and the totals must be published after the tax is recalculated.
        var editor = NewEditor();
        AddItem(editor, 100m, 1);

        Assert.Equal(5.50m, editor.SalesTax);
        Assert.Equal(ATRequestCalculator.Passthrough(100m, 5.50m, 0.15m), editor.PassthroughFee);
        Assert.Equal(ATRequestCalculator.Total(100m, 5.50m, 0.15m), editor.TotalCost);
    }
}
