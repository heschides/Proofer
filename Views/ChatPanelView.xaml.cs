using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Collections.Specialized;
using Sati.ViewModels.Children;

namespace Sati.Views;

public partial class ChatPanelView : UserControl
{
    private ChatViewModel? _viewModel;
    public ChatPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
        SizeChanged += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        LayoutRoot.Height = Math.Max(432, ActualHeight - 32);
        var compact = ActualWidth < 800;
        var grid = (Grid)RoomList.Parent;
        grid.ColumnDefinitions[0].MinWidth = compact ? 0 : 150;
        grid.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 220);
        grid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 8);
        RoomList.Visibility = RoomSplitter.Visibility = RoomHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactRoomPicker.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Attach()
    {
        Detach();
        _viewModel = DataContext as ChatViewModel;
        if (_viewModel is null) return;
        _viewModel.MessageSent += OnSent;
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void Detach()
    {
        if (_viewModel is null) return;
        _viewModel.MessageSent -= OnSent;
        _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        _viewModel = null;
    }

    private void OnSent(object? sender, EventArgs args)
    {
        if (!IsVisible) return;
        ComposeBox.Focus();
        if (_viewModel?.Messages.LastOrDefault() is { } message) MessageHistory.ScrollIntoView(message);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // Announce a neutral status only when focus is in this visible workspace.
        // No message narrative goes to a notification or a background live region.
        if (!IsVisible || !IsKeyboardFocusWithin || args.Action != NotifyCollectionChangedAction.Add) return;
        var peer = UIElementAutomationPeer.FromElement(ChatStatus) ?? UIElementAutomationPeer.CreatePeerForElement(ChatStatus);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent,
            "New messages in the selected chat room.", "chat-message");
    }
}
