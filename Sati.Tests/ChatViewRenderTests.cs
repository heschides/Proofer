using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sati.Models;
using Sati.ViewModels.Children;
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
            // The dock lists every room; the tab strip lists the ones opened from it.
            // Selecting a room is what opens its tab, so one selection drives both.
            var rooms = WpfUiHarness.FindByAutomationName<ListBox>(view, "Chat rooms");
            var roomTabs = WpfUiHarness.FindByAutomationName<TabControl>(view, "Open chat rooms");
            var transcriptTabs = WpfUiHarness.FindByAutomationName<TabControl>(view, "Transcript view");
            var compose = WpfUiHarness.FindByAutomationName<TextBox>(view, "Chat message draft");
            var history = WpfUiHarness.FindByAutomationName<ListBox>(view, "Chat message history");
            Assert.Equal(2, rooms.Items.Count);
            Assert.Same(fixture.ViewModel.Rooms[0], rooms.SelectedItem);
            Assert.Same(fixture.ViewModel.Rooms[0], roomTabs.SelectedItem);
            Assert.Equal(1, roomTabs.Items.Count);
            Assert.Equal(2, transcriptTabs.Items.Count);
            Assert.Equal("Latest", ((ChatTranscriptTab)transcriptTabs.SelectedItem).Name);
            // IsVisible needs a presentation source; the harness arranges without a window.
            Assert.Equal(Visibility.Visible, rooms.Visibility);
            Assert.Empty(WpfUiHarness.Descendants(view).OfType<GridSplitter>());
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

    /// <summary>
    /// The two conditional panels appear only in their own state. NullToVisibilityConverter
    /// reads "visible while not null" with no parameter and inverts with one, which is easy
    /// to write backwards; reversed, the empty-state card covers the open room's tab strip
    /// and the redaction panel offers to hide a message nobody has selected.
    /// </summary>
    [Fact]
    public void EmptyStateAndRedactionPanelAppearOnlyInTheirOwnState()
    {
        WpfUiHarness.Run(() =>
        {
            var fixture = new ChatViewModelTests.ChatFixture();
            fixture.Session.SetUser(User.Create(1, "super", "Example supervisor", "", "",
                UserRole.Supervisor, null, 1));
            fixture.Start();
            var view = new ChatPanelView { DataContext = fixture.ViewModel };
            WpfUiHarness.Realize(view, 1000, 700);

            // Nothing open: the invitation shows and no room pane exists to hide.
            var empty = WpfUiHarness.Descendants(view).OfType<TextBlock>()
                .Single(block => block.Text == "No room is open");
            Assert.True(IsShown(empty), "The empty state must show while no room is open.");

            fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
            WpfUiHarness.Realize(view, 1000, 700);
            Assert.False(IsShown(empty), "The empty state must not cover an open room's tab strip.");

            // A supervisor may redact, but only once a message is actually selected.
            var reason = WpfUiHarness.FindByAutomationName<TextBox>(view, "Reason to hide selected chat message");
            Assert.True(fixture.ViewModel.CanRedact);
            Assert.Null(fixture.ViewModel.SelectedMessage);
            Assert.False(IsShown(reason), "Redaction must stay hidden until a message is selected.");

            fixture.ViewModel.SelectedMessage = fixture.ViewModel.Messages[0];
            WpfUiHarness.Realize(view, 1000, 700);
            Assert.True(IsShown(reason), "Redaction must appear once a message is selected.");
            fixture.ViewModel.SuspendAndClear();
        });
    }

    /// <summary>Visible in its own right and not inside a collapsed ancestor.</summary>
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    /// <summary>
    /// The transcript distinguishes the signed-in author, another author, and a hidden
    /// message. Authorship is drawn from the server-side author id against the account,
    /// and a hidden message must never read as ordinary content.
    /// </summary>
    [Fact]
    public void TranscriptSeparatesOwnMessagesOtherAuthorsAndHiddenMessages()
    {
        WpfUiHarness.Run(() =>
        {
            var fixture = new ChatViewModelTests.ChatFixture();
            fixture.Start();
            fixture.Service.History = (_, _) => Task.FromResult(MixedTranscript());
            fixture.Service.Page = (_, after) => Task.FromResult(new Sati.Contracts.V1.ChatPageDto([], after, false, after));
            fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
            var view = new ChatPanelView { DataContext = fixture.ViewModel };
            WpfUiHarness.Realize(view, 1000, 700);

            var items = fixture.ViewModel.Messages;
            Assert.Equal(4, items.Count);
            Assert.True(items[0].IsOwnMessage, "Account 1 wrote the first post.");
            Assert.False(items[1].IsOwnMessage, "Account 7 wrote the second post.");
            Assert.Equal("You", items[0].AuthorLabel);
            Assert.Equal("Dana Riverstone", items[1].AuthorLabel);
            Assert.Equal("DR", items[1].Initials);
            Assert.True(items[3].IsRedacted);

            // Dana's second post lands a minute after the first, so it tucks under it.
            // The hidden post four minutes later still opens its own group.
            Assert.True(items[0].StartsGroup);
            Assert.True(items[1].StartsGroup, "A change of author always starts a group.");
            Assert.False(items[2].StartsGroup, "One author inside the window keeps the group.");
            Assert.True(items[3].StartsGroup, "A hidden message stands alone.");

            var history = WpfUiHarness.FindByAutomationName<ListBox>(view, "Chat message history");
            var blocks = WpfUiHarness.Descendants(history).OfType<TextBlock>().ToArray();
            Assert.Contains(blocks, block => block.Text == "You");
            Assert.Contains(blocks, block => block.Text == "Dana Riverstone");
            Assert.Contains(blocks, block => block.Text == "DR");

            // A hidden message is set apart by weight and colour, not by position alone.
            var hidden = Assert.Single(blocks, block => block.Text.StartsWith("Message hidden on"));
            Assert.Equal(FontStyles.Italic, hidden.FontStyle);
            var ordinary = Assert.Single(blocks, block => block.Text.StartsWith("Synthetic example: I can cover"));
            Assert.NotEqual(ordinary.Foreground, hidden.Foreground);
            Assert.Equal(FontStyles.Normal, ordinary.FontStyle);

            // Every row still announces its author and time even where the visible
            // byline is folded into the group above it.
            foreach (var item in items) Assert.Contains(item.Message.AuthorDisplayName, item.AccessibleName);

            var path = Path.Combine(Path.GetTempPath(), "sati-chat-preview-transcript.png");
            var image = new RenderTargetBitmap(1000, 700, 96, 96, PixelFormats.Pbgra32);
            image.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            fixture.ViewModel.SuspendAndClear();
        });
    }

    private static Sati.Contracts.V1.ChatPageDto MixedTranscript()
    {
        var start = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        Sati.Contracts.V1.ChatChangeDto Post(long sequence, int author, string name, int minutes, string? body, DateTime? redacted = null) =>
            new(sequence, "message", new(10 + sequence, 1, sequence, author, name, start.AddMinutes(minutes), body, redacted, redacted is null ? null : 1));
        return new(
        [
            Post(1, 1, "Example staff", 0, "Synthetic example: who can cover the morning meeting?"),
            Post(2, 7, "Dana Riverstone", 4, "Synthetic example: I can cover the morning meeting."),
            Post(3, 7, "Dana Riverstone", 5, "Synthetic example: I will bring the updated schedule."),
            Post(4, 7, "Dana Riverstone", 9, null, new DateTime(2026, 9, 5, 12, 40, 0, DateTimeKind.Utc)),
        ], 4, false, 4);
    }
}
