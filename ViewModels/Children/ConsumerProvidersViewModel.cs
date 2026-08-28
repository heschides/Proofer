using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;

namespace Sati.ViewModels.Children;

/// <summary>
/// One consumer's medical provider list.
/// <para>
/// The practice and the network are never stored on a row here. They are resolved from the
/// agency directory every time the list is built, so correcting a directory entry corrects
/// every consumer who names it. That is the whole reason this panel loads the directory
/// alongside the links.
/// </para>
/// <para>
/// Tidiness comes from state rather than a cap: current providers are listed, ended ones sit
/// behind a disclosure, and ending a relationship keeps the row.
/// </para>
/// </summary>
public partial class ConsumerProvidersViewModel : ObservableObject
{
    // Selection-driven loads race. Click three consumers quickly and the slowest response
    // would otherwise publish one consumer's providers under another's name.
    private readonly LatestRequestTracker _loads = new();
    private readonly IConsumerProviderService _linkService;
    private readonly IProviderService _providerService;
    private readonly Func<DateTime> _today;

    private List<Provider> _directory = [];
    private List<ProviderAffiliationNode> _nodes = [];
    private int? _personId;

    // The free-text fields that predate the directory. Held so the panel can offer to link
    // them; never written to, and never cleared — the typed value is the only record of what
    // somebody actually entered.
    private string? _legacyPrimaryCare;
    private string? _legacyHealthcareSystem;

    public ConsumerProvidersViewModel(
        IConsumerProviderService linkService,
        IProviderService providerService)
        : this(linkService, providerService, () => DateTime.Today)
    {
    }

    internal ConsumerProvidersViewModel(
        IConsumerProviderService linkService,
        IProviderService providerService,
        Func<DateTime> today)
    {
        _linkService = linkService;
        _providerService = providerService;
        _today = today;
    }

    /// <summary>Current relationships, primary care first.</summary>
    public ObservableCollection<ConsumerProviderRowViewModel> Current { get; } = [];

    /// <summary>Ended relationships. Kept, not deleted — and collapsed by default.</summary>
    public ObservableCollection<ConsumerProviderRowViewModel> Past { get; } = [];

    /// <summary>Directory entries offered in the picker, individuals first.</summary>
    public ObservableCollection<Provider> ProviderOptions { get; } = [];

    [ObservableProperty] private bool hasLoadedPerson;
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProviderAffiliation))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProviderAffiliation))]
    private int? newProviderId;

    [ObservableProperty] private string newRole = string.Empty;
    [ObservableProperty] private bool newIsPrimaryCare;
    [ObservableProperty] private bool newHasActiveRelease;
    [ObservableProperty] private bool showPast;

    public bool HasStatusMessage => StatusMessage.Length > 0;
    public bool HasCurrent => Current.Count > 0;
    public bool HasPast => Past.Count > 0;
    public bool CanAdd => HasLoadedPerson && NewProviderId is > 0 && !IsBusy;

    public string PastDisclosureLabel => Past.Count == 1
        ? "1 past provider"
        : $"{Past.Count} past providers";

    /// <summary>
    /// The chain above the provider being added, shown live under the picker so the case
    /// manager can see they picked the right clinician before committing.
    /// </summary>
    public string SelectedProviderAffiliation => NewProviderId is { } id
        ? ProviderAffiliation.DescribeAffiliation(id, _nodes)
        : string.Empty;

    public bool HasSelectedProviderAffiliation => SelectedProviderAffiliation.Length > 0;

    partial void OnNewProviderIdChanged(int? value) => OnPropertyChanged(nameof(CanAdd));
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanLinkLegacyPrimaryCare));
    }
    partial void OnHasLoadedPersonChanged(bool value) => OnPropertyChanged(nameof(CanAdd));

    /// <summary>
    /// Points the panel at a consumer. Every call takes a request identity, so a slow load
    /// for a consumer the case manager has already navigated away from is discarded rather
    /// than written over the newer one.
    /// </summary>
    /// <remarks>
    /// Takes the consumer rather than an id because the panel also reports on the two free-text
    /// fields that predate the directory, and those live on the record.
    /// </remarks>
    public void SetPerson(Person? person)
    {
        var request = _loads.Begin();

        Current.Clear();
        Past.Clear();
        ProviderOptions.Clear();
        _directory = [];
        _nodes = [];
        ClearEditor();
        StatusMessage = string.Empty;
        _personId = person?.Id;
        _legacyPrimaryCare = person?.PrimaryCareProvider;
        _legacyHealthcareSystem = person?.HealthcareSystemName;
        HasLoadedPerson = person is not null;
        RaiseListChanged();

        if (person is not null)
            _ = LoadAsync(person.Id, request);
    }

    public async Task RefreshAsync()
    {
        if (_personId is { } id)
            await LoadAsync(id, _loads.Begin());
    }

    private async Task LoadAsync(int personId, int request)
    {
        try
        {
            var directory = await _providerService.GetAllAsync();
            var links = await _linkService.GetByPersonAsync(personId);

            if (!_loads.IsCurrent(request) || _personId != personId)
                return;

            _directory = directory;
            _nodes = directory.ToAffiliationNodes();

            ProviderOptions.Clear();
            // Individuals first: a consumer's provider is normally a named clinician, and
            // the practice is what you pick when there is no named one.
            foreach (var provider in directory
                         .Where(candidate => candidate.Type == ProviderType.Healthcare)
                         .OrderBy(candidate => candidate.MedicalKind switch
                         {
                             MedicalProviderKind.Individual => 0,
                             MedicalProviderKind.Practice => 1,
                             _ => 2
                         })
                         .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ProviderOptions.Add(provider);
            }

            Populate(links);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or UnauthorizedAccessException
                                              or CloudApiException)
        {
            if (_loads.IsCurrent(request) && _personId == personId)
                StatusMessage = exception.Message;
        }
    }

    private void Populate(IEnumerable<PersonProvider> links)
    {
        Current.Clear();
        Past.Clear();

        var rows = links.Select(BuildRow).ToList();
        foreach (var row in ConsumerProviderRules.OrderForDisplay(
                     rows.Where(row => row.IsCurrent),
                     row => row.IsPrimaryCare,
                     row => row.SortOrder,
                     row => row.ProviderName))
        {
            Current.Add(row);
        }

        // Most recently ended first: the provider someone stopped seeing last month is the
        // one a case manager is most likely to be looking for.
        foreach (var row in rows.Where(row => !row.IsCurrent)
                     .OrderByDescending(row => row.EndDate ?? DateTime.MinValue)
                     .ThenBy(row => row.ProviderName, StringComparer.CurrentCultureIgnoreCase))
        {
            Past.Add(row);
        }

        RaiseListChanged();
    }

    private ConsumerProviderRowViewModel BuildRow(PersonProvider link)
    {
        var provider = _directory.FirstOrDefault(candidate => candidate.Id == link.ProviderId);
        var practice = ProviderAffiliation.NearestAncestorOfKind(
            link.ProviderId, MedicalProviderKind.Practice, _nodes);
        var network = ProviderAffiliation.NearestAncestorOfKind(
            link.ProviderId, MedicalProviderKind.Network, _nodes);

        return new ConsumerProviderRowViewModel(
            link,
            // A directory entry the case manager cannot see is named rather than blank, so a
            // profile row never renders as an unexplained empty line.
            provider?.Name ?? "Provider no longer in the directory",
            practice?.Name ?? string.Empty,
            network?.Name ?? string.Empty);
    }

    [RelayCommand]
    private async Task AddProvider()
    {
        if (_personId is not { } personId || NewProviderId is not { } providerId)
            return;

        var link = new PersonProvider
        {
            PersonId = personId,
            ProviderId = providerId,
            Role = string.IsNullOrWhiteSpace(NewRole) ? null : NewRole.Trim(),
            IsPrimaryCare = NewIsPrimaryCare,
            HasActiveRelease = NewHasActiveRelease,
            StartDate = _today(),
            SortOrder = Current.Count
        };

        if (await RunAsync(() => _linkService.SaveAsync(link)))
            ClearEditor();
    }

    [RelayCommand]
    private async Task EndProvider(ConsumerProviderRowViewModel? row)
    {
        if (row is null || _personId is not { } personId)
            return;

        // Ended, not deleted. The row stays so the record can still answer who was treating
        // this consumer in a given year.
        await RunAsync(() => _linkService.EndAsync(personId, row.Id, _today()));
    }

    [RelayCommand]
    private async Task RemoveProvider(ConsumerProviderRowViewModel? row)
    {
        if (row is null || _personId is not { } personId)
            return;

        // For a link recorded against the wrong consumer. Ending is the command for a
        // relationship that really happened.
        await RunAsync(() => _linkService.RemoveAsync(personId, row.Id));
    }

    private async Task<bool> RunAsync(Func<Task> operation)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or UnauthorizedAccessException
                                              or CloudApiException)
        {
            // The rules reject an edit rather than correcting it, so the entered values stay
            // on screen with the reason beside them.
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
        return true;
    }

    private void ClearEditor()
    {
        NewProviderId = null;
        NewRole = string.Empty;
        NewIsPrimaryCare = false;
        NewHasActiveRelease = false;
    }

    // ── Linking the free-text fields that predate the directory ──────────────
    //
    // Nothing here is written automatically. A bulk name-match backfill across live consumer
    // records is exactly the operation that should not run unreviewed, and a wrong provider on
    // a medical record is worse than an unlinked one: unlinked is visibly unfinished, wrong
    // looks finished. The panel proposes; a case manager confirms one consumer at a time.
    //
    // The legacy strings are never cleared either way. They are the only record of what
    // somebody actually typed, and a link is an addition beside them, not a replacement.

    private LegacyProviderMatch _legacyMatch;

    /// <summary>
    /// True when free text names a primary care provider and no current link says the same.
    /// A consumer with a current primary-care link has nothing left to reconcile.
    /// </summary>
    public bool NeedsPrimaryCareLinking =>
        _legacyMatch.Outcome != LegacyMatchOutcome.NoLegacyValue &&
        !Current.Any(row => row.IsPrimaryCare);

    public string PrimaryCareLinkGuidance => LegacyProviderLinking.PrimaryCareGuidance(_legacyMatch);

    /// <summary>Only an unambiguous single match can be linked in one click.</summary>
    public bool CanLinkLegacyPrimaryCare => NeedsPrimaryCareLinking && _legacyMatch.CanLink && !IsBusy;

    public string LinkLegacyPrimaryCareLabel => $"Link {_legacyMatch.ProviderName}";

    /// <summary>
    /// Whether the typed healthcare system still agrees with the network the linked provider
    /// resolves to. A disagreement is surfaced rather than silently resolved: one of the two is
    /// stale and only a person knows which.
    /// </summary>
    public string HealthcareSystemGuidance => LegacyProviderLinking.HealthcareSystemGuidance(
        _legacyHealthcareSystem,
        Current.FirstOrDefault(row => row.IsPrimaryCare)?.NetworkName
            ?? Current.FirstOrDefault(row => row.NetworkName.Length > 0)?.NetworkName);

    public bool HasHealthcareSystemGuidance => HealthcareSystemGuidance.Length > 0;

    [RelayCommand]
    private async Task LinkLegacyPrimaryCare()
    {
        if (_personId is not { } personId || !_legacyMatch.CanLink)
            return;

        // Recorded as primary care because that is what the legacy field meant. No start date:
        // when the relationship began is not something the free text ever knew, and inventing
        // today would assert a fact nobody entered.
        await RunAsync(() => _linkService.SaveAsync(new PersonProvider
        {
            PersonId = personId,
            ProviderId = _legacyMatch.ProviderId,
            IsPrimaryCare = true,
            SortOrder = 0
        }));
    }

    private void RefreshLegacyReconciliation()
    {
        _legacyMatch = LegacyProviderLinking.Match(_legacyPrimaryCare, _nodes);
        OnPropertyChanged(nameof(NeedsPrimaryCareLinking));
        OnPropertyChanged(nameof(PrimaryCareLinkGuidance));
        OnPropertyChanged(nameof(CanLinkLegacyPrimaryCare));
        OnPropertyChanged(nameof(LinkLegacyPrimaryCareLabel));
        OnPropertyChanged(nameof(HealthcareSystemGuidance));
        OnPropertyChanged(nameof(HasHealthcareSystemGuidance));
    }

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(HasPast));
        OnPropertyChanged(nameof(PastDisclosureLabel));
        RefreshLegacyReconciliation();
    }
}

/// <summary>
/// One row of the list. The practice and network are passed in already resolved — the row
/// holds no directory of its own, so it cannot resolve them a second, different way.
/// </summary>
public sealed class ConsumerProviderRowViewModel(
    PersonProvider link, string providerName, string practiceName, string networkName)
{
    public int Id { get; } = link.Id;
    public int ProviderId { get; } = link.ProviderId;
    public string ProviderName { get; } = providerName;
    public string PracticeName { get; } = practiceName;
    public string NetworkName { get; } = networkName;
    public string? Role { get; } = link.Role;
    public bool IsPrimaryCare { get; } = link.IsPrimaryCare;
    public bool HasActiveRelease { get; } = link.HasActiveRelease;
    public int SortOrder { get; } = link.SortOrder;
    public DateTime? StartDate { get; } = link.StartDate;
    public DateTime? EndDate { get; } = link.EndDate;
    public bool IsCurrent { get; } = link.IsActive;

    /// <summary>
    /// "Coastal Women's Healthcare · MaineHealth", or empty when the provider stands alone.
    /// Read-only in the interface: an editable derived value is a stored copy in disguise.
    /// </summary>
    public string Affiliation { get; } = string.Join(
        " · ", new[] { practiceName, networkName }.Where(part => part.Length > 0));

    public bool HasAffiliation => Affiliation.Length > 0;

    public string RoleLabel => string.IsNullOrWhiteSpace(Role)
        ? (IsPrimaryCare ? "Primary care" : "Role not recorded")
        : Role;

    /// <summary>
    /// The status a screen reader announces and a non-colour cue for sighted users, so
    /// "ended" never depends on noticing a shade of grey.
    /// </summary>
    public string StatusLabel => IsCurrent
        ? (IsPrimaryCare ? "Current · primary care" : "Current")
        : EndDate is { } ended ? $"Ended {ended:d MMM yyyy}" : "Ended";

    public string AutomationName =>
        $"{ProviderName}, {RoleLabel}, {StatusLabel}" +
        (HasAffiliation ? $", {Affiliation}" : string.Empty);
}
