using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ProviderDirectoryViewModelTests
{
    [Fact]
    public async Task SelectingASavedProviderLoadsItsNamedContacts()
    {
        var service = new RecordingProviderService();
        service.Contacts[1] =
        [
            Contact(11, 1, "Referral coordinator", isPrimary: true),
            Contact(12, 1, "Billing")
        ];
        var viewModel = Create(service, UserRole.CaseManager);
        await viewModel.LoadAsync();

        viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Id == 1);
        await WaitUntilAsync(() => viewModel.ProviderContacts.Count == 2);

        Assert.True(viewModel.CanManageContacts);
        Assert.True(viewModel.HasProviderContacts);
        Assert.Equal("Referral coordinator", viewModel.ProviderContacts[0].Name);
    }

    [Fact]
    public async Task ASlowContactLoadCannotOverwriteTheNewerProviderSelection()
    {
        var service = new DelayedContactProviderService();
        var viewModel = Create(service, UserRole.CaseManager);
        await viewModel.LoadAsync();

        viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Id == 1);
        viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Id == 2);
        service.Complete(2, [Contact(22, 2, "New selection")]);
        await WaitUntilAsync(() => viewModel.ProviderContacts.Count == 1);
        service.Complete(1, [Contact(11, 1, "Stale selection")]);
        await Task.Delay(25);

        Assert.Equal("New selection", Assert.Single(viewModel.ProviderContacts).Name);
    }

    [Fact]
    public async Task ContactEditorSavesAllNamedContactFieldsAndRefreshesTheList()
    {
        var service = new RecordingProviderService();
        var viewModel = Create(service, UserRole.CaseManager);
        await viewModel.LoadAsync();
        viewModel.SelectedProvider = viewModel.Providers[0];
        await WaitUntilAsync(() => service.ContactReads > 0);
        viewModel.NewProviderContactCommand.Execute(null);
        viewModel.ContactName = "Jamie Referral";
        viewModel.ContactRole = "Referral coordinator";
        viewModel.ContactPhone = "207-555-0100";
        viewModel.ContactExtension = "42";
        viewModel.ContactEmail = "jamie@example.test";
        viewModel.ContactIsPrimary = true;

        await viewModel.SaveProviderContactCommand.ExecuteAsync(null);

        var saved = Assert.Single(service.Contacts[1]);
        Assert.Equal("Jamie Referral", saved.Name);
        Assert.Equal("Referral coordinator", saved.Role);
        Assert.Equal("207-555-0100", saved.Phone);
        Assert.Equal("42", saved.Extension);
        Assert.Equal("jamie@example.test", saved.Email);
        Assert.True(saved.IsPrimary);
        Assert.False(viewModel.IsContactEditorOpen);
        Assert.Equal("Jamie Referral", Assert.Single(viewModel.ProviderContacts).Name);
    }

    [Fact]
    public async Task RemovingAContactUsesTheSelectedProviderScope()
    {
        var service = new RecordingProviderService();
        service.Contacts[1] = [Contact(11, 1, "Remove me")];
        var viewModel = Create(service, UserRole.CaseManager);
        await viewModel.LoadAsync();
        viewModel.SelectedProvider = viewModel.Providers[0];
        await WaitUntilAsync(() => viewModel.ProviderContacts.Count == 1);

        await viewModel.RemoveProviderContactCommand.ExecuteAsync(viewModel.ProviderContacts[0]);

        Assert.Equal((1, 11), service.LastRemovedContact);
        Assert.Empty(viewModel.ProviderContacts);
    }

    [Fact]
    public async Task MergeFailsClosedWhenNoConfirmationViewIsAttached()
    {
        var service = new RecordingProviderService();
        var viewModel = Create(service, UserRole.Admin);
        await OpenFirstForMergeAsync(viewModel);

        await viewModel.MergeProviderEntriesCommand.ExecuteAsync(null);

        Assert.Equal(0, service.MergeCalls);
        Assert.Equal(2, viewModel.Providers.Count);
    }

    [Fact]
    public async Task MergeConfirmationNamesBothEntriesAndCancelWritesNothing()
    {
        var service = new RecordingProviderService();
        var viewModel = Create(service, UserRole.Admin);
        await OpenFirstForMergeAsync(viewModel);
        ProviderMergeConfirmationEventArgs? shown = null;
        viewModel.MergeConfirmationRequested += (_, args) => shown = args;

        await viewModel.MergeProviderEntriesCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal("Keep Provider", shown.SurvivingProviderName);
        Assert.Equal("Duplicate Provider", shown.MergedProviderName);
        Assert.Contains("Existing documents will keep", shown.Message);
        Assert.Contains("cannot be undone", shown.Message);
        Assert.Equal(0, service.MergeCalls);
    }

    [Fact]
    public async Task ConfirmedAdminMergeRefreshesAndShowsTheServiceSummary()
    {
        var service = new RecordingProviderService();
        var viewModel = Create(service, UserRole.Admin);
        await OpenFirstForMergeAsync(viewModel);
        viewModel.MergeConfirmationRequested += (_, args) => args.Confirmed = true;

        await viewModel.MergeProviderEntriesCommand.ExecuteAsync(null);

        Assert.Equal(1, service.MergeCalls);
        Assert.Single(viewModel.Providers);
        Assert.Equal("Keep Provider", viewModel.SelectedProvider?.Name);
        Assert.True(viewModel.HasNoticeMessage);
        Assert.Contains("was merged", viewModel.NoticeMessage);
    }

    [Fact]
    public async Task NonAdminDoesNotReceiveTheMergeControlOrReachTheService()
    {
        var service = new RecordingProviderService();
        var viewModel = Create(service, UserRole.CaseManager);
        await OpenFirstForMergeAsync(viewModel);

        Assert.False(viewModel.CanMergeProviders);
        await viewModel.MergeProviderEntriesCommand.ExecuteAsync(null);
        Assert.Equal(0, service.MergeCalls);
    }

    private static async Task OpenFirstForMergeAsync(ProvidersViewModel viewModel)
    {
        await viewModel.LoadAsync();
        viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Id == 1);
        viewModel.MergeProvider = viewModel.MergeCandidates.Single(provider => provider.Id == 2);
    }

    private static ProvidersViewModel Create(IProviderService service, UserRole role)
    {
        var session = new SessionService();
        session.SetUser(User.Create(90, "provider-user", "Provider User", "hash", "salt", role, null, 7));
        return new ProvidersViewModel(service, session);
    }

    private static ProviderContact Contact(
        int id,
        int providerId,
        string name,
        bool isPrimary = false)
    {
        var contact = ProviderContact.Rehydrate(id);
        contact.ProviderId = providerId;
        contact.Name = name;
        contact.IsPrimary = isPrimary;
        return contact;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private class RecordingProviderService : IProviderService
    {
        private int _nextContactId = 100;
        public List<Provider> Directory { get; } =
        [
            new() { Id = 1, AgencyId = 7, Name = "Keep Provider", Type = ProviderType.Other },
            new() { Id = 2, AgencyId = 7, Name = "Duplicate Provider", Type = ProviderType.Other }
        ];
        public Dictionary<int, List<ProviderContact>> Contacts { get; } = [];
        public int ContactReads { get; private set; }
        public (int ProviderId, int ContactId)? LastRemovedContact { get; private set; }
        public int MergeCalls { get; private set; }

        public Task<List<Provider>> GetAllAsync() => Task.FromResult(Directory.ToList());
        public Task<List<Provider>> GetPassthroughProvidersAsync() => Task.FromResult(new List<Provider>());
        public Task<Provider> AddAsync(Provider provider) => Task.FromResult(provider);
        public Task<Provider> UpdateAsync(Provider provider) => Task.FromResult(provider);
        public Task DeleteAsync(Provider provider) => Task.CompletedTask;

        public virtual Task<List<ProviderContact>> GetContactsAsync(int providerId)
        {
            ContactReads++;
            return Task.FromResult(Contacts.TryGetValue(providerId, out var contacts)
                ? contacts.ToList()
                : new List<ProviderContact>());
        }

        public Task<ProviderContact> SaveContactAsync(ProviderContact contact)
        {
            if (!Contacts.TryGetValue(contact.ProviderId, out var contacts))
                Contacts[contact.ProviderId] = contacts = [];
            if (contact.Id == 0)
            {
                var saved = ProviderContact.Rehydrate(++_nextContactId);
                saved.ProviderId = contact.ProviderId;
                saved.Name = contact.Name;
                saved.Role = contact.Role;
                saved.Phone = contact.Phone;
                saved.Extension = contact.Extension;
                saved.Email = contact.Email;
                saved.IsPrimary = contact.IsPrimary;
                saved.SortOrder = contact.SortOrder;
                contacts.Add(saved);
                return Task.FromResult(saved);
            }

            return Task.FromResult(contact);
        }

        public Task RemoveContactAsync(int providerId, int contactId)
        {
            LastRemovedContact = (providerId, contactId);
            if (Contacts.TryGetValue(providerId, out var contacts))
                contacts.RemoveAll(contact => contact.Id == contactId);
            return Task.CompletedTask;
        }

        public Task<string> MergeAsync(int survivingProviderId, int mergedProviderId)
        {
            MergeCalls++;
            Directory.RemoveAll(provider => provider.Id == mergedProviderId);
            return Task.FromResult(
                "\"Duplicate Provider\" was merged into \"Keep Provider\". Moved 0 affiliated entries, 0 consumer links, and 0 contacts. Existing documents keep what they recorded.");
        }
    }

    private sealed class DelayedContactProviderService : RecordingProviderService
    {
        private readonly Dictionary<int, TaskCompletionSource<List<ProviderContact>>> _loads = [];

        public override Task<List<ProviderContact>> GetContactsAsync(int providerId)
        {
            var source = new TaskCompletionSource<List<ProviderContact>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _loads[providerId] = source;
            return source.Task;
        }

        public void Complete(int providerId, List<ProviderContact> contacts) =>
            _loads[providerId].SetResult(contacts);
    }
}
