using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

public partial class ScratchpadHistoryViewModel : ObservableObject
{
    private readonly IScratchpadService _scratchpadService;
    private readonly ISessionService _sessionService;

    private readonly ObservableCollection<Scratchpad> _entries = [];
    public ICollectionView EntriesView { get; }

    [ObservableProperty] private Scratchpad? selectedEntry;
    [ObservableProperty] private string? searchText;

    public string SelectedContent => SelectedEntry?.Content ?? string.Empty;

    partial void OnSelectedEntryChanged(Scratchpad? value) =>
        OnPropertyChanged(nameof(SelectedContent));

    partial void OnSearchTextChanged(string? value) =>
        EntriesView.Refresh();

    public ScratchpadHistoryViewModel(IScratchpadService scratchpadService, ISessionService sessionService)
    {
        _scratchpadService = scratchpadService;
        _sessionService = sessionService;

        EntriesView = CollectionViewSource.GetDefaultView(_entries);
        EntriesView.Filter = Filter;

        _ = LoadAsync();
    }

    private bool Filter(object obj)
    {
        if (obj is not Scratchpad entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return entry.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadAsync()
    {
        var userId = _sessionService.CurrentUser!.Id;
        var entries = await _scratchpadService.GetHistoryAsync(userId);
        foreach (var entry in entries)
            _entries.Add(entry);
    }
}