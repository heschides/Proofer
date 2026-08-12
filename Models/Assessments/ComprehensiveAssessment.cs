using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Sati.Models.Assessments;

public enum AssessmentStatus { Draft, ReadyForReview, Returned, Approved, Superseded }

public enum AssessmentAnswerStatus
{
    [Description("Answered")] Answered = 0,
    [Description("Not Applicable")] NotApplicable = 1,
    [Description("Declined")] Declined = 2,
    [Description("Unable to Assess")] UnableToAssess = 3,
    [Description("Follow-Up Required")] FollowUpRequired = 4,
    [Description("Not Yet Answered")] NotYetAnswered = 5
}

public enum AssessmentQuestionKind { Narrative, YesNo, HealthConcern, Therapy, ActivitySupport }

public enum ActivitySupportLevel
{
    Independent = 0,
    Prompting = 1,
    // Retained only to read drafts saved before skills training became a
    // separate checkbox. New responses never write this value.
    SkillsTraining = 2,
    Monitoring = 3,
    PhysicalAssistance = 4,
    TotalCare = 5
}

public enum TherapySessionFormat { NotSelected, InPerson, Telehealth }

public enum TherapyFrequencyDirection { NotSelected, Increase, Decrease }

[Flags]
public enum SupportMethod
{
    None = 0,
    SetupOrEnvironmental = 1,
    PromptingOrCoaching = 2,
    HandsOnAssistance = 4,
    AnotherPersonCompletes = 8,
    Varies = 16,
    NoSupportCurrentlyNeeded = 32
}

public class ComprehensiveAssessment
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int AuthorUserId { get; set; }
    public User AuthorUser { get; set; } = null!;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string DocumentJson { get; set; } = "{}";
}

public sealed class AssessmentDocument
{
    public List<AssessmentContributor> Contributors { get; set; } = [];
    public Dictionary<string, AssessmentAnswer> Answers { get; set; } = [];
    public List<AssessmentNeed> Needs { get; set; } = [];
}

public sealed class AssessmentContributor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
}

public sealed class AssessmentAnswer
{
    public AssessmentAnswerStatus Status { get; set; } = AssessmentAnswerStatus.NotYetAnswered;
    public string Narrative { get; set; } = string.Empty;
    public SupportMethod Supports { get; set; }
    public string SupportDetails { get; set; } = string.Empty;
    public string ExceptionReason { get; set; } = string.Empty;
    public string DissentingOpinion { get; set; } = string.Empty;
    public bool? YesNoResponse { get; set; }
    public bool? FollowUpYesNoResponse { get; set; }
    public string Details { get; set; } = string.Empty;
    public TherapySessionFormat TherapySessionFormat { get; set; }
    public bool? WantsOtherSessionFormat { get; set; }
    public bool? WantsFrequencyChange { get; set; }
    public TherapyFrequencyDirection TherapyFrequencyDirection { get; set; }
    public Dictionary<string, ActivitySupportLevel> ActivitySupportLevels { get; set; } = [];
    public Dictionary<string, bool> ActivitySkillsTraining { get; set; } = [];
}

public enum AssessmentNeedType
{
    [Description("Material")] Material,
    [Description("Support")] Support,
    [Description("Skill Development")] SkillDevelopment,
    [Description("Access or Accommodation")] AccessOrAccommodation,
    [Description("Health or Safety")] HealthOrSafety,
    [Description("Relationship or Community")] RelationshipOrCommunity,
    [Description("Choice, Autonomy, or Rights")] ChoiceAutonomyOrRights,
    [Description("Information, Planning, or Decision Support")] InformationPlanningOrDecisionSupport
}

public sealed class AssessmentNeed
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AssessmentNeedType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DesiredResult { get; set; } = string.Empty;
    public bool AssociateProvider { get; set; }
    public int? ProviderId { get; set; }
    public string ProviderNameSnapshot { get; set; } = string.Empty;
}
