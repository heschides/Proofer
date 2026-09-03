using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Forms;

public sealed record PacketDocument(AnnualDocumentKind Kind, byte[] Pdf, string FileName, DocumentArtifactOrigin Origin,
    IReadOnlyList<string> BlankFields, string? TemplateOwner = null, string? TemplateKey = null,
    int? TemplateVersion = null, int? SourceContentId = null, int? SourceContentVersion = null);
public sealed record PacketRenderInput(AgencyReleaseSubject Subject, DateTime CycleStart, DateTime CycleEnd,
    DateTime GeneratedAtUtc, int ActorId, IReadOnlyList<DocumentArtifactDto> LiveArtifacts,
    SafetyPlanDto? SafetyPlan, IReadOnlyList<DocumentTemplateDto> Templates, bool MedicalReleaseAttested,
    string? ProviderName, string? ProviderAddress, string? ProviderPhone);
public sealed record PacketRenderResult(IReadOnlyList<PacketDocument> Documents, IReadOnlyList<string> Omitted);

/// <summary>Pure rendering: callers supply already-authorized snapshots and record all artifacts atomically.</summary>
public sealed class AnnualPacketComposer(AgencyReleasePdfGenerator release, DhhsFormFiller dhhs,
    DocumentTemplatePdfComposer templates, SafetyPlanPdfGenerator safety)
{
    public PacketRenderResult Render(PacketRenderInput input)
    {
        var files = new List<PacketDocument>(); var omitted = new List<string>();
        var context = new DocumentTemplateRenderContext(input.Subject.AgencyName, input.Subject.AgencyAddress,
            input.Subject.AgencyPhone, input.Subject.ConsumerName, input.Subject.BirthDate, input.CycleStart, input.CycleEnd,
            input.Subject.CaseManagerName, input.Subject.CaseManagerRole, input.ProviderName, input.ProviderAddress, input.ProviderPhone);
        foreach (var kind in new[] { AnnualDocumentKind.PrivacyPractices, AnnualDocumentKind.MedicalRecordsRequest })
        {
            if (kind == AnnualDocumentKind.MedicalRecordsRequest && (!input.MedicalReleaseAttested || string.IsNullOrWhiteSpace(input.ProviderName)))
            { omitted.Add("Medical records request omitted: attest the medical release and link a current primary-care provider first."); continue; }
            var template = input.Templates.SingleOrDefault(x => x.Kind == kind.ToString());
            var body = template?.Body ?? (kind == AnnualDocumentKind.PrivacyPractices ? SatiDefaultDocumentTemplates.PrivacyPracticesBody : RecordsRequestBody);
            var rendered = templates.Generate(kind, body, context, input.GeneratedAtUtc);
            files.Add(new(kind, rendered.Pdf, $"{kind}-{input.Subject.PersonId}.pdf", DocumentArtifactOrigin.GeneratedInSati,
                kind == AnnualDocumentKind.MedicalRecordsRequest
                    ? rendered.BlankFields.Concat(["Requested records and date range (staff review)"]).ToArray()
                    : rendered.BlankFields,
                template?.Owner ?? "SatiDefault", kind.ToString(), template?.Version ?? 1));
        }
        var plan = input.SafetyPlan;
        var planJson = plan?.DocumentJson ?? SafetyPlanRules.EmptyDocumentJson();
        if (SafetyPlanRules.Validate(planJson, plan?.Status == "Approved").Count > 0)
            throw new InvalidOperationException("The safety plan contains invalid data; reload and review it.");
        var content = JsonSerializer.Deserialize<SafetyPlanDocument>(planJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var origin = plan?.Status == "Approved" ? DocumentArtifactOrigin.GeneratedInSati : DocumentArtifactOrigin.Draft;
        files.Add(new(AnnualDocumentKind.SafetyPlan,
            safety.Generate(input.Subject.ConsumerName ?? "", input.CycleStart, content, plan?.Status ?? "Draft", input.GeneratedAtUtc),
            $"Safety-Plan-{(origin == DocumentArtifactOrigin.Draft ? "DRAFT-" : "")}{input.Subject.PersonId}.pdf", origin,
            content.Sections.Where(x => string.IsNullOrWhiteSpace(x.Text)).Select(x => x.Id).ToArray(),
            SourceContentId: plan?.Id, SourceContentVersion: plan?.Version));
        foreach (var kind in new[] { AnnualDocumentKind.ReleaseAgency, AnnualDocumentKind.ReleaseMedical, AnnualDocumentKind.ReleaseDhhs })
        {
            if (input.LiveArtifacts.Any(x => x.Kind == kind.ToString() && x.Origin != "Draft"))
            { omitted.Add($"{AnnualDocumentCatalog.ForKind(kind).DisplayName}: a completed or external copy is already recorded. Retrieve that exact saved/signed copy; it is not reconstructed from metadata."); continue; }
            byte[] pdf;
            if (kind == AnnualDocumentKind.ReleaseDhhs)
                pdf = dhhs.Fill(DhhsFormDefinition.FormKey.AuthorizationToRelease,
                    new(input.Subject.ConsumerName, input.Subject.BirthDate, null, null, null, null, null, null, null),
                    DhhsFormDefinition.Selections.None);
            else
            {
                var choices = new AgencyReleaseRequest(null, null, null, null, null, null, null, null, null, null,
                    null, null, null, null, null, null, null, null, null, IsDraft: true);
                pdf = kind == AnnualDocumentKind.ReleaseAgency ? release.Generate(input.Subject, choices, input.GeneratedAtUtc)
                    : release.GenerateMedical(input.Subject, choices, input.GeneratedAtUtc);
            }
            files.Add(new(kind, pdf, $"{kind}-DRAFT-{input.Subject.PersonId}.pdf", DocumentArtifactOrigin.Draft,
                ["Recipient", "Disclosure choices", "Authorization", "Signatures"]));
        }
        return new(files, omitted);
    }

    public static byte[] Zip(PacketRenderInput input, PacketRenderResult rendered, IReadOnlyList<DocumentArtifactDto> recorded)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var manifest = new StringBuilder()
                .AppendLine("SATI ANNUAL DOCUMENT PACKET")
                .AppendLine($"Consumer record: {input.Subject.PersonId}; cycle: {input.CycleStart:yyyy-MM-dd} through {input.CycleEnd:yyyy-MM-dd}")
                .AppendLine($"Generated UTC: {input.GeneratedAtUtc:O}; staff: {input.ActorId} ({input.Subject.CaseManagerName})")
                .AppendLine("Draft releases require recipient/scope choices and authorization. PDFs do not attest form completion.")
                .AppendLine("Privacy notice receipt: not yet recorded for this newly generated copy.")
                .AppendLine("Medical records requests are downloads only. Staff must verify authorization, recipient and scope before sending.")
                .AppendLine();
            foreach (var file in rendered.Documents)
            {
                using (var target = zip.CreateEntry(file.FileName).Open()) target.Write(file.Pdf);
                var artifact = recorded.Single(x => x.Kind == file.Kind.ToString());
                var actual = Convert.ToHexString(SHA256.HashData(file.Pdf));
                if (!DocumentVerification.Matches(artifact.ContentSha256, artifact.ByteCount, new(artifact.Id, actual, file.Pdf.LongLength)))
                    throw new InvalidOperationException("The generated document did not match its recorded hash.");
                manifest.AppendLine($"{AnnualDocumentCatalog.ForKind(file.Kind).DisplayName}: {file.Origin}")
                    .AppendLine($"File: {file.FileName}; artifact: {artifact.Id}; SHA-256: {actual}")
                    .AppendLine($"Template: {file.TemplateOwner}/{file.TemplateKey}/{file.TemplateVersion}; content: {file.SourceContentId}/{file.SourceContentVersion}")
                    .AppendLine("Blank fields: " + string.Join(", ", file.BlankFields)).AppendLine();
            }
            foreach (var reason in rendered.Omitted) manifest.AppendLine(reason);
            using var writer = new StreamWriter(zip.CreateEntry("MANIFEST.txt").Open(), new UTF8Encoding(false));
            writer.Write(manifest.ToString());
        }
        return stream.ToArray();
    }
    private const string RecordsRequestBody = """
# Medical records request
Prepared for staff review and delivery - not sent by Sati.
## Recipient
{{provider.name}}
{{provider.address}}
Phone: {{provider.phone}}
## Consumer
{{consumer.full_name}}, date of birth {{consumer.birth_date}}
## Request
Please provide the records permitted by the consumer's applicable authorization to {{agency.name}}.
Requested records and date range (staff must complete): ________________________________________
Confirm the authorization covers this recipient, scope and delivery before sending. Attach the applicable authorization as required by agency procedure.
## Return contact
{{agency.name}}
{{agency.address}}
{{agency.phone}}
Case manager: {{case_manager.name}}
Prepared for the cycle {{cycle.start}} through {{cycle.end}}.
""";
}
