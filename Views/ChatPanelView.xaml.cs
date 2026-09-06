using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Collections.Specialized;
using System.Windows.Media;
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

    /// <summary>
    /// The workspace is one column at every width, so the only thing left to size is the
    /// floor. Below it the outer scroller takes over rather than crushing the transcript
    /// and the composer into unusable slivers. 32 is the layout root's vertical margin.
    /// </summary>
    private void ApplyLayout() => LayoutRoot.Height = Math.Max(MinimumWorkspaceHeight, ActualHeight - 32);

    private const double MinimumWorkspaceHeight = 440;

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
        // The composer and the transcript live inside the selected room tab's template,
        // so they are rebuilt whenever the tab changes and cannot be held as fields.
        // They are located by the same automation names assistive technology uses.
        Find<TextBox>("Chat message draft")?.Focus();
        if (_viewModel?.Messages.LastOrDefault() is { } message)
            Find<ListBox>("Chat message history")?.ScrollIntoView(message);
    }

    /// <summary>The first realized descendant of that type carrying that automation name.</summary>
    private T? Find<T>(string automationName) where T : FrameworkElement =>
        Descendants(this).OfType<T>().FirstOrDefault(item => AutomationProperties.GetName(item) == automationName);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
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
