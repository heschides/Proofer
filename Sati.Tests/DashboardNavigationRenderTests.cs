using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class DashboardNavigationRenderTests
{
    [Fact]
    public void RequestedDocumentDestinationsRenderAsKeyboardOperableTabs()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new CaseManagerDashboardView();
            WpfUiHarness.Realize(view, 1400, 900);

            foreach (var name in new[]
                     {
                         "Assistive technology requests",
                         "DHHS authorized representative form",
                         "Release forms"
                     })
            {
                var button = WpfUiHarness.FindByAutomationName<Button>(view, name);
                Assert.Equal(Visibility.Visible, button.Visibility);
                Assert.True(button.IsEnabled);
                Assert.True(button.Focusable);
                Assert.True(System.Windows.Input.KeyboardNavigation.GetIsTabStop(button));
            }

        });
    }
}
