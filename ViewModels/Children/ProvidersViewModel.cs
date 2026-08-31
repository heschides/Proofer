using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // The Providers directory tab under CM sub-nav. Master-detail: a list of all
    // providers plus an editor pane for the selected or new one. Reference-data
    // CRUD, so it persists immediately on Save/Delete — contrast the AT page,
    // which defers persistence to a later slice.
    public partial class ProvidersViewModel : ObservableObject
    {
        private readonly IProviderService _providerService;
        private readonly ISessionService? _sessionService;
        private readonly LatestRequestTracker _contactLoads = new();

        public ProvidersViewModel(
            IProviderService providerService,
            ISessionService? sessionService = null)
        {
            _providerService = providerService;
            _sessionService = sessionService;
        }

        public event EventHandler<ProviderMergeConfirmationEventArgs>? MergeConfirmationRequested;

        public ObservableCollection<Provider> Providers { get; } = [];
        public ObservableCollection<Provider> MergeCandidates { get; } = [];
        public ObservableCollection<ProviderContact> ProviderContacts { get; } = [];

        // The row picked in the list. Selecting one opens it in the editor via the
        // OnChanged callback below. Null when nothing is selected.
        [ObservableProperty] private Provider? selectedProvider;

        // The editor for the provider currently open (existing or brand-new), or
        // null = list-only. Same CurrentEditor idiom as the AT page.
        [ObservableProperty] private ProviderEditorViewModel? currentEditor;
        [ObservableProperty] private Provider? mergeProvider;
        [ObservableProperty] private ProviderContact? selectedProviderContact;
        [ObservableProperty] private bool isContactEditorOpen;
        [ObservableProperty] private string contactName = string.Empty;
        [ObservableProperty] private string contactRole = string.Empty;
        [ObservableProperty] private string contactPhone = string.Empty;
        [ObservableProperty] private string contactExtension = string.Empty;
        [ObservableProperty] private string contactEmail = string.Empty;
        [ObservableProperty] private bool contactIsPrimary;
        [ObservableProperty] private string noticeMessage = string.Empty;

        public bool IsEditing => CurrentEditor is not null;
        public bool CanManageContacts => CurrentEditor?.Provider.Id > 0;
        public bool HasProviderContacts => ProviderContacts.Count > 0;
        public bool HasNoticeMessage => NoticeMessage.Length > 0;
        public bool CanMergeProviders =>
            CurrentEditor?.Provider.Id > 0 &&
            _sessionService?.CurrentUser?.HasAdminPermissions == true;
        public bool HasMergeSelection => CanMergeProviders && MergeProvider is not null;
        public string ContactEditorHeader =>
            SelectedProviderContact is null ? "ADD CONTACT" : "EDIT CONTACT";

        // Why the last save or delete was refused. The directory rules — a duplicate
        // identifier, an illegal affiliation, deleting a parent that still has entries
        // beneath it — all produce a message written to be read by the person editing.
        // Without somewhere to show it those messages reach nobody and the button looks
        // broken instead.
        [ObservableProperty] private string saveError = string.Empty;

        public bool HasSaveError => SaveError.Length > 0;

        partial void OnSaveErrorChanged(string value)
            => OnPropertyChanged(nameof(HasSaveError));

        partial void OnNoticeMessageChanged(string value) =>
            OnPropertyChanged(nameof(HasNoticeMessage));

        partial void OnCurrentEditorChanged(ProviderEditorViewModel? value)
        {
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(CanManageContacts));
            OnPropertyChanged(nameof(CanMergeProviders));
            OnPropertyChanged(nameof(HasMergeSelection));
            MergeProvider = null;
            MergeCandidates.Clear();
            if (value?.Provider.Id > 0)
            {
                foreach (var provider in Providers.Where(provider => provider.Id != value.Provider.Id))
                    MergeCandidates.Add(provider);
                _ = LoadContactsAsync(value.Provider.Id, _contactLoads.Begin());
            }
            else
            {
                _contactLoads.Invalidate();
                ProviderContacts.Clear();
                ClearContactEditor();
                RaiseContactListChanged();
            }
        }

        partial void OnSelectedProviderContactChanged(ProviderContact? value)
        {
            if (value is null)
            {
                ClearContactEditor();
                return;
            }

            ContactName = value.Name;
            ContactRole = value.Role ?? string.Empty;
            ContactPhone = value.Phone ?? string.Empty;
            ContactExtension = value.Extension ?? string.Empty;
            ContactEmail = value.Email ?? string.Empty;
            ContactIsPrimary = value.IsPrimary;
            IsContactEditorOpen = true;
            OnPropertyChanged(nameof(ContactEditorHeader));
        }

        partial void OnMergeProviderChanged(Provider? value) =>
            OnPropertyChanged(nameof(HasMergeSelection));

        // Selecting a list row opens it in a fresh editor. NOTE: switching rows
        // discards unsaved edits on the previous one — standard master-detail, but
        // worth knowing. NewProvider clears selection so it doesn't fight this.
        partial void OnSelectedProviderChanged(Provider? value)
        {
            SaveError = string.Empty;
            NoticeMessage = string.Empty;
            CurrentEditor = value is null ? null : new ProviderEditorViewModel(value, Providers);
        }

        // Called by the CM dashboard on navigate (mirrors Reviews/Statistics).
        public async Task LoadAsync()
        {
            SelectedProvider = null;
            Providers.Clear();
            foreach (var p in await _providerService.GetAllAsync())
                Providers.Add(p);
        }

        [RelayCommand]
        private void NewProvider()
        {
            SelectedProvider = null;   // clear list selection first
            SaveError = string.Empty;
            CurrentEditor = new ProviderEditorViewModel(new Provider(), Providers);
        }

        [RelayCommand]
        private void CloseEditor()
        {
            CurrentEditor = null;
            SelectedProvider = null;
            SaveError = string.Empty;
        }

        private async Task LoadContactsAsync(int providerId, int request)
        {
            try
            {
                var contacts = await _providerService.GetContactsAsync(providerId);
                if (!_contactLoads.IsCurrent(request) ||
                    CurrentEditor?.Provider.Id != providerId)
                    return;

                ProviderContacts.Clear();
                foreach (var contact in contacts)
                    ProviderContacts.Add(contact);
                ClearContactEditor();
                RaiseContactListChanged();
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or CloudApiException)
            {
                if (_contactLoads.IsCurrent(request) &&
                    CurrentEditor?.Provider.Id == providerId)
                    SaveError = ex.Message;
            }
        }

        [RelayCommand]
        private void NewProviderContact()
        {
            if (!CanManageContacts)
                return;
            SelectedProviderContact = null;
            ClearContactEditor();
            IsContactEditorOpen = true;
        }

        [RelayCommand]
        private void CancelProviderContact() => ClearContactEditor();

        [RelayCommand]
        private async Task SaveProviderContact()
        {
            if (CurrentEditor?.Provider.Id is not > 0 || string.IsNullOrWhiteSpace(ContactName))
            {
                SaveError = "A contact needs a name.";
                return;
            }

            var contact = SelectedProviderContact ?? new ProviderContact
            {
                ProviderId = CurrentEditor.Provider.Id,
                SortOrder = ProviderContacts.Count
            };
            contact.Name = ContactName;
            contact.Role = ContactRole;
            contact.Phone = ContactPhone;
            contact.Extension = ContactExtension;
            contact.Email = ContactEmail;
            contact.IsPrimary = ContactIsPrimary;

            SaveError = string.Empty;
            try
            {
                await _providerService.SaveContactAsync(contact);
                await LoadContactsAsync(CurrentEditor.Provider.Id, _contactLoads.Begin());
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or CloudApiException)
            {
                SaveError = ex.Message;
            }
        }

        [RelayCommand]
        private async Task RemoveProviderContact(ProviderContact? contact)
        {
            if (CurrentEditor?.Provider.Id is not > 0 || contact is null)
                return;

            SaveError = string.Empty;
            try
            {
                await _providerService.RemoveContactAsync(CurrentEditor.Provider.Id, contact.Id);
                await LoadContactsAsync(CurrentEditor.Provider.Id, _contactLoads.Begin());
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or CloudApiException)
            {
                SaveError = ex.Message;
            }
        }

        private void ClearContactEditor()
        {
            SelectedProviderContact = null;
            ContactName = string.Empty;
            ContactRole = string.Empty;
            ContactPhone = string.Empty;
            ContactExtension = string.Empty;
            ContactEmail = string.Empty;
            ContactIsPrimary = false;
            IsContactEditorOpen = false;
            OnPropertyChanged(nameof(ContactEditorHeader));
        }

        private void RaiseContactListChanged()
        {
            OnPropertyChanged(nameof(HasProviderContacts));
            OnPropertyChanged(nameof(CanManageContacts));
        }

        // New (Id 0) → Add; existing → Update. Reload keeps the list name-ordered
        // after a rename and refreshes the just-saved row.
        //
        // A refused save leaves the editor open with the entered values intact. The
        // directory rules reject an edit rather than correcting it, so discarding the
        // work and closing would make the message useless.
        [RelayCommand]
        private async Task SaveProvider()
        {
            if (CurrentEditor is null)
                return;

            SaveError = string.Empty;
            var provider = CurrentEditor.Provider;
            try
            {
                if (provider.Id == 0)
                    await _providerService.AddAsync(provider);
                else
                    await _providerService.UpdateAsync(provider);
            }
            catch (Exception ex) when (ex is InvalidOperationException or CloudApiException)
            {
                SaveError = ex.Message;
                return;
            }

            await LoadAsync();
            CurrentEditor = null;
            SelectedProvider = null;
        }

        [RelayCommand]
        private async Task DeleteProvider()
        {
            if (CurrentEditor is null || CurrentEditor.Provider.Id == 0)
            {
                CurrentEditor = null;      // nothing persisted yet
                SelectedProvider = null;
                SaveError = string.Empty;
                return;
            }

            SaveError = string.Empty;
            try
            {
                await _providerService.DeleteAsync(CurrentEditor.Provider);
            }
            catch (Exception ex) when (ex is InvalidOperationException or CloudApiException)
            {
                SaveError = ex.Message;
                return;
            }

            await LoadAsync();
            CurrentEditor = null;
            SelectedProvider = null;
        }

        [RelayCommand]
        private async Task MergeProviderEntries()
        {
            var survivor = CurrentEditor?.Provider;
            var merged = MergeProvider;
            if (!CanMergeProviders || survivor is null || merged is null || survivor.Id == merged.Id)
                return;

            var confirmation = new ProviderMergeConfirmationEventArgs(
                survivor.Id,
                survivor.Name,
                merged.Id,
                merged.Name);
            MergeConfirmationRequested?.Invoke(this, confirmation);
            if (!confirmation.Confirmed)
                return;

            SaveError = string.Empty;
            NoticeMessage = string.Empty;
            try
            {
                var summary = await _providerService.MergeAsync(survivor.Id, merged.Id);
                await LoadAsync();
                SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == survivor.Id);
                NoticeMessage = summary;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or CloudApiException)
            {
                SaveError = ex.Message;
            }
        }
    }

    public sealed class ProviderMergeConfirmationEventArgs(
        int survivingProviderId,
        string survivingProviderName,
        int mergedProviderId,
        string mergedProviderName) : EventArgs
    {
        public int SurvivingProviderId { get; } = survivingProviderId;
        public string SurvivingProviderName { get; } = survivingProviderName;
        public int MergedProviderId { get; } = mergedProviderId;
        public string MergedProviderName { get; } = mergedProviderName;
        public string Message { get; } =
            $"Merge \"{mergedProviderName}\" into \"{survivingProviderName}\"? " +
            "Affiliations, consumer links, and named contacts will move to the surviving entry. " +
            "Existing documents will keep what they recorded. This cannot be undone.";
        public bool Confirmed { get; set; }
    }
}
