using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Helpers;
using Sati.Services;
using Sati.ViewModels.Children;
using System.Collections.ObjectModel;

namespace Sati.ViewModels;

public sealed partial class DailyAgendaItemViewModel : ObservableObject
{
    public DailyAgendaItemViewModel(DailyAgendaItem item) => Item = item;

    public DailyAgendaItem Item { get; }
    [ObservableProperty] private bool isSelected;

    public string Title => Item.Title;
    public string PersonName => Item.PersonName;
    public string DueText => Item.DueText;
    public bool BlocksBilling => Item.BlocksBilling;
    public bool IsOverdue => Item.IsOverdue;
    public string StatusCue => Item.IsOverdue
        ? Item.BlocksBilling ? "OVERDUE · BLOCKS BILLING" : "OVERDUE"
        : Item.Kind == DailyAgendaItemKind.SuggestedAssessment
            ? "SUGGESTED WORK"
            : "COMING UP";
    public string AutomationName =>
        $"{StatusCue}: {Title} for {PersonName}, {DueText}";
}

public sealed partial class DailyAgendaViewModel : ObservableObject
{
    private readonly ScratchpadViewModel _scratchpad;
    private readonly DateOnly _agendaDate;
    private bool _confirmed;

    public DailyAgendaViewModel(
        DailyAgendaBuildResult agenda,
        ScratchpadViewModel scratchpad,
        string displayName,
        string environmentLabel,
        bool isDemo,
        string assessmentProgressText,
        DateOnly agendaDate,
        int? greetingIndex = null)
    {
        _scratchpad = scratchpad;
        _agendaDate = agendaDate;
        EnvironmentLabel = environmentLabel;
        IsDemo = isDemo;
        AssessmentProgressText = assessmentProgressText;
        OverdueTotal = agenda.OverdueTotal;

        OverdueItems = CreateItems(agenda.OverdueItems);
        UpcomingItems = CreateItems(agenda.UpcomingItems);
        AssessmentItems = CreateItems(
            agenda.AssessmentSuggestion is null ? [] : [agenda.AssessmentSuggestion]);
        AllItems = [.. OverdueItems, .. UpcomingItems, .. AssessmentItems];
        foreach (var item in AllItems)
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DailyAgendaItemViewModel.IsSelected))
                    ConfirmCommand.NotifyCanExecuteChanged();
            };

        var set = AgendaGreetings.SelectSet(agenda);
        var selectedIndex = greetingIndex ?? Random.Shared.Next(AgendaGreetings.Count(set));
        Greeting = AgendaGreetings.Format(
            set,
            selectedIndex,
            displayName,
            agenda.LookaheadDays,
            agenda.AssessmentSuggestion?.PersonName);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler<DailyAgendaItem>? OpenRequested;

    public string Greeting { get; }
    public string EnvironmentLabel { get; }
    public bool IsDemo { get; }
    public string AssessmentProgressText { get; }
    public int OverdueTotal { get; }
    public bool HasOverdue => OverdueItems.Count > 0;
    public bool HasUpcoming => UpcomingItems.Count > 0;
    public bool HasAssessmentSuggestion => AssessmentItems.Count > 0;
    public bool HasNoItems => AllItems.Count == 0;
    public string OverdueSummary => OverdueTotal > OverdueItems.Count
        ? $"Showing {OverdueItems.Count} of {OverdueTotal} incomplete overdue forms, oldest first."
        : $"{OverdueTotal} incomplete overdue {(OverdueTotal == 1 ? "form" : "forms")}.";

    public ObservableCollection<DailyAgendaItemViewModel> OverdueItems { get; }
    public ObservableCollection<DailyAgendaItemViewModel> UpcomingItems { get; }
    public ObservableCollection<DailyAgendaItemViewModel> AssessmentItems { get; }
    public IReadOnlyList<DailyAgendaItemViewModel> AllItems { get; }

    private bool CanConfirm() => !_confirmed && AllItems.Any(item => item.IsSelected);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!CanConfirm())
            return;

        var lines = AllItems
            .Where(item => item.IsSelected)
            .Select(item => item.Item.ScratchpadLine)
            .ToList();
        _scratchpad.ScratchpadContent = AppendToTodaysWork(
            _scratchpad.ScratchpadContent,
            lines,
            _agendaDate);
        _confirmed = true;
        ConfirmCommand.NotifyCanExecuteChanged();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Skip() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenItem(DailyAgendaItemViewModel? item)
    {
        if (item is not null)
        {
            OpenRequested?.Invoke(this, item.Item);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static string AppendToTodaysWork(
        string? existing,
        IReadOnlyList<string> lines,
        DateOnly agendaDate)
    {
        if (lines.Count == 0)
            return existing ?? string.Empty;

        var additions = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(existing))
            return additions;

        var header = agendaDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d");
        return $"{existing.TrimEnd()}{Environment.NewLine}{Environment.NewLine}" +
               $"{header}{Environment.NewLine}{additions}";
    }

    private static ObservableCollection<DailyAgendaItemViewModel> CreateItems(
        IEnumerable<DailyAgendaItem> items) =>
        new(items.Select(item => new DailyAgendaItemViewModel(item)));
}
