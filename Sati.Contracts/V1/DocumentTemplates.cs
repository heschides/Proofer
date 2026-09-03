using System.Text.RegularExpressions;

namespace Sati.Contracts.V1;

public sealed record DocumentTemplateFact(
    int Id,
    int? AgencyId,
    string Kind,
    int Version,
    DateTime PublishedAtUtc,
    DateTime? RetiredAtUtc);

public sealed record DocumentTemplateDto(
    int Id,
    int? AgencyId,
    string Kind,
    int Version,
    string Body,
    DateTime PublishedAtUtc,
    int? PublishedByUserId,
    DateTime? RetiredAtUtc,
    string Owner);

public sealed record PublishDocumentTemplateRequest(string Body);

public sealed record DocumentTemplateRenderContext(
    string? AgencyName,
    string? AgencyAddress,
    string? AgencyPhone,
    string? ConsumerFullName,
    DateTime? ConsumerBirthDate,
    DateTime CycleStart,
    DateTime CycleEnd,
    string? CaseManagerName,
    string? CaseManagerRole,
    string? ProviderName = null,
    string? ProviderAddress = null,
    string? ProviderPhone = null,
    string? ProviderFax = null);

public static class DocumentTemplateResolution
{
    public static DocumentTemplateFact? Resolve(
        int agencyId,
        AnnualDocumentKind kind,
        IEnumerable<DocumentTemplateFact> candidates) =>
        candidates
            .Where(candidate =>
                candidate.RetiredAtUtc is null &&
                candidate.Kind.Equals(kind.ToString(), StringComparison.OrdinalIgnoreCase) &&
                (candidate.AgencyId == agencyId || candidate.AgencyId is null))
            .OrderByDescending(candidate => candidate.AgencyId == agencyId)
            .ThenByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.PublishedAtUtc)
            .FirstOrDefault();
}

public static partial class DocumentTemplateRules
{
    public const int BodyMaxLength = 100_000;
    public const string PageBreakMarker = "[[PAGE_BREAK]]";

    public static IReadOnlyList<string> AllowedTokens(AnnualDocumentKind kind) => kind switch
    {
        AnnualDocumentKind.PrivacyPractices => CommonTokens,
        AnnualDocumentKind.MedicalRecordsRequest => [.. CommonTokens, .. ProviderTokens],
        _ => []
    };

    public static IReadOnlyDictionary<string, string[]> Validate(
        AnnualDocumentKind kind,
        string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (AllowedTokens(kind).Count == 0)
        {
            errors["kind"] = ["This document kind does not use a published template."];
            return errors;
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            errors["body"] = ["Template content is required."];
            return errors;
        }
        if (body.Length > BodyMaxLength)
            errors["body"] = [$"Template content cannot exceed {BodyMaxLength} characters."];

        var allowed = AllowedTokens(kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = TokenPattern().Matches(body)
            .Select(match => match.Groups[1].Value)
            .Where(token => !allowed.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0)
            errors["tokens"] = [$"Unknown template token(s): {string.Join(", ", unknown)}."];

        var withoutKnownTokens = TokenPattern().Replace(body, string.Empty);
        if (withoutKnownTokens.Contains("{{", StringComparison.Ordinal) ||
            withoutKnownTokens.Contains("}}", StringComparison.Ordinal))
            errors["syntax"] = ["Template tokens must use the exact {{token.name}} form."];

        int? tableColumns = null;
        foreach (var sourceLine in body.Split('\n'))
        {
            var line = sourceLine.Trim();
            if (!line.StartsWith('|'))
            {
                tableColumns = null;
                continue;
            }
            var columns = line.Trim('|').Split('|').Length;
            if (columns is < 2 or > 8 || tableColumns is int expected && columns != expected)
                errors["table"] = ["Tables require 2-8 columns and the same number of cells in every row."];
            tableColumns = columns;
        }

        return errors;
    }

    public static string OwnerName(int? agencyId) => agencyId is null ? "SatiDefault" : "Agency";

    public static IReadOnlyList<string> CommonTokens { get; } =
    [
        "agency.name", "agency.address", "agency.phone",
        "consumer.full_name", "consumer.birth_date",
        "cycle.start", "cycle.end",
        "case_manager.name", "case_manager.role"
    ];

    public static IReadOnlyList<string> ProviderTokens { get; } =
        ["provider.name", "provider.address", "provider.phone", "provider.fax"];

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    public static partial Regex TokenPattern();
}

public static class SatiDefaultDocumentTemplates
{
    public const int PrivacyPracticesVersion = 1;
    public static readonly DateTime PublishedAtUtc =
        new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    public const string PrivacyPracticesBody = """
# Notice of Privacy Practices

PROVISIONAL SATI DEFAULT - AGENCY PRIVACY AND LEGAL REVIEW REQUIRED

Prepared for cycle beginning: {{cycle.start}}

This notice describes general ways {{agency.name}} may use and share information about {{consumer.full_name}}, and how the individual or authorized representative may exercise privacy rights. It is a generic starting point and must be replaced or approved by the agency before production use.

## Our responsibilities

- Protect the privacy and security of health and service information.
- Follow the privacy practices described in the agency's current approved notice.
- Notify affected people when required after a breach of unsecured information.
- Provide the current notice when privacy practices materially change.

## How information may be used or shared

Information may be used or shared as permitted or required by applicable law for treatment and service coordination, payment, health-care operations, public-health and safety duties, oversight, legal proceedings, and other specifically authorized purposes. Uses or disclosures requiring written authorization will not occur without that authorization, and an authorization may be revoked as allowed by law.

## Individual privacy rights

- Ask to inspect or obtain a copy of records, subject to lawful limits.
- Ask for a correction or amendment.
- Ask for confidential communications or certain restrictions.
- Ask for an accounting of qualifying disclosures.
- Receive a paper copy of the agency's approved notice.
- Make a privacy complaint without retaliation.

## Questions or complaints

Contact {{agency.name}} at {{agency.address}} or {{agency.phone}} to ask questions, exercise a privacy right, or make a complaint. The agency's approved notice must identify any additional external complaint process that applies.

## Receipt

Receiving this notice does not authorize a release of information. Receipt or a documented good-faith effort to provide the notice is recorded separately by authorized staff.

Prepared for: {{consumer.full_name}}
Date of birth: {{consumer.birth_date}}
Case manager: {{case_manager.name}}, {{case_manager.role}}
Coverage cycle: {{cycle.start}} through {{cycle.end}}
""";
}
