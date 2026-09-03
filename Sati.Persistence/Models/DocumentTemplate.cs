using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>An immutable published version of an annual-document template.</summary>
public sealed class DocumentTemplate
{
    public int Id { get; private set; }
    public int? AgencyId { get; private set; }
    public AnnualDocumentKind Kind { get; private set; }
    public int Version { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTime PublishedAtUtc { get; private set; }
    public int? PublishedByUserId { get; private set; }
    public DateTime? RetiredAtUtc { get; private set; }

    private DocumentTemplate() { }

    public static DocumentTemplate Publish(
        int? agencyId,
        AnnualDocumentKind kind,
        int version,
        string body,
        DateTime publishedAtUtc,
        int? publishedByUserId)
    {
        var errors = DocumentTemplateRules.Validate(kind, body);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.SelectMany(item => item.Value)), nameof(body));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (agencyId is not null && publishedByUserId is null)
            throw new ArgumentException("An agency template requires a publishing user.", nameof(publishedByUserId));

        return new DocumentTemplate
        {
            AgencyId = agencyId,
            Kind = kind,
            Version = version,
            Body = body.Trim(),
            PublishedAtUtc = DateTime.SpecifyKind(publishedAtUtc, DateTimeKind.Utc),
            PublishedByUserId = publishedByUserId
        };
    }
}
