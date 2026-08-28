using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

public sealed class AdminTestDataDeletionViewRenderTests
{
    [Fact]
    public void AdminDashboardRendersAnAccessibleDestructiveTestConsumerAction()
    {
        WpfUiHarness.Run(() =>
        {
            var view = new AdminDashboardView();
            WpfUiHarness.Realize(view, 1400, 1000);

            var button = WpfUiHarness.FindByAutomationName<Button>(
                view,
                "Delete selected test consumer");

            Assert.Equal("Delete test consumer", button.Content);
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.True(button.ActualWidth > 0);
            Assert.True(button.ActualHeight > 0);
        });
    }
}
