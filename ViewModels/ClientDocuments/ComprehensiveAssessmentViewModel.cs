using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Assessments;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;

namespace Sati.ViewModels.ClientDocuments;

public sealed partial class ComprehensiveAssessmentViewModel : ObservableObject
{
    private readonly IComprehensiveAssessmentService _service;
    private readonly ISessionService _session;
    // The consumer's own provider list, and the directory it resolves against. A need is
    // associated with somebody the consumer actually sees, and the practice and network are
    // frozen onto the document at the moment of choosing.
    private readonly IConsumerProviderService _consumerProviders;
    private readonly IProviderService _providers;
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

    /// <summary>
    /// The providers a need may be associated with: the ones this consumer actually sees, each
    /// carrying its resolved practice and network. Shared by every need row rather than rebuilt
    /// per row, so one load answers for all of them.
    /// </summary>
    public ObservableCollection<AssessmentProviderOption> ProviderOptions { get; } = [];

    public bool HasProviderOptions => ProviderOptions.Count > 0;
    public Array AnswerStatuses => Enum.GetValues<AssessmentAnswerStatus>();
    public Array NeedTypes => Enum.GetValues<AssessmentNeedType>();
    public Array TherapySessionFormats => Enum.GetValues<TherapySessionFormat>().Cast<TherapySessionFormat>()
        .Where(value => value != TherapySessionFormat.NotSelected).ToArray();
    public Array TherapyFrequencyDirections => Enum.GetValues<TherapyFrequencyDirection>().Cast<TherapyFrequencyDirection>()
        .Where(value => value != TherapyFrequencyDirection.NotSelected).ToArray();

    public ComprehensiveAssessmentViewModel(
        IComprehensiveAssessmentService service,
        ISessionService session,
        IConsumerProviderService consumerProviders,
        IProviderService providers)
    {
        _service = service;
        _session = session;
        _consumerProviders = consumerProviders;
        _providers = providers;
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
                foreach (var question in Sections.SelectMany(section => section.Questions))
                    question.SetPerson(person);
                HasPerson = person is not null;
                var user = _session.CurrentUser;
                CanEdit = person is not null && user is not null && person.UserId == user.Id;
                ClearAnswers();
                Contributors.Clear();
                Needs.Clear();
                ProviderOptions.Clear();
                OnPropertyChanged(nameof(HasProviderOptions));
                if (person is null || user is null) { SaveStatus = "Not loaded"; return; }
                if (!CanEdit) { SaveStatus = "Read only — this consumer is not on your caseload"; return; }

                await LoadProviderOptionsAsync(person.Id);

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

    /// <summary>
    /// Builds the provider choices from this consumer's current links, resolving each one's
    /// chain once. Only current links: a need being written today should not offer somebody the
    /// consumer stopped seeing last year.
    /// <para>
    /// A failure here leaves the list empty rather than blocking the assessment. The provider
    /// association is optional, and an assessment that will not open because a directory read
    /// failed is a worse outcome than one where the picker is briefly unavailable.
    /// </para>
    /// </summary>
    private async Task LoadProviderOptionsAsync(int personId)
    {
        try
        {
            var directory = (await _providers.GetAllAsync()).ToAffiliationNodes();
            var links = await _consumerProviders.GetByPersonAsync(personId);

            foreach (var option in links
                         .Where(link => link.IsActive)
                         .Select(link => new AssessmentProviderOption(
                             ProviderAffiliation.Snapshot(link.ProviderId, directory)))
                         .Where(option => option.ProviderId != 0)
                         .OrderBy(option => option.Display, StringComparer.CurrentCultureIgnoreCase))
            {
                ProviderOptions.Add(option);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Assessment provider options could not be loaded: {exception.Message}");
        }

        OnPropertyChanged(nameof(HasProviderOptions));
    }

    [RelayCommand]
    public void AddNeed()
    {
        if (!CanEdit) return;
        Needs.Add(new AssessmentNeedViewModel(new AssessmentNeed(), ScheduleSave, RemoveNeed, ProviderOptions));
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
            Needs.Add(new AssessmentNeedViewModel(need, ScheduleSave, RemoveNeed, ProviderOptions));
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
            await _service.SaveDocumentAsync(record, document);

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

        await _service.SubmitForReviewAsync(record);
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
            Q("self-view", "How {Name} sees {reflexive}",
              "Center the assessment in the person’s own understanding of who they are.",
              "Strengths, identity, interests, values, and anything the person wants others to understand.", "A diagnostic or service-based description written only by others."),
            Q("what-people-like", "What do people like about {object}?",
              "Capture strengths that other people experience in their relationship with the person.",
              "Specific qualities, contributions, examples, and perspectives from people who know the person.", "Generic compliments without an example."),
            Q("good-life", "What does a good life look like to {object}?",
              "Describe the person’s own priorities, routines, relationships, places, and experiences that make life meaningful.",
              "Concrete preferences and examples in everyday language.", "Happy, appropriate, doing well, or generic service goals without examples."));

        AddSection("Communication", "Understanding & expression",
            Q("communication-overview", "What should people know about communicating with {Name}?",
              "Explain how the person communicates choices, agreement, refusal, discomfort, and a need for a break.",
              "Methods that work, signs others may miss, processing time, and useful accommodations.", "Labels without describing how communication works."),
            Q("receptive-communication", "What support {does} {subject} need with receptive communication?",
              "Describe what helps the person understand information from other people.",
              "Language, pacing, visual supports, repetition, environment, and how understanding is confirmed.", "Understands or does not understand without examples."),
            Q("expressive-communication", "What support {does} {subject} need with expressive communication?",
              "Describe what helps the person communicate thoughts, needs, preferences, and decisions.",
              "Speech, gestures, devices, behavior, time, prompting, and how others confirm meaning.", "Verbal or nonverbal as a complete description."),
            YN("communication-assessment-received", "{Has} {subject} received a communication assessment?"),
            YN("communication-assessment-schedule", "Should a communication assessment be scheduled for the coming year?"));

        AddSection("Home & daily life", "Home, routines & personal activities",
            Q("home-independent", "What activities does {Name} do at home independently?",
              "Identify existing independence before describing support needs.",
              "Specific household, personal, and routine activities completed independently.", "Independent as a general label without examples."),
            Q("home-supports", "What supports {does} {subject} need in the home with daily activities?",
              "Describe how support helps while preserving choice, privacy, and existing skills.",
              "Who helps, what they do, when support is needed, and what the person still does.", "Needs help with ADLs without naming the activity or support."),
            AS("home-support-levels", "Indicate the level of support needed for each home and daily-life activity.",
               "Meal preparation", "Eating and drinking", "Household cleaning", "Laundry", "Shopping and errands",
               "Personal hygiene", "Dressing", "Toileting", "Medication routines", "Mobility and transfers"));

        AddSection("Health & wellness", "Physical, behavioral & emotional health",
            Q("physical-health-view", "How does {Name} feel about {possessive} physical health?",
              "Capture the person’s view of their health, comfort, energy, and access to care.",
              "What feels well, what does not, and the person’s own priorities.", "A diagnosis list without the person’s perspective."),
            Q("physical-health-change", "Is there anything {subject} {wants} to change about {possessive} physical health?",
              "Identify changes the person wants rather than assuming clinical priorities are shared.",
              "Desired change, motivation, barriers, and support requested.", "Provider goals without the person’s view."),
            HC("health-concerns", "Are there health and wellness concerns being actively monitored by healthcare providers?"),
            Q("mental-health-view", "How does {Name} feel about {possessive} mental health?",
              "Capture the person’s view of emotional well-being and any support they value.",
              "Mood, stress, coping, relationships, routines, and what helps.", "A diagnosis or behavior list without the person’s perspective."),
            TH("therapy", "{Is} {subject} seeing a therapist?"));

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
        new(key, prompt, why, include, avoid, false, AssessmentQuestionKind.Narrative, [], ScheduleSave);
    private AssessmentQuestionViewModel SQ(string key, string prompt, string why, string include, string avoid) =>
        new(key, prompt, why, include, avoid, true, AssessmentQuestionKind.Narrative, [], ScheduleSave);
    private AssessmentQuestionViewModel YN(string key, string prompt) =>
        new(key, prompt, string.Empty, string.Empty, string.Empty, false, AssessmentQuestionKind.YesNo, [], ScheduleSave);
    private AssessmentQuestionViewModel HC(string key, string prompt) =>
        new(key, prompt, string.Empty, string.Empty, string.Empty, false, AssessmentQuestionKind.HealthConcern, [], ScheduleSave);
    private AssessmentQuestionViewModel TH(string key, string prompt) =>
        new(key, prompt, string.Empty, string.Empty, string.Empty, false, AssessmentQuestionKind.Therapy, [], ScheduleSave);
    private AssessmentQuestionViewModel AS(string key, string prompt, params string[] activities) =>
        new(key, prompt, string.Empty, string.Empty, string.Empty, false, AssessmentQuestionKind.ActivitySupport, activities, ScheduleSave);
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
    private readonly string _promptTemplate;
    private string _subjectPronoun = "they";
    private bool _usesPluralAgreement = true;
    public string Key { get; }
    public string Prompt { get; private set; }
    public string WhyAsked { get; }
    public string CompleteAnswerIncludes { get; }
    public string Avoid { get; }
    public bool UsesSupports { get; }
    public AssessmentQuestionKind Kind { get; }
    public bool IsNarrative => Kind == AssessmentQuestionKind.Narrative;
    public bool HasGuidance => !string.IsNullOrWhiteSpace(WhyAsked);
    public bool UsesYesNo => Kind is AssessmentQuestionKind.YesNo or AssessmentQuestionKind.HealthConcern or AssessmentQuestionKind.Therapy;
    public bool IsHealthConcern => Kind == AssessmentQuestionKind.HealthConcern;
    public bool IsTherapy => Kind == AssessmentQuestionKind.Therapy;
    public bool UsesActivitySupport => Kind == AssessmentQuestionKind.ActivitySupport;
    public string WouldLikeTherapistPrompt => $"Would {_subjectPronoun} like to see a therapist?";
    public string TherapistSatisfactionPrompt => $"{(_usesPluralAgreement ? "Are" : "Is")} {_subjectPronoun} satisfied with the therapist?";
    public string SessionFormatPrompt => "Are therapy sessions attended in person or via Telehealth?";
    public string OtherSessionFormatPrompt => $"Would {_subjectPronoun} like to switch to {(TherapySessionFormat == TherapySessionFormat.InPerson ? "Telehealth" : "in-person")} sessions?";
    public string FrequencyChangePrompt => $"Would {_subjectPronoun} like to change the frequency of their therapy sessions?";
    public ObservableCollection<ActivitySupportViewModel> Activities { get; } = [];

    [ObservableProperty] private AssessmentAnswerStatus status = AssessmentAnswerStatus.NotYetAnswered;
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
    [ObservableProperty] private bool? yesNoResponse;
    [ObservableProperty] private bool? followUpYesNoResponse;
    [ObservableProperty] private string details = string.Empty;
    [ObservableProperty] private TherapySessionFormat therapySessionFormat;
    [ObservableProperty] private bool? wantsOtherSessionFormat;
    [ObservableProperty] private bool? wantsFrequencyChange;
    [ObservableProperty] private TherapyFrequencyDirection therapyFrequencyDirection;

    public AssessmentQuestionViewModel(string key, string prompt, string why, string include, string avoid,
        bool usesSupports, AssessmentQuestionKind kind, IEnumerable<string> activities, Action changed)
    {
        Key = key; _promptTemplate = prompt; Prompt = prompt; WhyAsked = why;
        CompleteAnswerIncludes = include; Avoid = avoid; UsesSupports = usesSupports; Kind = kind; _changed = changed;
        foreach (var activity in activities)
            Activities.Add(new ActivitySupportViewModel(activity, ActivitySupportLevel.Independent, Changed));
    }

    public void SetPerson(Person? person)
    {
        var name = person?.FirstName ?? "the person";
        _subjectPronoun = person?.SubjectPronoun ?? "they";
        _usesPluralAgreement = string.Equals(_subjectPronoun, "they", StringComparison.OrdinalIgnoreCase);
        Prompt = _promptTemplate
            .Replace("{Name}", name)
            .Replace("{subject}", person?.SubjectPronoun ?? "they")
            .Replace("{object}", person?.ObjectPronoun ?? "them")
            .Replace("{possessive}", person?.PossessivePronoun ?? "their")
            .Replace("{reflexive}", person?.ReflexivePronoun ?? "themselves")
            .Replace("{does}", _usesPluralAgreement ? "do" : "does")
            .Replace("{Has}", _usesPluralAgreement ? "Have" : "Has")
            .Replace("{Is}", _usesPluralAgreement ? "Are" : "Is")
            .Replace("{wants}", _usesPluralAgreement ? "want" : "wants");
        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(WouldLikeTherapistPrompt));
        OnPropertyChanged(nameof(TherapistSatisfactionPrompt));
        OnPropertyChanged(nameof(OtherSessionFormatPrompt));
        OnPropertyChanged(nameof(FrequencyChangePrompt));
    }

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
    partial void OnYesNoResponseChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowHealthConcernDetails));
        OnPropertyChanged(nameof(ShowTherapyCurrentFollowUps));
        OnPropertyChanged(nameof(ShowTherapyRequestedFollowUp));
        Changed();
    }
    partial void OnFollowUpYesNoResponseChanged(bool? value) => Changed();
    partial void OnDetailsChanged(string value) => Changed();
    partial void OnTherapySessionFormatChanged(TherapySessionFormat value)
    {
        OnPropertyChanged(nameof(ShowOtherSessionFormatQuestion));
        OnPropertyChanged(nameof(OtherSessionFormatPrompt));
        Changed();
    }
    partial void OnWantsOtherSessionFormatChanged(bool? value) => Changed();
    partial void OnWantsFrequencyChangeChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowFrequencyDirection));
        Changed();
    }
    partial void OnTherapyFrequencyDirectionChanged(TherapyFrequencyDirection value) => Changed();
    private void ClearNoSupport(bool selected) { if (selected) NoSupportCurrentlyNeeded = false; }
    private void Changed() { if (!_loading) { OnPropertyChanged(nameof(IsAddressed)); OnPropertyChanged(nameof(ShowExceptionReason)); _changed(); } }

    public bool ShowExceptionReason => Status is not AssessmentAnswerStatus.Answered
        and not AssessmentAnswerStatus.NotYetAnswered;
    public bool ShowHealthConcernDetails => IsHealthConcern && YesNoResponse == true;
    public bool ShowTherapyCurrentFollowUps => IsTherapy && YesNoResponse == true;
    public bool ShowTherapyRequestedFollowUp => IsTherapy && YesNoResponse == false;
    public bool ShowOtherSessionFormatQuestion => ShowTherapyCurrentFollowUps && TherapySessionFormat != TherapySessionFormat.NotSelected;
    public bool ShowFrequencyDirection => ShowTherapyCurrentFollowUps && WantsFrequencyChange == true;
    public string ExceptionReasonPrompt => Status switch
    {
        AssessmentAnswerStatus.FollowUpRequired => "Explain why follow-up is required.",
        AssessmentAnswerStatus.NotYetAnswered => string.Empty,
        AssessmentAnswerStatus.UnableToAssess => "Explain why this item could not be assessed.",
        AssessmentAnswerStatus.Declined => "Explain why an answer was declined.",
        AssessmentAnswerStatus.NotApplicable => "Explain why this item is not applicable.",
        _ => "Additional explanation"
    };
    public bool HasConcreteSupport => SetupOrEnvironmental || PromptingOrCoaching || HandsOnAssistance || AnotherPersonCompletes;
    public bool IsAddressed => Status switch
    {
        AssessmentAnswerStatus.Answered => Kind switch
        {
            AssessmentQuestionKind.Narrative => !string.IsNullOrWhiteSpace(Narrative)
                && (!UsesSupports || NoSupportCurrentlyNeeded || HasConcreteSupport)
                && (!Varies || (HasConcreteSupport && !string.IsNullOrWhiteSpace(SupportDetails))),
            AssessmentQuestionKind.YesNo => YesNoResponse.HasValue,
            AssessmentQuestionKind.HealthConcern => YesNoResponse.HasValue
                && (YesNoResponse == false || !string.IsNullOrWhiteSpace(Details)),
            AssessmentQuestionKind.Therapy => TherapyResponseComplete,
            AssessmentQuestionKind.ActivitySupport => Activities.Count > 0,
            _ => false
        },
        AssessmentAnswerStatus.FollowUpRequired => false,
        AssessmentAnswerStatus.NotYetAnswered => false,
        _ => !string.IsNullOrWhiteSpace(ExceptionReason)
    };

    private bool TherapyResponseComplete => YesNoResponse switch
    {
        false => FollowUpYesNoResponse.HasValue,
        true => FollowUpYesNoResponse.HasValue
            && TherapySessionFormat != TherapySessionFormat.NotSelected
            && WantsOtherSessionFormat.HasValue
            && WantsFrequencyChange.HasValue
            && (WantsFrequencyChange == false || TherapyFrequencyDirection != TherapyFrequencyDirection.NotSelected),
        _ => false
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
        YesNoResponse = answer.YesNoResponse;
        FollowUpYesNoResponse = answer.FollowUpYesNoResponse;
        Details = answer.Details;
        TherapySessionFormat = answer.TherapySessionFormat;
        WantsOtherSessionFormat = answer.WantsOtherSessionFormat;
        WantsFrequencyChange = answer.WantsFrequencyChange;
        TherapyFrequencyDirection = answer.TherapyFrequencyDirection;
        var savedActivityLevels = answer.ActivitySupportLevels ?? [];
        var savedSkillsTraining = answer.ActivitySkillsTraining ?? [];
        foreach (var activity in Activities)
        {
            var level = savedActivityLevels.TryGetValue(activity.Name, out var savedLevel)
                ? savedLevel : ActivitySupportLevel.Independent;
            var skillsTraining = savedSkillsTraining.TryGetValue(activity.Name, out var savedTraining) && savedTraining;
            activity.Load(level, skillsTraining);
        }
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
        return new()
        {
            Status = Status, Narrative = Narrative, Supports = supports, SupportDetails = SupportDetails,
            ExceptionReason = ExceptionReason, DissentingOpinion = DissentingOpinion,
            YesNoResponse = YesNoResponse, FollowUpYesNoResponse = FollowUpYesNoResponse, Details = Details,
            TherapySessionFormat = TherapySessionFormat, WantsOtherSessionFormat = WantsOtherSessionFormat,
            WantsFrequencyChange = WantsFrequencyChange, TherapyFrequencyDirection = TherapyFrequencyDirection,
            ActivitySupportLevels = Activities.ToDictionary(activity => activity.Name, activity => activity.Level),
            ActivitySkillsTraining = Activities.ToDictionary(activity => activity.Name, activity => activity.SkillsTraining)
        };
    }
}

public sealed partial class ActivitySupportViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;
    public string Name { get; }
    [ObservableProperty] private int levelValue;
    [ObservableProperty] private bool skillsTraining;
    public ActivitySupportLevel Level => LevelValue switch
    {
        0 => ActivitySupportLevel.Independent,
        1 => ActivitySupportLevel.Prompting,
        2 => ActivitySupportLevel.Monitoring,
        3 => ActivitySupportLevel.PhysicalAssistance,
        _ => ActivitySupportLevel.TotalCare
    };

    public ActivitySupportViewModel(string name, ActivitySupportLevel level, Action changed)
    { Name = name; levelValue = (int)level; _changed = changed; }

    partial void OnLevelValueChanged(int value)
    {
        OnPropertyChanged(nameof(Level));
        if (!_loading) _changed();
    }
    partial void OnSkillsTrainingChanged(bool value)
    {
        if (!_loading) _changed();
    }

    public void Load(ActivitySupportLevel level, bool savedSkillsTraining)
    {
        _loading = true;
        SkillsTraining = savedSkillsTraining || level == ActivitySupportLevel.SkillsTraining;
        LevelValue = level switch
        {
            ActivitySupportLevel.Prompting => 1,
            ActivitySupportLevel.Monitoring => 2,
            ActivitySupportLevel.PhysicalAssistance => 3,
            ActivitySupportLevel.TotalCare => 4,
            _ => 0
        };
        _loading = false;
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

/// <summary>
/// One provider a need may be associated with, already resolved. The snapshot travels with the
/// option so choosing it is what freezes the practice and network onto the document — there is
/// no second lookup that could disagree with what the picker showed.
/// </summary>
public sealed class AssessmentProviderOption(ProviderAffiliation.ProviderSnapshot snapshot)
{
    public ProviderAffiliation.ProviderSnapshot Snapshot { get; } = snapshot;
    public int ProviderId => Snapshot.ProviderId;
    public string Display => Snapshot.Describe();
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
    [ObservableProperty] private string providerPracticeSnapshot;
    [ObservableProperty] private string providerNetworkSnapshot;
    [ObservableProperty] private int? providerId;

    public AssessmentNeedViewModel(
        AssessmentNeed model,
        Action changed,
        Action<AssessmentNeedViewModel> remove,
        IReadOnlyList<AssessmentProviderOption>? providerOptions = null)
    {
        Id = model.Id; type = model.Type; description = model.Description; desiredResult = model.DesiredResult;
        associateProvider = model.AssociateProvider; providerNameSnapshot = model.ProviderNameSnapshot;
        providerPracticeSnapshot = model.ProviderPracticeSnapshot;
        providerNetworkSnapshot = model.ProviderNetworkSnapshot;
        providerId = model.ProviderId;
        ProviderOptions = providerOptions ?? [];
        _changed = changed; _remove = remove;
    }

    public IReadOnlyList<AssessmentProviderOption> ProviderOptions { get; }

    public bool HasProviderOptions => ProviderOptions.Count > 0;

    /// <summary>
    /// What this need currently records, exactly as it will read on the document. Shown even
    /// when the provider is no longer in the directory, or was typed before the directory
    /// existed — the document keeps what it froze.
    /// </summary>
    public string RecordedProvider => string.Join(" — ", new[]
    {
        ProviderNameSnapshot,
        string.Join(" · ", new[] { ProviderPracticeSnapshot, ProviderNetworkSnapshot }
            .Where(part => !string.IsNullOrWhiteSpace(part)))
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public bool HasRecordedProvider => !string.IsNullOrWhiteSpace(ProviderNameSnapshot);

    /// <summary>
    /// Shown when a need already names a provider that is not among the current choices —
    /// typed before the directory, or somebody the consumer no longer sees. The document is not
    /// rewritten to match; it says what it said.
    /// </summary>
    public bool RecordedProviderIsOutsideCurrentChoices =>
        HasRecordedProvider && ProviderOptions.All(option => option.ProviderId != ProviderId);

    partial void OnTypeChanged(AssessmentNeedType value) => _changed();
    partial void OnDescriptionChanged(string value) => _changed();
    partial void OnDesiredResultChanged(string value) => _changed();
    partial void OnAssociateProviderChanged(bool value) => _changed();

    /// <summary>
    /// Choosing a provider freezes its resolved chain onto the need. This is the one place in
    /// Sati where the practice and network are copied rather than derived, and it is deliberate:
    /// an assessment approved in March has to keep saying what it said in March.
    /// </summary>
    partial void OnProviderIdChanged(int? value)
    {
        var chosen = ProviderOptions.FirstOrDefault(option => option.ProviderId == value);
        if (chosen is not null)
        {
            ProviderNameSnapshot = chosen.Snapshot.ProviderName;
            ProviderPracticeSnapshot = chosen.Snapshot.PracticeName;
            ProviderNetworkSnapshot = chosen.Snapshot.NetworkName;
        }

        RaiseRecordedProviderChanged();
        _changed();
    }

    partial void OnProviderNameSnapshotChanged(string value) => RaiseRecordedProviderChanged();
    partial void OnProviderPracticeSnapshotChanged(string value) => RaiseRecordedProviderChanged();
    partial void OnProviderNetworkSnapshotChanged(string value) => RaiseRecordedProviderChanged();

    private void RaiseRecordedProviderChanged()
    {
        OnPropertyChanged(nameof(RecordedProvider));
        OnPropertyChanged(nameof(HasRecordedProvider));
        OnPropertyChanged(nameof(RecordedProviderIsOutsideCurrentChoices));
    }

    [RelayCommand] public void Remove() => _remove(this);

    public AssessmentNeed ToModel() => new()
    {
        Id = Id, Type = Type, Description = Description, DesiredResult = DesiredResult,
        AssociateProvider = AssociateProvider, ProviderId = ProviderId,
        ProviderNameSnapshot = ProviderNameSnapshot,
        ProviderPracticeSnapshot = ProviderPracticeSnapshot,
        ProviderNetworkSnapshot = ProviderNetworkSnapshot
    };
}
