using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The consumer profile's provider panel. The behaviour worth pinning down is that the
/// practice and network are resolved from the directory on every load rather than stored,
/// that ended relationships stay on the record behind a disclosure, and that a slow load
/// for a consumer the case manager has left cannot publish itself over the newer one.
/// </summary>
public sealed class ConsumerProvidersViewModelTests
{
    private static readonly DateTime Today = new(2026, 8, 28);

    [Fact]
    public async Task ThePracticeAndNetworkAreResolvedFromTheDirectory()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 4)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        var row = Assert.Single(viewModel.Current);
        Assert.Equal("Dr. Reed", row.ProviderName);
        Assert.Equal("Coastal Women's Healthcare", row.PracticeName);
        Assert.Equal("MaineHealth", row.NetworkName);
        Assert.Equal("Coastal Women's Healthcare · MaineHealth", row.Affiliation);
    }

    [Fact]
    public async Task MovingTheClinicianInTheDirectoryChangesWhatTheProfileShows()
    {
        // Nothing about the consumer's row changes. That is the point of deriving.
        var links = new StubLinkService([Link(1, 40, providerId: 4)]);
        var directory = Directory();
        var providers = new StubProviderService(directory);
        var viewModel = Build(links, providers);

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;
        var before = Assert.Single(viewModel.Current).Affiliation;

        directory.Add(Medical(5, "InterMed", MedicalProviderKind.Network));
        directory.Single(provider => provider.Id == 4).ParentProviderId = 5;
        await viewModel.RefreshAsync();

        Assert.Equal("Coastal Women's Healthcare · MaineHealth", before);
        Assert.Equal("InterMed", Assert.Single(viewModel.Current).Affiliation);
    }

    [Fact]
    public async Task AProviderThatStandsAloneShowsNoAffiliationRatherThanABlankLine()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 1)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        var row = Assert.Single(viewModel.Current);
        Assert.False(row.HasAffiliation);
        Assert.Equal(string.Empty, row.Affiliation);
    }

    [Fact]
    public async Task EndedRelationshipsMoveToThePastListInsteadOfDisappearing()
    {
        var links = new StubLinkService([
            Link(1, 40, providerId: 4),
            Link(2, 40, providerId: 3, endDate: new DateTime(2026, 3, 1))
        ]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.Single(viewModel.Current);
        Assert.Single(viewModel.Past);
        Assert.True(viewModel.HasPast);
        Assert.Equal("1 past provider", viewModel.PastDisclosureLabel);
        // Collapsed by default: past providers are kept for the record, not for the eye.
        Assert.False(viewModel.ShowPast);
    }

    [Fact]
    public async Task ThePrimaryCareProviderIsPinnedFirst()
    {
        var links = new StubLinkService([
            Link(1, 40, providerId: 3, sortOrder: 0),
            Link(2, 40, providerId: 4, sortOrder: 5, isPrimaryCare: true)
        ]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.Equal("Dr. Reed", viewModel.Current[0].ProviderName);
        Assert.Equal("Current · primary care", viewModel.Current[0].StatusLabel);
    }

    [Fact]
    public async Task AnEndedRowSaysSoInTextRatherThanOnlyInColour()
    {
        var links = new StubLinkService([
            Link(1, 40, providerId: 4, endDate: new DateTime(2026, 3, 4))
        ]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        var row = Assert.Single(viewModel.Past);
        Assert.Equal("Ended 4 Mar 2026", row.StatusLabel);
        Assert.Contains("Ended 4 Mar 2026", row.AutomationName);
        Assert.Contains("Dr. Reed", row.AutomationName);
    }

    [Fact]
    public async Task ADirectoryEntryTheCaseManagerCannotSeeIsNamedRatherThanLeftBlank()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 999)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.Equal("Provider no longer in the directory", Assert.Single(viewModel.Current).ProviderName);
    }

    [Fact]
    public async Task ThePickerOffersMedicalEntriesWithIndividualsFirst()
    {
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.Equal(
            new[] { "Dr. Reed", "Coastal Women's Healthcare", "MaineHealth" },
            viewModel.ProviderOptions.Select(option => option.Name).ToArray());
    }

    [Fact]
    public async Task AWaiverProviderIsNotOfferedOnTheMedicalPicker()
    {
        var links = new StubLinkService([]);
        var directory = Directory();
        directory.Add(new Provider { Id = 7, Name = "Spurwink", Type = ProviderType.Waiver });
        var viewModel = Build(links, new StubProviderService(directory));

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.DoesNotContain(viewModel.ProviderOptions, option => option.Name == "Spurwink");
    }

    [Fact]
    public async Task ARefusedAddShowsTheReasonAndKeepsWhatWasEntered()
    {
        var links = new StubLinkService([]) { SaveRefusal = "Dr. Reed is already the primary care provider." };
        var viewModel = Build(links, Directory());
        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        viewModel.NewProviderId = 4;
        viewModel.NewRole = "Neurologist";
        await viewModel.AddProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasStatusMessage);
        Assert.Contains("already the primary care provider", viewModel.StatusMessage);
        Assert.Equal(4, viewModel.NewProviderId);
        Assert.Equal("Neurologist", viewModel.NewRole);
    }

    [Fact]
    public async Task TheAffiliationOfTheProviderBeingAddedIsShownBeforeCommitting()
    {
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());
        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        viewModel.NewProviderId = 4;

        Assert.True(viewModel.HasSelectedProviderAffiliation);
        Assert.Equal(
            "Coastal Women's Healthcare · MaineHealth", viewModel.SelectedProviderAffiliation);
    }

    [Fact]
    public async Task ChangingConsumerDiscardsAnInFlightLoadForThePreviousOne()
    {
        // The outgoing consumer's load is held open until after the incoming one has already
        // published, so the stale response really does arrive last. Without that ordering the
        // test would pass whenever the newer load simply happened to finish second, which
        // proves nothing about the guard.
        var links = new StubLinkService([
            Link(1, 40, providerId: 4),
            Link(2, 41, providerId: 3)
        ])
        {
            HoldFirstLoad = new TaskCompletionSource()
        };
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        viewModel.SetPerson(Consumer(41));
        await links.SecondLoadPublished;

        links.HoldFirstLoad!.SetResult();
        await links.FirstLoadReturned;

        // Dr. Reed belongs to consumer 40. Consumer 41 sees only their own provider.
        var row = Assert.Single(viewModel.Current);
        Assert.Equal("Coastal Women's Healthcare", row.ProviderName);
    }

    [Fact]
    public void ClearingTheConsumerEmptiesThePanel()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 4)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(null);

        Assert.False(viewModel.HasLoadedPerson);
        Assert.False(viewModel.CanAdd);
        Assert.Empty(viewModel.Current);
        Assert.Empty(viewModel.ProviderOptions);
    }

    // ── Reconciling the legacy free-text fields ──────────────────────────────

    [Fact]
    public async Task AFreeTextPrimaryCareProviderWithNoLinkIsOfferedForLinking()
    {
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacyPrimaryCare: "Dr. Reed"));
        await links.Loaded;

        Assert.True(viewModel.NeedsPrimaryCareLinking);
        Assert.True(viewModel.CanLinkLegacyPrimaryCare);
        Assert.Equal("Link Dr. Reed", viewModel.LinkLegacyPrimaryCareLabel);
    }

    [Fact]
    public async Task LinkingTheFreeTextProviderRecordsItAsPrimaryCare()
    {
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());
        viewModel.SetPerson(Consumer(40, legacyPrimaryCare: "Dr. Reed"));
        await links.Loaded;

        await viewModel.LinkLegacyPrimaryCareCommand.ExecuteAsync(null);

        var saved = Assert.Single(links.Saved);
        Assert.Equal(4, saved.ProviderId);
        Assert.True(saved.IsPrimaryCare);
        // No start date: when the relationship began is not something the free text ever
        // knew, and today would assert a fact nobody entered.
        Assert.Null(saved.StartDate);
    }

    [Fact]
    public async Task AConsumerWhoAlreadyHasAPrimaryCareLinkIsNotPrompted()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 4, isPrimaryCare: true)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacyPrimaryCare: "Dr. Reed"));
        await links.Loaded;

        Assert.False(viewModel.NeedsPrimaryCareLinking);
    }

    [Fact]
    public async Task AConsumerWithNoFreeTextIsNotPrompted()
    {
        // Most consumers legitimately have nothing recorded; a prompt nobody can clear is
        // worse than no prompt.
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40));
        await links.Loaded;

        Assert.False(viewModel.NeedsPrimaryCareLinking);
        Assert.Equal(string.Empty, viewModel.PrimaryCareLinkGuidance);
    }

    [Fact]
    public async Task AFreeTextNameWithNoDirectoryEntryIsExplainedButNotLinkable()
    {
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacyPrimaryCare: "Dr. Nobody"));
        await links.Loaded;

        Assert.True(viewModel.NeedsPrimaryCareLinking);
        Assert.False(viewModel.CanLinkLegacyPrimaryCare);
        Assert.Contains("Add them to the provider directory", viewModel.PrimaryCareLinkGuidance);
    }

    [Fact]
    public async Task ATypedHealthcareSystemThatDisagreesWithTheLinkedNetworkIsSurfaced()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 4, isPrimaryCare: true)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacySystem: "InterMed"));
        await links.Loaded;

        Assert.True(viewModel.HasHealthcareSystemGuidance);
        Assert.Contains("InterMed", viewModel.HealthcareSystemGuidance);
        Assert.Contains("MaineHealth", viewModel.HealthcareSystemGuidance);
    }

    [Fact]
    public async Task ATypedHealthcareSystemThatAgreesIsSilent()
    {
        var links = new StubLinkService([Link(1, 40, providerId: 4, isPrimaryCare: true)]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacySystem: "MaineHealth"));
        await links.Loaded;

        Assert.False(viewModel.HasHealthcareSystemGuidance);
    }

    [Fact]
    public async Task NothingIsWrittenJustByOpeningAConsumer()
    {
        // The panel proposes; it never backfills. A bulk name-match write across live consumer
        // records is precisely the operation that must not happen unreviewed.
        var links = new StubLinkService([]);
        var viewModel = Build(links, Directory());

        viewModel.SetPerson(Consumer(40, legacyPrimaryCare: "Dr. Reed", legacySystem: "MaineHealth"));
        await links.Loaded;

        Assert.Empty(links.Saved);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ConsumerProvidersViewModel Build(
        IConsumerProviderService links, List<Provider> directory) =>
        Build(links, new StubProviderService(directory));

    private static ConsumerProvidersViewModel Build(
        IConsumerProviderService links, StubProviderService providers) =>
        new(links, providers, () => Today);

    /// <summary>MaineHealth → Coastal Women's Healthcare → Dr. Reed, plus a standalone network.</summary>
    private static List<Provider> Directory() =>
    [
        Medical(1, "MaineHealth", MedicalProviderKind.Network),
        Medical(3, "Coastal Women's Healthcare", MedicalProviderKind.Practice, 1),
        Medical(4, "Dr. Reed", MedicalProviderKind.Individual, 3)
    ];

    private static Provider Medical(
        int id, string name, MedicalProviderKind kind, int? parentId = null) => new()
    {
        Id = id, Type = ProviderType.Healthcare, Name = name, MedicalKind = kind, ParentProviderId = parentId
    };

    /// <summary>A consumer carrying whatever legacy free text the test needs.</summary>
    private static Person Consumer(int id, string? legacyPrimaryCare = null, string? legacySystem = null)
    {
        var person = Person.Rehydrate(id, 1);
        person.PrimaryCareProvider = legacyPrimaryCare;
        person.HealthcareSystemName = legacySystem;
        return person;
    }

    private static PersonProvider Link(
        int id, int personId, int providerId, bool isPrimaryCare = false,
        DateTime? endDate = null, int sortOrder = 0)
    {
        var link = PersonProvider.Rehydrate(id);
        link.PersonId = personId;
        link.ProviderId = providerId;
        link.IsPrimaryCare = isPrimaryCare;
        link.EndDate = endDate;
        link.SortOrder = sortOrder;
        return link;
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
        private readonly TaskCompletionSource _loaded = Signal();
        private readonly TaskCompletionSource _secondPublished = Signal();
        private readonly TaskCompletionSource _firstReturned = Signal();
        private int _calls;

        public Task Loaded => _loaded.Task;

        /// <summary>Completes once a second load has returned, so ordering can be forced.</summary>
        public Task SecondLoadPublished => _secondPublished.Task;

        /// <summary>Completes once the deliberately delayed first load has returned.</summary>
        public Task FirstLoadReturned => _firstReturned.Task;

        /// <summary>Holds the first load open so it lands after the consumer has changed.</summary>
        public TaskCompletionSource? HoldFirstLoad { get; init; }

        public string? SaveRefusal { get; init; }

        public async Task<List<PersonProvider>> GetByPersonAsync(int personId)
        {
            var call = Interlocked.Increment(ref _calls);
            if (HoldFirstLoad is not null && call == 1)
                await HoldFirstLoad.Task;

            var forPerson = links.Where(link => link.PersonId == personId).ToList();
            _loaded.TrySetResult();
            if (call == 1)
                _firstReturned.TrySetResult();
            else
                _secondPublished.TrySetResult();
            return forPerson;
        }

        private static TaskCompletionSource Signal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Everything the panel actually asked to persist.</summary>
        public List<PersonProvider> Saved { get; } = [];

        public Task<PersonProvider> SaveAsync(PersonProvider link)
        {
            if (SaveRefusal is not null)
                throw new InvalidOperationException(SaveRefusal);

            Saved.Add(link);
            links.Add(link);
            return Task.FromResult(link);
        }

        public Task EndAsync(int personId, int linkId, DateTime endDate) => Task.CompletedTask;

        public Task RemoveAsync(int personId, int linkId) => Task.CompletedTask;
    }
}
