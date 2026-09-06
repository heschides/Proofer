using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ChatViewRenderTests
{
    [Theory]
    [InlineData(900, 650)]
    [InlineData(1400, 900)]
    [InlineData(640, 480)]
    public void ChatRendersThemedKeyboardOperableControlsWithoutOpeningAConnection(double width, double height)
    {
        WpfUiHarness.Run(() =>
        {
            var fixture = new ChatViewModelTests.ChatFixture();
            fixture.Service.Rooms = [fixture.Service.Rooms[0] with { PersonId = 37, ConsumerDisplayName = "Morgan Avery Example" }, fixture.Service.Rooms[1]];
            fixture.Start();
            fixture.Service.History = (_, _) => Task.FromResult(ChatViewModelTests.Page(1,
                string.Join(" ", Enumerable.Repeat("Synthetic example: Please review tomorrow's team schedule and confirm which staff can cover the morning meeting.", 8))));
            fixture.Service.Page = (_, after) => Task.FromResult(new Sati.Contracts.V1.ChatPageDto([], after, false, after));
            fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
            var view = new ChatPanelView { DataContext = fixture.ViewModel };
            WpfUiHarness.Realize(view, width, height);
            var rooms = WpfUiHarness.FindByAutomationName<ListBox>(view, "Chat rooms");
            var compose = WpfUiHarness.FindByAutomationName<TextBox>(view, "Chat message draft");
            var history = WpfUiHarness.FindByAutomationName<ListBox>(view, "Chat message history");
            Assert.Equal(2, rooms.Items.Count);
            Assert.Single(history.Items);
            var messageBody = Assert.Single(WpfUiHarness.Descendants(history).OfType<TextBlock>(), block => block.Text.StartsWith("Synthetic example:"));
            Assert.Equal(TextWrapping.Wrap, messageBody.TextWrapping);
            Assert.True(messageBody.ActualHeight > 45, $"Long message body must wrap over multiple lines, height: {messageBody.ActualHeight}");
            Assert.True(messageBody.ActualWidth <= history.ActualWidth, "Message text must fit within the visible history width.");
            Assert.Contains("Morgan Avery Example", fixture.ViewModel.RoomNotice);
            Assert.Contains("record 37", fixture.ViewModel.RoomNotice);
            Assert.Equal(4000, compose.MaxLength);
            Assert.True(compose.IsEnabled);
            Assert.True(compose.Focusable);
            Assert.True(System.Windows.Input.KeyboardNavigation.GetIsTabStop(compose));
            Assert.True(compose.ActualWidth > 200);
            Assert.True(history.ActualHeight > 45, $"Message history height: {history.ActualHeight}");
            var composeOrigin = compose.TranslatePoint(new Point(), view);
            Assert.True(composeOrigin.Y >= 0 && composeOrigin.Y + compose.ActualHeight <= height);
            var path = Path.Combine(Path.GetTempPath(), $"sati-chat-preview-{width:0}.png");
            var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            image.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(WpfUiHarness.FindByAutomationName<TextBlock>(view, "Chat status")));
            fixture.ViewModel.SuspendAndClear();
        });
    }
}
