using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.ClientDocuments;

public sealed partial class PersonCenteredPlanViewModel : ObservableObject
{
    private static readonly (string Key, string Title)[] NarrativeSources =
    [
        ("good-life", "What a good life looks like"),
        ("strengths", "Strengths and resources to build upon"),
        ("priority-needs", "Priorities for the coming plan year"),
        ("communication-overview", "Communication preferences"),
        ("decision-support", "Decision-making support"),
        ("relationships", "Important relationships"),
        ("community-access", "Community access and belonging"),
        ("employment-learning", "Work, learning, and meaningful activity"),
        ("risks", "Risks requiring planning or support"),
        ("rights", "Rights, choices, access, and privacy")
    ];

    private readonly IPersonCenteredPlanSourceService _sourceService;
    private readonly ISessionService _session;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    [ObservableProperty] private string personName = "Select a consumer to begin.";
    [ObservableProperty] private bool hasPerson;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasSource;
    [ObservableProperty] private bool isProvisionalSource;
    [ObservableProperty] private string sourceTitle = "No assessment source";
    [ObservableProperty] private string sourceNotice = string.Empty;
    [ObservableProperty] private string sourceUpdatedText = string.Empty;

    public ObservableCollection<PlanContributorItem> Contributors { get; } = [];
    public ObservableCollection<PlanOutcomeCandidateItem> OutcomeCandidates { get; } = [];
    public ObservableCollection<PlanFoundationItem> Foundations { get; } = [];

    public bool HasContributors => Contributors.Count > 0;
    public bool HasOutcomeCandidates => OutcomeCandidates.Count > 0;
    public bool HasFoundations => Foundations.Count > 0;
    public bool HasPlanningContent => HasOutcomeCandidates || HasFoundations || HasContributors;

    public PersonCenteredPlanViewModel(
        IPersonCenteredPlanSourceService sourceService,
        ISessionService session)
    {
        _sourceService = sourceService;
        _session = session;
    }

    public async Task LoadPersonAsync(Person? person)
    {
        await _loadGate.WaitAsync();
        try
        {
            IsLoading = true;
            PersonName = person?.FullName ?? "Select a consumer to begin.";
            HasPerson = person is not null;
            ClearSource();

            var user = _session.CurrentUser;
            if (person is null || user is null)
                return;

            var source = await _sourceService.GetSourceAsync(person.Id, user.Id);
            if (source is null)
            {
                SourceNotice = "Begin the Comprehensive Assessment first. Identified needs and person-centered answers will appear here automatically.";
                return;
            }

            HasSource = true;
            IsProvisionalSource = source.Status != AssessmentStatus.Approved;
            SourceTitle = $"Comprehensive Assessment v{source.Version}";
            SourceUpdatedText = $"Updated {source.UpdatedAt.ToLocalTime():MMMM d, yyyy 'at' h:mm tt}";
            SourceNotice = IsProvisionalSource
                ? $"Working from the {FormatStatus(source.Status)} assessment. This information is provisional until supervisor approval."
                : "Working from the latest supervisor-approved assessment. The source version remains unchanged.";

            foreach (var contributor in source.Document.Contributors.Where(item =>
                         !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Relationship)))
            {
                Contributors.Add(new PlanContributorItem(contributor.Name, contributor.Relationship));
            }

            foreach (var need in source.Document.Needs.Where(item =>
                         !string.IsNullOrWhiteSpace(item.Description) || !string.IsNullOrWhiteSpace(item.DesiredResult)))
            {
                OutcomeCandidates.Add(new PlanOutcomeCandidateItem(
                    FormatNeedType(need.Type),
                    need.Description,
                    need.DesiredResult,
                    // The frozen triple, not just the name: the plan quotes what the assessment
                    // recorded at the time, affiliation included.
                    need.AssociateProvider ? need.DescribeProvider() : string.Empty));
            }

            foreach (var (key, title) in NarrativeSources)
            {
                if (!source.Document.Answers.TryGetValue(key, out var answer)
                    || answer.Status != AssessmentAnswerStatus.Answered
                    || string.IsNullOrWhiteSpace(answer.Narrative))
                    continue;

                Foundations.Add(new PlanFoundationItem(title, answer.Narrative.Trim()));
            }

            NotifyCollectionState();
        }
        finally
        {
            IsLoading = false;
            _loadGate.Release();
        }
    }

    private void ClearSource()
    {
        HasSource = false;
        IsProvisionalSource = false;
        SourceTitle = "No assessment source";
        SourceNotice = string.Empty;
        SourceUpdatedText = string.Empty;
        Contributors.Clear();
        OutcomeCandidates.Clear();
        Foundations.Clear();
        NotifyCollectionState();
    }

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasContributors));
        OnPropertyChanged(nameof(HasOutcomeCandidates));
        OnPropertyChanged(nameof(HasFoundations));
        OnPropertyChanged(nameof(HasPlanningContent));
    }

    private static string FormatStatus(AssessmentStatus status) => status switch
    {
        AssessmentStatus.ReadyForReview => "ready-for-review",
        AssessmentStatus.Returned => "returned",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string FormatNeedType(AssessmentNeedType type) => type switch
    {
        AssessmentNeedType.SkillDevelopment => "Skill development",
        AssessmentNeedType.AccessOrAccommodation => "Access or accommodation",
        AssessmentNeedType.HealthOrSafety => "Health or safety",
        AssessmentNeedType.RelationshipOrCommunity => "Relationship or community",
        AssessmentNeedType.ChoiceAutonomyOrRights => "Choice, autonomy, or rights",
        AssessmentNeedType.InformationPlanningOrDecisionSupport => "Information, planning, or decision support",
        _ => type.ToString()
    };
}

public sealed record PlanContributorItem(string Name, string Relationship);
public sealed record PlanFoundationItem(string Title, string Narrative);
public sealed record PlanOutcomeCandidateItem(
    string Type,
    string Need,
    string DesiredResult,
    string ProviderName)
{
    public bool HasNeed => !string.IsNullOrWhiteSpace(Need);
    public bool HasDesiredResult => !string.IsNullOrWhiteSpace(DesiredResult);
    public bool HasProvider => !string.IsNullOrWhiteSpace(ProviderName);
}
