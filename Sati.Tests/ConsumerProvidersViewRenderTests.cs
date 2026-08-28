using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The consumer profile's provider panel loaded for real, with a DataContext.
/// <para>
/// The feature-view smoke test in <see cref="StabilizationTests"/> already proves
/// <see cref="ClientsView"/> parses and lays out, but it runs with no DataContext, so no binding
/// is exercised. That matters most for the two per-row command bindings, which reach the view
/// model through <c>RelativeSource AncestorType=ItemsControl</c>: name the wrong ancestor or the
/// wrong property and WPF logs a trace and leaves the button inert. Nothing fails, nothing
/// throws, and the End button simply never works.
/// </para>
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ConsumerProvidersViewRenderTests
{
    [Fact]
    public async Task EachProviderRowsEndAndRemoveButtonsReachTheViewModelCommands()
    {
        var panel = await LoadedPanelAsync();

        RenderProfile(panel, view =>
        {
            var list = WpfUiHarness.FindByAutomationName<ItemsControl>(view, "Current medical providers");
            var buttons = WpfUiHarness.Descendants(list).OfType<Button>().ToList();
            var end = buttons.Single(button => Equals(button.Content, "End"));
            var remove = buttons.Single(button => Equals(button.Content, "Remove"));

            // The command has to be the view model's own, not merely non-null: a binding that
            // resolved to some other command would still leave a button that looks wired.
            Assert.Same(panel.EndProviderCommand, end.Command);
            Assert.Same(panel.RemoveProviderCommand, remove.Command);

            // And the row travels with it, or every button would act on whichever row the
            // view model happened to consider current.
            var row = Assert.Single(panel.Current);
            Assert.Same(row, end.CommandParameter);
            Assert.Same(row, remove.CommandParameter);
        });
    }

    [Fact]
    public async Task TheAddButtonIsInertUntilAProviderIsChosen()
    {
        var panel = await LoadedPanelAsync();

        RenderProfile(panel, view =>
        {
            var add = WpfUiHarness.FindByAutomationName<Button>(
                view, "Add this provider to the consumer");

            Assert.False(add.IsEnabled);

            panel.NewProviderId = 4;
            WpfUiHarness.Realize(view);

            Assert.True(add.IsEnabled);
        });
    }

    [Fact]
    public async Task TheDerivedAffiliationIsRenderedAndIsNotEditable()
    {
        var panel = await LoadedPanelAsync();

        RenderProfile(panel, view =>
        {
            var list = WpfUiHarness.FindByAutomationName<ItemsControl>(view, "Current medical providers");
            var texts = WpfUiHarness.Descendants(list).OfType<TextBlock>()
                .Select(block => block.Text).ToList();

            Assert.Contains("Coastal Women's Healthcare · MaineHealth", texts);
            // Rendered as text, never as an input: an editable derived value is a stored copy
            // in disguise, and the point of deriving is that it cannot drift.
            Assert.Empty(WpfUiHarness.Descendants(list).OfType<TextBox>());
        });
    }

    [Fact]
    public async Task PastProvidersAreCollapsedButPresentAndReachableByKeyboard()
    {
        var panel = await LoadedPanelAsync(includeEnded: true);

        RenderProfile(panel, view =>
        {
            var disclosure = WpfUiHarness.FindByAutomationName<Expander>(view, "Show past providers");

            Assert.Equal(Visibility.Visible, disclosure.Visibility);
            Assert.False(disclosure.IsExpanded);
            // An Expander is focusable and toggles from the keyboard; a click-only affordance
            // would put the past list out of reach without a mouse.
            Assert.True(disclosure.Focusable);
            Assert.Equal("1 past provider", disclosure.Header);
        });
    }

    [Fact]
    public async Task ARefusalIsAnnouncedRatherThanOnlyColoured()
    {
        var panel = await LoadedPanelAsync(saveRefusal: "That provider is already recorded.");

        RenderProfile(panel, view =>
        {
            var message = WpfUiHarness.FindByAutomationName<TextBlock>(view, "Provider list message");

            Assert.Equal(Visibility.Collapsed, message.Visibility);

            panel.StatusMessage = "That provider is already recorded.";
            WpfUiHarness.Realize(view);

            Assert.Equal(Visibility.Visible, message.Visibility);
            Assert.Equal("That provider is already recorded.", message.Text);
            Assert.Equal(
                AutomationLiveSetting.Assertive,
                AutomationProperties.GetLiveSetting(message));
        });
    }

    [Fact]
    public async Task TheFreeTextLinkingPromptIsOfferedWithAWorkingCommand()
    {
        var panel = await LoadedPanelAsync(legacyPrimaryCare: "Dr. Reed", withCurrentLink: false);

        RenderProfile(panel, view =>
        {
            var prompt = WpfUiHarness.FindByAutomationName<Border>(
                view, "Provider still recorded as free text");
            var link = WpfUiHarness.FindByAutomationName<Button>(
                view, "Link this free-text provider to the directory");

            Assert.Equal(Visibility.Visible, prompt.Visibility);
            Assert.Equal(Visibility.Visible, link.Visibility);
            Assert.Same(panel.LinkLegacyPrimaryCareCommand, link.Command);
            Assert.Equal("Link Dr. Reed", link.Content);
        });
    }

    [Fact]
    public async Task AnAmbiguousOrMissingNameExplainsItselfWithoutOfferingAOneClickLink()
    {
        var panel = await LoadedPanelAsync(legacyPrimaryCare: "Dr. Nobody", withCurrentLink: false);

        RenderProfile(panel, view =>
        {
            var prompt = WpfUiHarness.FindByAutomationName<Border>(
                view, "Provider still recorded as free text");
            var link = WpfUiHarness.FindByAutomationName<Button>(
                view, "Link this free-text provider to the directory");

            // The explanation shows; the button does not, because linking would be a guess.
            Assert.Equal(Visibility.Visible, prompt.Visibility);
            Assert.Equal(Visibility.Collapsed, link.Visibility);
        });
    }

    [Fact]
    public async Task AConsumerWithNothingToReconcileSeesNoPrompt()
    {
        var panel = await LoadedPanelAsync();

        RenderProfile(panel, view =>
        {
            var prompt = WpfUiHarness.FindByAutomationName<Border>(
                view, "Provider still recorded as free text");
            var system = WpfUiHarness.FindByAutomationName<TextBlock>(
                view, "Healthcare system reconciliation");

            Assert.Equal(Visibility.Collapsed, prompt.Visibility);
            Assert.Equal(Visibility.Collapsed, system.Visibility);
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the panel on its own, on the shared UI thread. Its own control is what makes this
    /// <para>
    /// possible: rendering it inside the whole consumer profile would need the rest of that
    /// screen's view model and services standing up, and the panel does not depend on any of it.

    /// </para>
    /// </summary>
    private static void RenderProfile(ConsumerProvidersViewModel panel, Action<ConsumerProvidersView> assert)
    {
        WpfUiHarness.Run(() =>
        {
            var view = new ConsumerProvidersView { DataContext = panel };
            WpfUiHarness.Realize(view);
            assert(view);
        });
    }



    private static async Task<ConsumerProvidersViewModel> LoadedPanelAsync(
        bool includeEnded = false,
        string? saveRefusal = null,
        string? legacyPrimaryCare = null,
        bool withCurrentLink = true)
    {
        var links = new List<PersonProvider>();
        if (withCurrentLink)
            links.Add(Link(1, 40, 4));
        if (includeEnded)
            links.Add(Link(2, 40, 3, endDate: new DateTime(2026, 3, 1)));

        var stub = new StubLinkService(links) { SaveRefusal = saveRefusal };
        var panel = new ConsumerProvidersViewModel(stub, new StubProviderService(Directory()));
        panel.SetPerson(Consumer(40, legacyPrimaryCare));
        await stub.Loaded;
        return panel;
    }

    private static List<Provider> Directory() =>
    [
        Medical(1, "MaineHealth", MedicalProviderKind.Network),
        Medical(3, "Coastal Women's Healthcare", MedicalProviderKind.Practice, 1),
        Medical(4, "Dr. Reed", MedicalProviderKind.Individual, 3)
    ];

    private static Provider Medical(
        int id, string name, MedicalProviderKind kind, int? parentId = null) => new()
    {
        Id = id, Type = ProviderType.Healthcare, Name = name,
        MedicalKind = kind, ParentProviderId = parentId
    };

    /// <summary>A consumer carrying whatever legacy free text the test needs.</summary>
    private static Person Consumer(int id, string? legacyPrimaryCare = null, string? legacySystem = null)
    {
        var person = Person.Rehydrate(id, 1);
        person.PrimaryCareProvider = legacyPrimaryCare;
        person.HealthcareSystemName = legacySystem;
        return person;
    }

    private static PersonProvider Link(int id, int personId, int providerId, DateTime? endDate = null)
    {
        var link = PersonProvider.Rehydrate(id);
        link.PersonId = personId;
        link.ProviderId = providerId;
        link.EndDate = endDate;
        return link;
    }

    /// <summary>
    /// Stands in for the client view model. Only the provider panel's bindings are under test;
    /// the rest of ClientsView binds against properties this object does not have, which WPF
    /// reports as trace output rather than failure.
    /// </summary>
    private sealed class ProfileHost(ConsumerProvidersViewModel panel)
    {
        public ConsumerProvidersViewModel ConsumerProviders { get; } = panel;
    }

    private sealed class StubProviderService(List<Provider> directory) : IProviderService
    {
        public Task<List<Provider>> GetAllAsync() => Task.FromResult(directory.ToList());
        public Task<List<Provider>> GetPassthroughProvidersAsync() => Task.FromResult(new List<Provider>());
        public Task<Provider> AddAsync(Provider provider) => Task.FromResult(provider);
        public Task<Provider> UpdateAsync(Provider provider) => Task.FromResult(provider);
        public Task DeleteAsync(Provider provider) => Task.CompletedTask;
        public Task<List<ProviderContact>> GetContactsAsync(int providerId) =>
            Task.FromResult(new List<ProviderContact>());
        public Task<ProviderContact> SaveContactAsync(ProviderContact contact) => Task.FromResult(contact);
        public Task RemoveContactAsync(int providerId, int contactId) => Task.CompletedTask;
        public Task<string> MergeAsync(int survivingProviderId, int mergedProviderId) =>
            Task.FromResult(string.Empty);
    }

    private sealed class StubLinkService(List<PersonProvider> links) : IConsumerProviderService
    {
        private readonly TaskCompletionSource _loaded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Loaded => _loaded.Task;
        public string? SaveRefusal { get; init; }

        public Task<List<PersonProvider>> GetByPersonAsync(int personId)
        {
            var forPerson = links.Where(link => link.PersonId == personId).ToList();
            _loaded.TrySetResult();
            return Task.FromResult(forPerson);
        }

        public Task<PersonProvider> SaveAsync(PersonProvider link) =>
            SaveRefusal is null
                ? Task.FromResult(link)
                : throw new InvalidOperationException(SaveRefusal);

        public Task EndAsync(int personId, int linkId, DateTime endDate) => Task.CompletedTask;
        public Task RemoveAsync(int personId, int linkId) => Task.CompletedTask;
    }
}
