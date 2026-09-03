using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class DocumentArtifactApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ExternalDocumentRouteDoesNotExposeAnotherAgency()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/people/201/documents/{AnnualDocumentKind.ReleaseMedical}/external",
            new RecordExternalDocumentRequest(new DateTime(2026, 1, 1), "Verified external record."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExternalDocumentRejectsANoteThatCannotFitTheProtectedRecord()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/people/101/documents/{AnnualDocumentKind.ReleaseMedical}/external",
            new RecordExternalDocumentRequest(
                new DateTime(2026, 1, 1),
                new string('x', AnnualDocumentRules.ExternalNoteMaxLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtherDocumentRoutesDoNotExposeAnotherAgency()
    {
        const int otherPersonId = 201;
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var otherFormId = await factory.CreateOutstandingFormAsync(otherPersonId, "Release_Medical");

        var render = await client.PostAsJsonAsync(
            $"/api/v1/people/{otherPersonId}/documents/{AnnualDocumentKind.ReleaseMedical}",
            new RenderAnnualDocumentRequest(Release: ValidRelease()));
        var list = await client.GetAsync(
            $"/api/v1/people/{otherPersonId}/documents?cycleStart=2026-01-01");
        var prerequisite = await client.GetAsync(
            $"/api/v1/people/{otherPersonId}/forms/Release_Medical/prerequisite?formId={otherFormId}");

        Assert.Equal(HttpStatusCode.NotFound, render.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prerequisite.StatusCode);
    }

    [Fact]
    public async Task DhhsDraftIsSupersededByACompletedReleaseArtifact()
    {
        const int personId = 101;
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        await factory.DeleteDocumentArtifactsAsync(personId, AnnualDocumentKind.ReleaseDhhs);
        try
        {
            (await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms.pdf",
                new DhhsFormRequest(nameof(DhhsFormDefinition.FormKey.AuthorizationToRelease))))
                .EnsureSuccessStatusCode();
            (await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms.pdf",
                new DhhsFormRequest(
                    nameof(DhhsFormDefinition.FormKey.AuthorizationToRelease),
                    new Dictionary<string, bool> { ["ReleaseSend my information to"] = true })))
                .EnsureSuccessStatusCode();

            var person = (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!
                .Single(candidate => candidate.Id == personId);
            var cycleStart = AnnualDocumentCycle.CurrentStart(person.EffectiveDate!.Value, DateTime.Today);
            var artifacts = await owner.GetFromJsonAsync<List<DocumentArtifactDto>>(
                $"/api/v1/people/{personId}/documents?cycleStart={cycleStart:yyyy-MM-dd}");
            var artifact = Assert.Single(artifacts!, item => item.Kind == AnnualDocumentKind.ReleaseDhhs.ToString());
            Assert.Equal(DocumentArtifactOrigin.GeneratedInSati.ToString(), artifact.Origin);
        }
        finally
        {
            await factory.DeleteDocumentArtifactsAsync(personId, AnnualDocumentKind.ReleaseDhhs);
        }
    }

    [Fact]
    public async Task MedicalDraftDoesNotSatisfyPrerequisiteButFinishedGeneratorOutputDoes()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        const int personId = 101;
        var formId = await factory.CreateOutstandingFormAsync(personId, "Release_Medical");
        await factory.DeleteDocumentArtifactsAsync(personId, AnnualDocumentKind.ReleaseMedical);
        var route = $"/api/v1/people/{personId}/documents/{AnnualDocumentKind.ReleaseMedical}";

        try
        {
            var draft = ValidRelease() with { IsDraft = true, ConfirmedObtainedRoi = false };
            (await owner.PostAsJsonAsync(route, new RenderAnnualDocumentRequest(Release: draft)))
                .EnsureSuccessStatusCode();
            var rejected = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Release_Medical/attestation",
                new AttestFormRequest(formId, DateTime.Today));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

            (await owner.PostAsJsonAsync(route, new RenderAnnualDocumentRequest(Release: ValidRelease())))
                .EnsureSuccessStatusCode();
            var accepted = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Release_Medical/attestation",
                new AttestFormRequest(formId, DateTime.Today));
            accepted.EnsureSuccessStatusCode();
        }
        finally
        {
            var revoke = await owner.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Release_Medical/attestation/revoke",
                new RevokeFormAttestationRequest(formId, "Document prerequisite test cleanup."));
            if (revoke.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict))
                revoke.EnsureSuccessStatusCode();
            await factory.DeleteDocumentArtifactsAsync(personId, AnnualDocumentKind.ReleaseMedical);
        }
    }

    [Fact]
    public async Task SupervisorMayOverrideMissingPrerequisiteWithTechnicalReason()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        const int personId = 101;
        var formId = await factory.CreateOutstandingFormAsync(personId, "Release_Medical");
        await factory.DeleteDocumentArtifactsAsync(personId, AnnualDocumentKind.ReleaseMedical);
        var before = await factory.GetAuditEventsAsync("form.prerequisite-overridden");

        try
        {
            var response = await supervisor.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Release_Medical/attestation",
                new AttestFormRequest(
                    formId, DateTime.Today, SupervisorOverrideReason: "PDF generation service was unavailable."));

            response.EnsureSuccessStatusCode();
            var after = await factory.GetAuditEventsAsync("form.prerequisite-overridden");
            Assert.Equal(before.Count + 1, after.Count);
            Assert.Contains(nameof(PrerequisiteKind.DocumentArtifact), after[^1].MetadataJson);
            Assert.DoesNotContain("PDF generation service was unavailable", after[^1].MetadataJson);
            Assert.Equal(
                "PDF generation service was unavailable.",
                await factory.GetLatestFormAttestationReasonAsync(formId));
        }
        finally
        {
            var revoke = await supervisor.PostAsJsonAsync(
                $"/api/v1/people/{personId}/forms/Release_Medical/attestation/revoke",
                new RevokeFormAttestationRequest(formId, "Supervisor override test cleanup."));
            if (revoke.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict))
                revoke.EnsureSuccessStatusCode();
        }
    }

    private static AgencyReleaseRequest ValidRelease() => new(
        true, "Healthcare provider", "Medical Records", "Provider",
        "1 Medical Center", "Portland", "ME", null, "207-555-0100", null,
        [AgencyReleaseInformation.Evaluations], null,
        DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        nameof(AgencyReleaseScope.OneTime), false, false, false, false,
        ConfirmedObtainedRoi: true);
}
