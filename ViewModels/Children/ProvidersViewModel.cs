using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
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

        public ProvidersViewModel(IProviderService providerService)
        {
            _providerService = providerService;
        }

        public ObservableCollection<Provider> Providers { get; } = [];

        // The row picked in the list. Selecting one opens it in the editor via the
        // OnChanged callback below. Null when nothing is selected.
        [ObservableProperty] private Provider? selectedProvider;

        // The editor for the provider currently open (existing or brand-new), or
        // null = list-only. Same CurrentEditor idiom as the AT page.
        [ObservableProperty] private ProviderEditorViewModel? currentEditor;

        public bool IsEditing => CurrentEditor is not null;

        partial void OnCurrentEditorChanged(ProviderEditorViewModel? value)
            => OnPropertyChanged(nameof(IsEditing));

        // Selecting a list row opens it in a fresh editor. NOTE: switching rows
        // discards unsaved edits on the previous one — standard master-detail, but
        // worth knowing. NewProvider clears selection so it doesn't fight this.
        partial void OnSelectedProviderChanged(Provider? value)
        {
            CurrentEditor = value is null ? null : new ProviderEditorViewModel(value);
        }

        // Called by the CM dashboard on navigate (mirrors Reviews/Statistics).
        public async Task LoadAsync()
        {
            Providers.Clear();
            foreach (var p in await _providerService.GetAllAsync())
                Providers.Add(p);
        }

        [RelayCommand]
        private void NewProvider()
        {
            SelectedProvider = null;   // clear list selection first
            CurrentEditor = new ProviderEditorViewModel(new Provider());
        }

        [RelayCommand]
        private void CloseEditor()
        {
            CurrentEditor = null;
            SelectedProvider = null;
        }

        // New (Id 0) → Add; existing → Update. Reload keeps the list name-ordered
        // after a rename and refreshes the just-saved row.
        [RelayCommand]
        private async Task SaveProvider()
        {
            if (CurrentEditor is null)
                return;

            var provider = CurrentEditor.Provider;
            if (provider.Id == 0)
                await _providerService.AddAsync(provider);
            else
                await _providerService.UpdateAsync(provider);

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
                return;
            }

            await _providerService.DeleteAsync(CurrentEditor.Provider);
            await LoadAsync();
            CurrentEditor = null;
            SelectedProvider = null;
        }
    }
}