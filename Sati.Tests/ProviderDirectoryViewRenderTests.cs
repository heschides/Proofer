using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

public sealed class ProviderDirectoryViewRenderTests
{
    [Fact]
    public void ProviderDirectoryRendersDuplicateWarningContactsAndAdminMergeControls()
    {
        WpfUiHarness.Run(() =>
        {
            var service = new RenderProviderService();
            var session = new SessionService();
            session.SetUser(User.Create(
                90, "render-admin", "Render Admin", "hash", "salt", UserRole.Admin, null, 7));
            var viewModel = new ProvidersViewModel(service, session);
            viewModel.LoadAsync().GetAwaiter().GetResult();
            viewModel.SelectedProvider = viewModel.Providers.Single(provider => provider.Id == 2);
            viewModel.CurrentEditor!.Name = "  MAINEHEALTH ";
            viewModel.MergeProvider = viewModel.MergeCandidates.Single(provider => provider.Id == 1);

            var view = new ProvidersView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 1400, 1100);

            var duplicate = WpfUiHarness.FindByAutomationName<Border>(
                view, "Possible duplicate provider");
            var addContact = WpfUiHarness.FindByAutomationName<Button>(
                view, "Add named provider contact");
            var contacts = WpfUiHarness.FindByAutomationName<ListBox>(
                view, "Named provider contacts");
            var merge = WpfUiHarness.FindByAutomationName<Border>(
                view, "Admin provider merge");
            var reviewMerge = WpfUiHarness.FindByAutomationName<Button>(
                view, "Review provider merge");

            Assert.Equal(Visibility.Visible, duplicate.Visibility);
            Assert.Equal(Visibility.Visible, addContact.Visibility);
            Assert.Single(contacts.Items);
            Assert.Equal(Visibility.Visible, merge.Visibility);
            Assert.True(reviewMerge.IsEnabled);
            Assert.True(reviewMerge.ActualWidth > 0);
        });
    }

    private sealed class RenderProviderService : IProviderService
    {
        private readonly List<Provider> _providers =
        [
            new() { Id = 1, AgencyId = 7, Name = "MaineHealth", Type = ProviderType.Other },
            new() { Id = 2, AgencyId = 7, Name = "Other entry", Type = ProviderType.Other }
        ];

        public Task<List<Provider>> GetAllAsync() => Task.FromResult(_providers.ToList());
        public Task<List<Provider>> GetPassthroughProvidersAsync() => Task.FromResult(new List<Provider>());
        public Task<Provider> AddAsync(Provider provider) => Task.FromResult(provider);
        public Task<Provider> UpdateAsync(Provider provider) => Task.FromResult(provider);
        public Task DeleteAsync(Provider provider) => Task.CompletedTask;
        public Task<List<ProviderContact>> GetContactsAsync(int providerId)
        {
            var contact = ProviderContact.Rehydrate(11);
            contact.ProviderId = providerId;
            contact.Name = "Referral coordinator";
            return Task.FromResult(new List<ProviderContact> { contact });
        }
        public Task<ProviderContact> SaveContactAsync(ProviderContact contact) => Task.FromResult(contact);
        public Task RemoveContactAsync(int providerId, int contactId) => Task.CompletedTask;
        public Task<string> MergeAsync(int survivingProviderId, int mergedProviderId) =>
            Task.FromResult("Merged.");
    }
}
