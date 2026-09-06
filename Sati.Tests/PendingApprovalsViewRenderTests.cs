using System.Windows;
using System.Windows.Controls;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class PendingApprovalsViewRenderTests
{
    [Fact]
    public void FilterControlsShareOneBaselineAndHeight()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new PendingApprovalsView();
            WpfUiHarness.Realize(view, 1200, 500);

            FrameworkElement[] controls =
            [
                WpfUiHarness.FindByAutomationName<ComboBox>(view, "Filter by case manager"),
                WpfUiHarness.FindByAutomationName<ComboBox>(view, "Filter by client"),
                WpfUiHarness.FindByAutomationName<DatePicker>(view, "Filter from date"),
                WpfUiHarness.FindByAutomationName<DatePicker>(view, "Filter through date"),
                WpfUiHarness.FindByAutomationName<TextBox>(view, "Search notes"),
                WpfUiHarness.FindByAutomationName<Button>(view, "Apply note filters"),
                WpfUiHarness.FindByAutomationName<Button>(view, "Clear note filters")
            ];

            var baseline = controls[0].TranslatePoint(new Point(), view).Y;
            Assert.All(controls, control =>
            {
                Assert.InRange(control.ActualHeight, 35.5, 36.5);
                Assert.InRange(control.TranslatePoint(new Point(), view).Y, baseline - 0.5, baseline + 0.5);
            });
        });
    }
}
