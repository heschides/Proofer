using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models.Assessments;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;

namespace Sati.ViewModels.ClientDocuments;

public sealed partial class ComprehensiveAssessmentViewModel : ObservableObject
{
    private readonly IComprehensiveAssessmentService _service;
    private readonly ISessionService _session;
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private ComprehensiveAssessment? _record;
    private AssessmentDocument _document = new();
    private bool _loading;

    [ObservableProperty] private string personName = "Select a consumer to begin.";
    [ObservableProperty] private bool hasPerson;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private string saveStatus = "Not loaded";
    [ObservableProperty] private AssessmentSectionViewModel? selectedSection;

    public ObservableCollection<AssessmentSectionViewModel> Sections { get; } = [];
    public ObservableCollection<AssessmentContributorViewModel> Contributors { get; } = [];
    public ObservableCollection<AssessmentNeedViewModel> Needs { get; } = [];
    public Array AnswerStatuses => Enum.GetValues<AssessmentAnswerStatus>();
    public Array NeedTypes => Enum.GetValues<AssessmentNeedType>();

    public ComprehensiveAssessmentViewModel(IComprehensiveAssessmentService service, ISessionService session)
    {
        _service = service;
        _session = session;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
        BuildSections();
        SelectedSection = Sections.FirstOrDefault();
    }

    partial void OnSelectedSectionChanged(AssessmentSectionViewModel? value) =>
        OnPropertyChanged(nameof(IsSummarySelected));
    public bool IsSummarySelected => SelectedSection?.Title == "Summary & needs";

    public async Task LoadPersonAsync(Person? person)
    {
        // Selection changes can arrive back-to-back. For example, saving an edited
        // client briefly clears SelectedPerson before restoring it. Serialize those
        // loads so an older request cannot clear or overwrite a newer request's state.
        await _loadGate.WaitAsync();
        try
        {
            _saveTimer.Stop();
            if (_record is not null) await SaveAsync();

            _loading = true;
            try
            {
                _record = null;
                _document = new();
                PersonName = person?.FullName ?? "Select a consumer to begin.";
                HasPerson = person is not null;
                var user = _session.CurrentUser;
                CanEdit = person is not null && user is not null && person.UserId == user.Id;
                ClearAnswers();
                Contributors.Clear();
                Needs.Clear();
                if (person is null || user is null) { SaveStatus = "Not loaded"; return; }
                if (!CanEdit) { SaveStatus = "Read only — this consumer is not on your caseload"; return; }

                var record = await _service.GetOrCreateDraftAsync(person.Id, user.Id);
                var document = JsonSerializer.Deserialize<AssessmentDocument>(record.DocumentJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();

                _record = record;
                _document = document;
                ApplyDocument();
                SaveStatus = $"Draft v{record.Version} · All changes saved";
            }
            finally
            {
                _loading = false;
                RefreshProgress();
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    [RelayCommand]
    public void AddContributor()
    {
        if (!CanEdit) return;
        var contributor = new AssessmentContributorViewModel(new AssessmentContributor(), ScheduleSave, RemoveContributor);
        Contributors.Add(contributor);
        ScheduleSave();
    }

    private void RemoveContributor(AssessmentContributorViewModel contributor)
    {
        Contributors.Remove(contributor);
        ScheduleSave();
    }

    [RelayCommand]
    public void AddNeed()
    {
        if (!CanEdit) return;
        Needs.Add(new AssessmentNeedViewModel(new AssessmentNeed(), ScheduleSave, RemoveNeed));
        ScheduleSave();
    }

    private void RemoveNeed(AssessmentNeedViewModel need) { Needs.Remove(need); ScheduleSave(); }

    private void ApplyDocument()
    {
        foreach (var section in Sections)
        foreach (var question in section.Questions)
            if (_document.Answers.TryGetValue(question.Key, out var answer)) question.Load(answer);

        foreach (var contributor in _document.Contributors)
            Contributors.Add(new AssessmentContributorViewModel(contributor, ScheduleSave, RemoveContributor));
        foreach (var need in _document.Needs)
            Needs.Add(new AssessmentNeedViewModel(need, ScheduleSave, RemoveNeed));
    }

    private void ClearAnswers()
    {
        foreach (var section in Sections)
        foreach (var question in section.Questions) question.Load(new AssessmentAnswer());
    }

    private void ScheduleSave()
    {
        if (_loading || !CanEdit || _record is null) return;
        SaveStatus = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshProgress();
    }

    private async Task SaveAsync()
    {
        var record = _record;
        if (_loading || !CanEdit || record is null) return;

        // Everything used after the database await is captured locally. A subsequent
        // client-selection notification may replace or clear the ViewModel fields while
        // this save is in flight, but it cannot invalidate this operation's snapshot.
        var document = new AssessmentDocument
        {
            Contributors = Contributors.Select(c => c.ToModel()).ToList(),
            Needs = Needs.Select(n => n.ToModel()).ToList(),
            Answers = Sections.SelectMany(s => s.Questions)
                .ToDictionary(q => q.Key, q => q.ToModel())
        };

        await _saveGate.WaitAsync();
        try
        {
            await _service.SaveDocumentAsync(record.Id, document);

            // Do not let completion of an old client's save overwrite the status or
            // document belonging to a client that was selected in the meantime.
            if (ReferenceEquals(_record, record))
            {
                _document = document;
                SaveStatus = $"Draft v{record.Version} · All changes saved";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_record, record))
                SaveStatus = $"Could not save: {ex.Message}";
        }
        finally
        {
            _saveGate.Release();
        }
    }

    [RelayCommand]
    private async Task SubmitForReviewAsync()
    {
        var record = _record;
        var user = _session.CurrentUser;
        if (record is null || user is null) return;
        if (!IsComplete)
        {
            SaveStatus = "Every question must be addressed before submission.";
            return;
        }
        await SaveAsync();

        // A selection change during the save means this command no longer owns the
        // visible record. Do not submit whichever assessment happened to load next.
        if (!ReferenceEquals(_record, record)) return;

        await _service.SubmitForReviewAsync(record.Id, user.Id);
        CanEdit = false;
        SaveStatus = "Submitted for supervisor review";
    }

    private void RefreshProgress()
    {
        foreach (var section in Sections) section.RefreshProgress();
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CompletionText));
    }

    public int AnsweredCount => Sections.Sum(s => s.Questions.Count(q => q.IsAddressed));
    public int TotalCount => Sections.Sum(s => s.Questions.Count);
    public bool IsComplete => HasPerson && TotalCount > 0 && AnsweredCount == TotalCount &&
        Sections.SelectMany(s => s.Questions).All(q => q.Status != AssessmentAnswerStatus.FollowUpRequired);
    public string CompletionText => $"{AnsweredCount} of {TotalCount} questions addressed";

    private void BuildSections()
    {
        AddSection("Getting started", "People & context",
            Q("good-life", "What does a good life look like to this person?",
              "Describe the person’s own priorities, routines, relationships, places, and experiences that make life meaningful.",
              "Concrete preferences and examples in everyday language.", "Happy, appropriate, doing well, or generic service goals without examples."),
            Q("communication", "What must others know to communicate successfully with the person?",
              "Explain how the person expresses choices, agreement, refusal, discomfort, and the need for a break.",
              "Methods that work, signs others may miss, processing time, and useful accommodations.", "Nonverbal, poor communicator, or guardian speaks for them without describing the person."));

        AddSection("Home & daily life", "Home, routines & personal activities",
            SQ("home-routines", "How does the person manage daily routines at home?",
               "Describe what the person does, where support enters the routine, and what happens when it is unavailable.",
               "Morning/evening routines, meals, household tasks, privacy, and differences between settings.", "Needs help with ADLs without naming the activities or support."),
            SQ("personal-care", "What support is used for personal care?",
               "Include only relevant activities and describe support in respectful, observable terms.",
               "Bathing, dressing, toileting, grooming, menstrual care, and how choice and privacy are protected.", "Independent/dependent alone, or unnecessary intimate detail."));

        AddSection("Health & wellness", "Physical, behavioral & emotional health",
            SQ("health-management", "How are healthcare and medications managed?",
               "Describe the person’s role and the actual supports used to access care and follow recommendations.",
               "Appointments, decisions, medications, communication with clinicians, preventive care, and barriers.", "List diagnoses without explaining their practical effect."),
            Q("wellness", "What helps the person remain emotionally and physically well?",
              "Describe known protective routines, early signs of distress, and responses that actually help.",
              "Sleep, movement, food, sensory regulation, trusted people, coping strategies, and seasonal patterns.", "Calm down, redirect, or monitor behavior without saying how."));

        AddSection("Safety & rights", "Risk, safeguards, autonomy & restrictions",
            Q("risks", "What current risks require planning or support?",
              "Use factual, individualized information and distinguish possibility from established history.",
              "Trigger, likelihood, consequence, existing safeguard, whether it works, and a backup response.", "Vague labels such as unsafe, elopes, noncompliant, or aggressive without context."),
            Q("rights", "Are any rights, choices, access, or privacy currently restricted?",
              "Identify any rule or practice that limits ordinary access or choice, even if intended for safety.",
              "Specific assessed need, less restrictive approaches tried, consent, review date, and plan to reduce it.", "House rules or provider policy as the sole justification."));

        AddSection("Community & relationships", "Belonging, transportation & social connection",
            SQ("community-access", "How does the person access places and activities they choose?",
               "Describe actual access, not merely what is available in theory.",
               "Chosen destinations, transportation, scheduling, cost, staffing, accessibility, and barriers.", "Has community support or goes into the community without frequency, choice, or barriers."),
            Q("relationships", "Which relationships matter, and what support is wanted to maintain or develop them?",
              "Include paid and unpaid relationships while respecting privacy and the person’s preferences.",
              "Who matters, desired contact, barriers, boundaries, intimacy, and unwanted isolation.", "Family involved or socializes with staff as a complete answer."));

        AddSection("Learning & work", "Employment, education & skill development",
            SQ("employment-learning", "What does the person want regarding work, learning, or meaningful activity?",
               "Start with the person’s interests and desired life outcome before describing services.",
               "Current experience, preferences, strengths, barriers, accommodations, benefits concerns, and next step.", "Not interested unless options were meaningfully explored and the context is documented."));

        AddSection("Choice & advocacy", "Decisions, control & self-advocacy",
            SQ("decision-support", "How does the person make decisions and what support helps?",
               "Describe decision-making by topic rather than treating capacity as all-or-nothing.",
               "How choices are presented, processing time, trusted supporters, risks understood, and final decision-maker.", "Guardian makes decisions without describing the person’s participation."),
            Q("dissent", "How does the person show disagreement, refusal, or a desire for change?",
              "Describe signals and the response expected from supporters.",
              "Words, gestures, behavior, communication technology, escalation signs, and how choices are honored.", "Behaviors when told no without considering whether the person is communicating refusal."));

        AddSection("Summary & needs", "Strengths, unmet needs & priorities",
            Q("strengths", "Which strengths, resources, and supports should the plan build upon?",
              "Identify capabilities and resources that are currently useful, not compliments detached from planning.",
              "Skills, interests, relationships, technology, community resources, routines, and successful strategies.", "Sweet, nice, high-functioning, or resilient without practical examples."),
            Q("priority-needs", "What needs attention during the coming plan year?",
              "Include material needs and broader support, access, autonomy, health, relationship, or planning needs.",
              "What is missing, desired result, urgency, responsible next step, and whether a provider should be associated.", "Needs more services without explaining the need or desired result."));
    }

    private AssessmentQuestionViewModel Q(string key, string prompt, string why, string include, string avoid) =>
        new(key, prompt, why, include, avoid, false, ScheduleSave);
    private AssessmentQuestionViewModel SQ(string key, string prompt, string why, string include, string avoid) =>
        new(key, prompt, why, include, avoid, true, ScheduleSave);
    private void AddSection(string title, string subtitle, params AssessmentQuestionViewModel[] questions) =>
        Sections.Add(new AssessmentSectionViewModel(title, subtitle, questions));
}

public sealed partial class AssessmentSectionViewModel(string title, string subtitle, IEnumerable<AssessmentQuestionViewModel> questions) : ObservableObject
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public ObservableCollection<AssessmentQuestionViewModel> Questions { get; } = new(questions);
    public string ProgressText => $"{Questions.Count(q => q.IsAddressed)}/{Questions.Count}";
    public void RefreshProgress() => OnPropertyChanged(nameof(ProgressText));
}

public sealed partial class AssessmentQuestionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;
    public string Key { get; }
    public string Prompt { get; }
    public string WhyAsked { get; }
    public string CompleteAnswerIncludes { get; }
    public string Avoid { get; }
    public bool UsesSupports { get; }

    [ObservableProperty] private AssessmentAnswerStatus status = AssessmentAnswerStatus.FollowUpRequired;
    [ObservableProperty] private string narrative = string.Empty;
    [ObservableProperty] private string supportDetails = string.Empty;
    [ObservableProperty] private string exceptionReason = string.Empty;
    [ObservableProperty] private string dissentingOpinion = string.Empty;
    [ObservableProperty] private bool setupOrEnvironmental;
    [ObservableProperty] private bool promptingOrCoaching;
    [ObservableProperty] private bool handsOnAssistance;
    [ObservableProperty] private bool anotherPersonCompletes;
    [ObservableProperty] private bool varies;
    [ObservableProperty] private bool noSupportCurrentlyNeeded;

    public AssessmentQuestionViewModel(string key, string prompt, string why, string include, string avoid, bool usesSupports, Action changed)
    { Key = key; Prompt = prompt; WhyAsked = why; CompleteAnswerIncludes = include; Avoid = avoid; UsesSupports = usesSupports; _changed = changed; }

    partial void OnStatusChanged(AssessmentAnswerStatus value)
    {
        OnPropertyChanged(nameof(ExceptionReasonPrompt));
        Changed();
    }
    partial void OnNarrativeChanged(string value) => Changed();
    partial void OnSupportDetailsChanged(string value) => Changed();
    partial void OnExceptionReasonChanged(string value) => Changed();
    partial void OnDissentingOpinionChanged(string value) => Changed();
    partial void OnSetupOrEnvironmentalChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnPromptingOrCoachingChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnHandsOnAssistanceChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnAnotherPersonCompletesChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnVariesChanged(bool value) { ClearNoSupport(value); Changed(); }
    partial void OnNoSupportCurrentlyNeededChanged(bool value)
    {
        if (value)
        {
            SetupOrEnvironmental = PromptingOrCoaching = HandsOnAssistance = AnotherPersonCompletes = Varies = false;
            SupportDetails = string.Empty;
        }
        Changed();
    }
    private void ClearNoSupport(bool selected) { if (selected) NoSupportCurrentlyNeeded = false; }
    private void Changed() { if (!_loading) { OnPropertyChanged(nameof(IsAddressed)); OnPropertyChanged(nameof(ShowExceptionReason)); _changed(); } }

    public bool ShowExceptionReason => Status != AssessmentAnswerStatus.Answered;
    public string ExceptionReasonPrompt => Status switch
    {
        AssessmentAnswerStatus.FollowUpRequired => "Explain why follow-up is required.",
        AssessmentAnswerStatus.UnableToAssess => "Explain why this item could not be assessed.",
        AssessmentAnswerStatus.Declined => "Explain why an answer was declined.",
        AssessmentAnswerStatus.NotApplicable => "Explain why this item is not applicable.",
        _ => "Additional explanation"
    };
    public bool HasConcreteSupport => SetupOrEnvironmental || PromptingOrCoaching || HandsOnAssistance || AnotherPersonCompletes;
    public bool IsAddressed => Status switch
    {
        AssessmentAnswerStatus.Answered => !string.IsNullOrWhiteSpace(Narrative)
            && (!UsesSupports || NoSupportCurrentlyNeeded || HasConcreteSupport)
            && (!Varies || (HasConcreteSupport && !string.IsNullOrWhiteSpace(SupportDetails))),
        AssessmentAnswerStatus.FollowUpRequired => false,
        _ => !string.IsNullOrWhiteSpace(ExceptionReason)
    };

    public void Load(AssessmentAnswer answer)
    {
        _loading = true;
        Status = answer.Status; Narrative = answer.Narrative; SupportDetails = answer.SupportDetails;
        ExceptionReason = answer.ExceptionReason; DissentingOpinion = answer.DissentingOpinion;
        SetupOrEnvironmental = answer.Supports.HasFlag(SupportMethod.SetupOrEnvironmental);
        PromptingOrCoaching = answer.Supports.HasFlag(SupportMethod.PromptingOrCoaching);
        HandsOnAssistance = answer.Supports.HasFlag(SupportMethod.HandsOnAssistance);
        AnotherPersonCompletes = answer.Supports.HasFlag(SupportMethod.AnotherPersonCompletes);
        Varies = answer.Supports.HasFlag(SupportMethod.Varies);
        NoSupportCurrentlyNeeded = answer.Supports.HasFlag(SupportMethod.NoSupportCurrentlyNeeded);
        _loading = false; OnPropertyChanged(string.Empty);
    }

    public AssessmentAnswer ToModel()
    {
        var supports = SupportMethod.None;
        if (SetupOrEnvironmental) supports |= SupportMethod.SetupOrEnvironmental;
        if (PromptingOrCoaching) supports |= SupportMethod.PromptingOrCoaching;
        if (HandsOnAssistance) supports |= SupportMethod.HandsOnAssistance;
        if (AnotherPersonCompletes) supports |= SupportMethod.AnotherPersonCompletes;
        if (Varies) supports |= SupportMethod.Varies;
        if (NoSupportCurrentlyNeeded) supports |= SupportMethod.NoSupportCurrentlyNeeded;
        return new() { Status = Status, Narrative = Narrative, Supports = supports, SupportDetails = SupportDetails, ExceptionReason = ExceptionReason, DissentingOpinion = DissentingOpinion };
    }
}

public sealed partial class AssessmentContributorViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<AssessmentContributorViewModel> _remove;
    public Guid Id { get; }
    [ObservableProperty] private string name;
    [ObservableProperty] private string relationship;
    public AssessmentContributorViewModel(AssessmentContributor model, Action changed, Action<AssessmentContributorViewModel> remove)
    { Id = model.Id; name = model.Name; relationship = model.Relationship; _changed = changed; _remove = remove; }
    partial void OnNameChanged(string value) => _changed();
    partial void OnRelationshipChanged(string value) => _changed();
    [RelayCommand]
    public void Remove() => _remove(this);
    public AssessmentContributor ToModel() => new() { Id = Id, Name = Name, Relationship = Relationship };
}

public sealed partial class AssessmentNeedViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<AssessmentNeedViewModel> _remove;
    public Guid Id { get; }
    [ObservableProperty] private AssessmentNeedType type;
    [ObservableProperty] private string description;
    [ObservableProperty] private string desiredResult;
    [ObservableProperty] private bool associateProvider;
    [ObservableProperty] private string providerNameSnapshot;

    public AssessmentNeedViewModel(AssessmentNeed model, Action changed, Action<AssessmentNeedViewModel> remove)
    {
        Id = model.Id; type = model.Type; description = model.Description; desiredResult = model.DesiredResult;
        associateProvider = model.AssociateProvider; providerNameSnapshot = model.ProviderNameSnapshot;
        _changed = changed; _remove = remove;
    }
    partial void OnTypeChanged(AssessmentNeedType value) => _changed();
    partial void OnDescriptionChanged(string value) => _changed();
    partial void OnDesiredResultChanged(string value) => _changed();
    partial void OnAssociateProviderChanged(bool value) => _changed();
    partial void OnProviderNameSnapshotChanged(string value) => _changed();
    [RelayCommand] public void Remove() => _remove(this);
    public AssessmentNeed ToModel() => new()
    {
        Id = Id, Type = Type, Description = Description, DesiredResult = DesiredResult,
        AssociateProvider = AssociateProvider, ProviderNameSnapshot = ProviderNameSnapshot
    };
}
