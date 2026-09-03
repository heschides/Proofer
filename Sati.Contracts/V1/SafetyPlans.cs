using System.Text.Json;

namespace Sati.Contracts.V1;

/// <summary>Authoritative, deliberately small shared structure for a safety plan.</summary>
public static class SafetyPlanRules
{
    public const int SchemaVersion = 1;
    public const int SectionTextMaxLength = 8_000;
    public static IReadOnlyList<string> SectionIds { get; } =
    [
        "concerns-and-triggers", "early-warning-signs", "coping-strategies",
        "support-contacts", "professional-and-emergency-supports",
        "environment-and-means-safety", "follow-up-and-review"
    ];

    public static IReadOnlyDictionary<string, string[]> Validate(string? documentJson, bool requireComplete)
    {
        if (string.IsNullOrWhiteSpace(documentJson) || documentJson.Length > 100_000)
            return new Dictionary<string, string[]> { ["document"] = ["Safety-plan data is required and must not exceed 100 KB."] };
        try
        {
            var document = JsonSerializer.Deserialize<SafetyPlanDocument>(documentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (document is null || document.SchemaVersion != SchemaVersion || document.Sections is null || document.Sections.Any(section => section is null || section.Id is null))
                return new Dictionary<string, string[]> { ["document"] = ["The safety-plan structure is not supported."] };
            var duplicateOrUnknown = document.Sections.GroupBy(x => x.Id, StringComparer.Ordinal)
                .Any(x => x.Count() != 1 || !SectionIds.Contains(x.Key, StringComparer.Ordinal));
            if (duplicateOrUnknown || document.Sections.Count != SectionIds.Count)
                return new Dictionary<string, string[]> { ["document"] = ["Each required safety-plan section must appear exactly once."] };
            foreach (var section in document.Sections)
                if (section.Text?.Trim().Length > SectionTextMaxLength)
                    return new Dictionary<string, string[]> { ["document"] = [$"A safety-plan section cannot exceed {SectionTextMaxLength} characters."] };
            if (requireComplete && document.Sections.Any(x => string.IsNullOrWhiteSpace(x.Text)))
                return new Dictionary<string, string[]> { ["document"] = ["Complete every shared safety-plan section before submitting it for review."] };
            return new Dictionary<string, string[]>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string[]> { ["document"] = ["Safety-plan data is invalid."] };
        }
    }

    public static string EmptyDocumentJson() => JsonSerializer.Serialize(new SafetyPlanDocument(
        SchemaVersion, SectionIds.Select(id => new SafetyPlanSection(id, string.Empty)).ToList()), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static bool CanAuthor(int actorId, UserPermissions permissions, int assignedUserId) =>
        actorId == assignedUserId && UserPermissionRules.HasCaseManagerPermissions(permissions);

    public static bool CanReview(int actorId, UserPermissions permissions, int authorId) =>
        actorId != authorId && UserPermissionRules.HasSupervisorPermissions(permissions);

    public static bool CanStartRevision(string? status) => status is null or "Approved" or "Returned";

    public static SafetyPlanDto Change(SafetyPlanDto plan, string action, int revision, int actorId,
        UserPermissions permissions, DateTime now, string? document = null, string? reason = null)
    {
        if (revision != plan.Revision) throw new SafetyPlanWorkflowException("safety_plan_stale", "This plan changed. Reload before continuing.");
        var review = action is "approve" or "return";
        if (review ? !CanReview(actorId, permissions, plan.AuthorUserId) : !CanAuthor(actorId, permissions, plan.AuthorUserId))
            throw new UnauthorizedAccessException("You cannot perform this safety-plan action.");
        if (review ? plan.Status != "ReadyForReview" : plan.Status != "Draft")
            throw new SafetyPlanWorkflowException("safety_plan_locked", "This version is locked. Start a new revision after review if changes are needed.");
        var content = document ?? plan.DocumentJson;
        var errors = Validate(content, action is "submit" or "approve");
        if (errors.Count > 0) throw new SafetyPlanWorkflowException("safety_plan_invalid", string.Join(" ", errors.SelectMany(x => x.Value)));
        if (action == "return" && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500))
            throw new SafetyPlanWorkflowException("safety_plan_invalid", "A return reason of 500 characters or fewer is required.");
        return action switch
        {
            "save" => plan with { DocumentJson = content, UpdatedAtUtc = now, Revision = plan.Revision + 1 },
            "submit" => plan with { Status = "ReadyForReview", SubmittedAtUtc = now, UpdatedAtUtc = now, Revision = plan.Revision + 1 },
            "approve" => plan with { Status = "Approved", ApprovedAtUtc = now, ApprovedByUserId = actorId, UpdatedAtUtc = now, Revision = plan.Revision + 1 },
            "return" => plan with { Status = "Returned", ReturnReason = reason!.Trim(), UpdatedAtUtc = now, Revision = plan.Revision + 1 },
            _ => throw new ArgumentException("Unknown safety-plan action.", nameof(action))
        };
    }
}

public sealed class SafetyPlanWorkflowException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed record SafetyPlanDocument(int SchemaVersion, List<SafetyPlanSection> Sections);
public sealed record SafetyPlanSection(string Id, string Text);
public sealed record SafetyPlanDto(int Id, int PersonId, int AuthorUserId, DateTime CycleStart, string Status,
    int Version, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? SubmittedAtUtc,
    DateTime? ApprovedAtUtc, int? ApprovedByUserId, string? ReturnReason, string DocumentJson);
public sealed record SaveSafetyPlanDocumentRequest(string DocumentJson, int ExpectedRevision);
public sealed record ReviewSafetyPlanRequest(int ExpectedRevision, string? ReturnReason = null);
