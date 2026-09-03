using System.Security.Cryptography;

namespace Sati.Contracts.V1;

public sealed record AnnualPacketWindowDto(DateTime CycleStart, DateTime OpensOn, DateTime EndsOn, bool IsOpen);
public static class AnnualPacketWindow
{
    public const int DefaultOpenDays = 30;
    public static AnnualPacketWindowDto ForCycle(DateTime effective, DateTime cycle, DateTime today, int openDays)
    {
        if (openDays is < 0 or > 180 || cycle.Year is < 2 or > 9997 || cycle < effective.Date ||
            AnnualDocumentCycle.CurrentStart(effective, cycle) != cycle.Date)
            throw new ArgumentException("Choose an enrollment anniversary and an opening window of 0–180 days.");
        var end = AnnualDocumentCycle.EndInclusive(effective, cycle);
        var opens = cycle.Date.AddDays(-openDays);
        return new(cycle.Date, opens, end, today.Date >= opens && today.Date <= end);
    }
    public static DateTime SuggestedCycle(DateTime effective, DateTime today, int openDays)
    {
        var current = AnnualDocumentCycle.CurrentStart(effective, today);
        if (current < effective.Date) return effective.Date;
        var next = effective.AddYears(current.Year - effective.Year + 1).Date;
        return today.Date >= next.AddDays(-openDays) ? next : current;
    }
}

public sealed record AnnualDocumentsStatusDto(AnnualPacketWindowDto Window,
    IReadOnlyList<DocumentArtifactDto> Artifacts, IReadOnlyList<int> AcknowledgedArtifactIds, string Reminder);
public sealed record AcknowledgeDocumentRequest(int DocumentArtifactId, DateTime? ReceivedOn, string? GoodFaithEffortReason);
public sealed record DocumentAcknowledgmentDto(int Id, int DocumentArtifactId, DateTime? ReceivedOn,
    string? GoodFaithEffortReason, int RecordedByUserId, DateTime RecordedAtUtc);
public sealed record VerifyDocumentRequest(int DocumentArtifactId, string Sha256, long ByteCount);
public sealed record VerifyDocumentResult(bool Matches, string Message);
public sealed record SaveAnnualPacketRequest(DateTime CycleStart);

public sealed record RecordsProviderFact(int Id, int? ParentId, string Name, string? Address, string? Phone);
public static class RecordsRecipient
{
    public static RecordsProviderFact? Resolve(int? providerId, IReadOnlyList<RecordsProviderFact> directory)
    {
        if (providerId is null) return null;
        var current = directory.SingleOrDefault(x => x.Id == providerId);
        if (current is null) return null;
        var result = current; var visited = new HashSet<int> { current.Id };
        while (current.ParentId is int parent)
        {
            if (!visited.Add(parent)) throw new InvalidOperationException("The provider directory contains a parent cycle.");
            current = directory.SingleOrDefault(x => x.Id == parent);
            if (current is null) break;
            result = result with { Address = string.IsNullOrWhiteSpace(result.Address) ? current.Address : result.Address,
                Phone = string.IsNullOrWhiteSpace(result.Phone) ? current.Phone : result.Phone };
        }
        return result;
    }
}

public static class DocumentAcknowledgmentRules
{
    public static string? Validate(AcknowledgeDocumentRequest request, DateTime generated, DateTime today)
    {
        if (request.ReceivedOn is null && string.IsNullOrWhiteSpace(request.GoodFaithEffortReason))
            return "Enter a receipt date or describe the good-faith effort to provide the notice.";
        if (request.GoodFaithEffortReason?.Length > 1000) return "The explanation must be at most 1,000 characters.";
        if (request.ReceivedOn is DateTime date && (date.Date > today.Date || date.Date < generated.Date))
            return "The receipt date must be between the document generation date and today.";
        return null;
    }
}

public static class DocumentVerification
{
    public static VerifyDocumentRequest FromBytes(int artifactId, byte[] content) =>
        new(artifactId, Convert.ToHexString(SHA256.HashData(content)), content.LongLength);
    public static bool Matches(string? storedHash, long? storedLength, VerifyDocumentRequest request) =>
        storedHash is not null && storedLength is not null && request.Sha256?.Length == 64 &&
        request.Sha256.All(Uri.IsHexDigit) && storedLength == request.ByteCount &&
        string.Equals(storedHash, request.Sha256, StringComparison.OrdinalIgnoreCase);
}

public static class AnnualDocumentReminder
{
    public static string Describe(bool windowOpen, bool pcpAttested, IEnumerable<DocumentArtifactDto> artifacts)
    {
        if (!windowOpen && !pcpAttested) return "";
        var list = artifacts.ToList();
        var missing = new[] { AnnualDocumentKind.ReleaseAgency, AnnualDocumentKind.ReleaseDhhs,
            AnnualDocumentKind.ReleaseMedical, AnnualDocumentKind.SafetyPlan }
            .Where(kind => !list.Any(x => x.Kind == kind.ToString() && x.Origin != "Draft"))
            .Select(kind => AnnualDocumentCatalog.ForKind(kind).DisplayName).ToArray();
        return missing.Length == 0 ? "" : "Preparation still needed: " + string.Join(", ", missing) +
            ". Open the release or safety-plan workspace to complete the saved work.";
    }
}
