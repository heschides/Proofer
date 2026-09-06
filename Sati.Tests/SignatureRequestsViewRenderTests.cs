using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sati.Views.ClientDocuments;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class SignatureRequestsViewRenderTests
{
    [Theory]
    [InlineData(640)]
    [InlineData(1100)]
    public void SigningWorkspaceUsesNamedMaskedKeyboardInputs(int width)
    {
        WpfUiHarness.Run(() =>
        {
            var view = new SignatureRequestsWorkspace();
            WpfUiHarness.Realize(new ScrollViewer { Content = view }, width, 850);
            var pin = WpfUiHarness.FindByAutomationName<PasswordBox>(view, "New signing access code");
            var confirm = WpfUiHarness.FindByAutomationName<PasswordBox>(view, "Confirm new signing access code");
            Assert.Equal(12, pin.MaxLength); Assert.Equal(12, confirm.MaxLength);
            Assert.True(pin.Focusable); Assert.True(KeyboardNavigation.GetIsTabStop(pin));
            Assert.DoesNotContain(WpfUiHarness.Descendants(view).OfType<TextBox>(), box => box.Name.Contains("Pin", StringComparison.OrdinalIgnoreCase));
            Assert.True(WpfUiHarness.FindByAutomationName<ComboBox>(view, "Intended signer").Focusable);
        });
    }
}
