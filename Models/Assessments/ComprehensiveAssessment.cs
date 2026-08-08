using System.ComponentModel.DataAnnotations;

namespace Sati.Models.Assessments;

public enum AssessmentStatus { Draft, ReadyForReview, Returned, Approved, Superseded }

public enum AssessmentAnswerStatus { Answered, NotApplicable, Declined, UnableToAssess, FollowUpRequired }

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
    public AssessmentAnswerStatus Status { get; set; } = AssessmentAnswerStatus.FollowUpRequired;
    public string Narrative { get; set; } = string.Empty;
    public SupportMethod Supports { get; set; }
    public string SupportDetails { get; set; } = string.Empty;
    public string ExceptionReason { get; set; } = string.Empty;
    public string DissentingOpinion { get; set; } = string.Empty;
}

public enum AssessmentNeedType
{
    Material, Support, SkillDevelopment, AccessOrAccommodation,
    HealthOrSafety, RelationshipOrCommunity, ChoiceAutonomyOrRights,
    InformationPlanningOrDecisionSupport
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
