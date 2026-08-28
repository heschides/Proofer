using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The directory form. The parent picker has to offer exactly what a save would accept —
/// a form that lists choices the server refuses is worse than one that lists none — and a
/// refusal has to reach the person editing rather than vanishing into an unhandled task.
/// </summary>
public sealed class ProviderAffiliationViewModelTests
{
    [Fact]
    public void TheParentPickerOffersOnlyEntriesASaveWouldAccept()
    {
        var directory = Directory();

        var individual = Editor(New(MedicalProviderKind.Individual), directory);
        var practice = Editor(New(MedicalProviderKind.Practice), directory);
        var network = Editor(New(MedicalProviderKind.Network), directory);

        // An individual may sit under the practice or either network; a practice or a
        // network may sit only under a network.
        Assert.Equal(
            new[] { "Coastal Women's Healthcare", "Maine Medical Partners", "MaineHealth" },
            individual.ParentOptions.Select(option => option.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "Maine Medical Partners", "MaineHealth" },
            practice.ParentOptions.Select(option => option.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "Maine Medical Partners", "MaineHealth" },
            network.ParentOptions.Select(option => option.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void ANonMedicalEntryIsNeverOfferedAsAParent()
    {
        var directory = Directory();
        directory.Add(new Provider { Id = 9, Name = "Spurwink", Type = ProviderType.Waiver });

        var editor = Editor(New(MedicalProviderKind.Individual), directory);

        Assert.DoesNotContain(editor.ParentOptions, option => option.Name == "Spurwink");
    }

    [Fact]
    public void AnEntryIsNeverOfferedAsItsOwnParent()
    {
        var directory = Directory();
        var existing = directory.Single(provider => provider.Name == "Maine Medical Partners");

        var editor = Editor(existing, directory);

        Assert.DoesNotContain(editor.ParentOptions, option => option.Id == existing.Id);
    }

    [Fact]
    public void AnEntryThatWouldCloseALoopIsNotOffered()
    {
        // Maine Medical Partners is a network already sitting beneath MaineHealth, so the
        // tier rule alone would happily allow it as MaineHealth's parent. Only the loop
        // check excludes it, which is what makes this a test of the loop check.
        var directory = Directory();
        var top = directory.Single(provider => provider.Name == "MaineHealth");

        var editor = Editor(top, directory);

        Assert.DoesNotContain(editor.ParentOptions, option => option.Name == "Maine Medical Partners");
    }

    [Fact]
    public void ChangingTheDesignationClearsAParentThatIsNoLongerLegal()
    {
        var directory = Directory();
        var provider = New(MedicalProviderKind.Individual);
        var editor = Editor(provider, directory);
        editor.ParentProviderId = directory.Single(candidate => candidate.Name == "Coastal Women's Healthcare").Id;

        // A practice cannot belong to a practice, so the selection has to go rather than
        // sit invisibly until the save is refused.
        editor.MedicalKind = MedicalProviderKind.Practice;

        Assert.Null(editor.ParentProviderId);
        Assert.Null(provider.ParentProviderId);
    }

    [Fact]
    public void ChangingTheDesignationKeepsAParentThatIsStillLegal()
    {
        var directory = Directory();
        var provider = New(MedicalProviderKind.Individual);
        var editor = Editor(provider, directory);
        var networkId = directory.Single(candidate => candidate.Name == "MaineHealth").Id;
        editor.ParentProviderId = networkId;

        editor.MedicalKind = MedicalProviderKind.Practice;

        Assert.Equal(networkId, editor.ParentProviderId);
    }

    [Fact]
    public void LeavingMedicalClearsBothTheDesignationAndTheParent()
    {
        var directory = Directory();
        var provider = New(MedicalProviderKind.Individual);
        var editor = Editor(provider, directory);
        editor.ParentProviderId = directory.Single(candidate => candidate.Name == "MaineHealth").Id;

        editor.Type = ProviderType.Waiver;

        Assert.False(editor.IsMedical);
        Assert.Null(editor.MedicalKind);
        Assert.Null(editor.ParentProviderId);
        Assert.Null(provider.MedicalKind);
        Assert.Null(provider.ParentProviderId);
    }

    [Fact]
    public void TheResolvedChainIsShownForAnEntryThatHasNotBeenSavedYet()
    {
        var directory = Directory();
        var editor = Editor(New(MedicalProviderKind.Individual), directory);

        editor.ParentProviderId = directory.Single(candidate => candidate.Name == "Coastal Women's Healthcare").Id;

        Assert.True(editor.HasAffiliation);
        Assert.Equal(
            "Coastal Women's Healthcare · Maine Medical Partners · MaineHealth",
            editor.AffiliationSummary);
    }

    [Fact]
    public void AnUnaffiliatedEntryShowsNoChain()
    {
        var editor = Editor(New(MedicalProviderKind.Network), Directory());

        Assert.False(editor.HasAffiliation);
        Assert.Equal(string.Empty, editor.AffiliationSummary);
    }

    [Fact]
    public void AnEmptyPickerExplainsWhatIsMissingRatherThanShowingNothing()
    {
        var editor = Editor(New(MedicalProviderKind.Individual), []);

        Assert.False(editor.HasParentOptions);
        Assert.Contains("Add a practice or a network", editor.ParentEmptyExplanation);
    }

    [Fact]
    public void TypingAnExistingNameShowsANonBlockingDuplicateWarning()
    {
        var directory = Directory();
        var editor = Editor(New(MedicalProviderKind.Network), directory);

        editor.Name = "  MAINEHEALTH ";

        Assert.True(editor.HasSameNameWarning);
        Assert.Contains("already named \"MAINEHEALTH\"", editor.SameNameWarning);
        Assert.Equal("  MAINEHEALTH ", editor.Provider.Name);
    }

    [Fact]
    public void AnEntryBeingEditedDoesNotWarnAboutItsOwnName()
    {
        var directory = Directory();
        var existing = directory.Single(provider => provider.Name == "MaineHealth");
        var editor = Editor(existing, directory);

        Assert.False(editor.HasSameNameWarning);
        Assert.Equal(string.Empty, editor.SameNameWarning);
    }

    [Fact]
    public async Task ARefusedSaveKeepsTheEditorOpenAndShowsTheReason()
    {
        var viewModel = new ProvidersViewModel(
            new StubProviderService("\"MaineHealth\" already sits beneath this entry."));
        viewModel.NewProviderCommand.Execute(null);
        viewModel.CurrentEditor!.Name = "Dr. Reed";

        await viewModel.SaveProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSaveError);
        Assert.Contains("already sits beneath", viewModel.SaveError);
        // The entered work survives the refusal: the rule rejects an edit, it does not
        // correct one, so discarding the form would make the message useless.
        Assert.NotNull(viewModel.CurrentEditor);
        Assert.Equal("Dr. Reed", viewModel.CurrentEditor!.Name);
    }

    [Fact]
    public async Task ARefusedDeleteShowsTheReasonAndLeavesTheEntryOpen()
    {
        var service = new StubProviderService("\"MaineHealth\" cannot be deleted while 1 entry is affiliated with it.");
        var viewModel = new ProvidersViewModel(service);
        await viewModel.LoadAsync();
        viewModel.SelectedProvider = viewModel.Providers.Single();

        await viewModel.DeleteProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSaveError);
        Assert.Contains("cannot be deleted", viewModel.SaveError);
        Assert.NotNull(viewModel.CurrentEditor);
    }

    [Fact]
    public async Task ASuccessfulSaveClearsTheRefusalAndClosesTheEditor()
    {
        var service = new StubProviderService(refusal: null);
        var viewModel = new ProvidersViewModel(service);
        viewModel.NewProviderCommand.Execute(null);
        viewModel.CurrentEditor!.Name = "MaineHealth";

        await viewModel.SaveProviderCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasSaveError);
        Assert.Null(viewModel.CurrentEditor);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProviderEditorViewModel Editor(Provider provider, IEnumerable<Provider> directory) =>
        new(provider, directory);

    private static Provider New(MedicalProviderKind kind) => new()
    {
        Type = ProviderType.Healthcare,
        MedicalKind = kind
    };

    /// <summary>MaineHealth → Maine Medical Partners → Coastal Women's Healthcare.</summary>
    private static List<Provider> Directory() =>
    [
        Medical(1, "MaineHealth", MedicalProviderKind.Network),
        Medical(2, "Maine Medical Partners", MedicalProviderKind.Network, 1),
        Medical(3, "Coastal Women's Healthcare", MedicalProviderKind.Practice, 2)
    ];

    private static Provider Medical(int id, string name, MedicalProviderKind kind, int? parentId = null) => new()
    {
        Id = id,
        Type = ProviderType.Healthcare,
        Name = name,
        MedicalKind = kind,
        ParentProviderId = parentId
    };

    private sealed class StubProviderService(string? refusal) : IProviderService
    {
        private readonly List<Provider> _providers =
            [Medical(1, "MaineHealth", MedicalProviderKind.Network)];

        public Task<List<Provider>> GetAllAsync() => Task.FromResult(_providers.ToList());

        public Task<List<Provider>> GetPassthroughProvidersAsync() => Task.FromResult(new List<Provider>());

        public Task<Provider> AddAsync(Provider provider) => Refuse() ?? Task.FromResult(provider);

        public Task<Provider> UpdateAsync(Provider provider) => Refuse() ?? Task.FromResult(provider);

        public Task DeleteAsync(Provider provider) =>
            refusal is null ? Task.CompletedTask : throw new InvalidOperationException(refusal);

        public Task<List<ProviderContact>> GetContactsAsync(int providerId) =>
            Task.FromResult(new List<ProviderContact>());
        public Task<ProviderContact> SaveContactAsync(ProviderContact contact) => Task.FromResult(contact);
        public Task RemoveContactAsync(int providerId, int contactId) => Task.CompletedTask;
        public Task<string> MergeAsync(int survivingProviderId, int mergedProviderId) =>
            Task.FromResult(string.Empty);

        private Task<Provider>? Refuse() =>
            refusal is null ? null : throw new InvalidOperationException(refusal);
    }
}
