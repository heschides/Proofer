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
            if (document is null || document.SchemaVersion != SchemaVersion)
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
}

public sealed record SafetyPlanDocument(int SchemaVersion, List<SafetyPlanSection> Sections);
public sealed record SafetyPlanSection(string Id, string Text);
public sealed record SafetyPlanDto(int Id, int PersonId, int AuthorUserId, DateTime CycleStart, string Status,
    int Version, int Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? SubmittedAtUtc,
    DateTime? ApprovedAtUtc, int? ApprovedByUserId, string? ReturnReason, string DocumentJson);
public sealed record SaveSafetyPlanDocumentRequest(string DocumentJson, int ExpectedRevision);
public sealed record ReviewSafetyPlanRequest(int ExpectedRevision, string? ReturnReason = null);
