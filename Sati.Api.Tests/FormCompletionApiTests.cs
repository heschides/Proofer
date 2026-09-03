using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormCompletionApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AttestationStoresTheEnteredDateInsteadOfSynthesizingTodayOrDueDate()
    {
        _ = await factory.CreateNonCompliantReviewNoteAsync();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var people = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var person = people!.Single(candidate => candidate.Id == 102);
        var form = person.Forms.First(candidate => !candidate.IsCompliant);
        var completedOn = DateTime.Today.AddDays(-5);
        var auditBefore = await factory.GetAuditEventsAsync("form.attested");

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/people/{person.Id}/forms/{form.Type}/attestation",
            new { FormId = form.Id, CompletedOn = completedOn, EvidenceNoteId = (int?)null });

        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<FormDto>();
        Assert.NotNull(saved);
        Assert.Equal(completedOn.Date, saved.CompletedDate);
        Assert.NotEqual(DateTime.Today, saved.CompletedDate);
        Assert.NotEqual(form.DueDate.Date, saved.CompletedDate);

        var auditAfter = await factory.GetAuditEventsAsync("form.attested");
        var audit = Assert.Single(auditAfter.Skip(auditBefore.Count));
        Assert.Equal(form.Id.ToString(), audit.ResourceId);
        Assert.Contains(completedOn.ToString("yyyy-MM-dd"), audit.MetadataJson);

        var revoke = await owner.PostAsJsonAsync(
            $"/api/v1/people/{person.Id}/forms/{form.Type}/attestation/revoke",
            new { FormId = form.Id, Reason = "API regression-test cleanup." });
        revoke.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AttestationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("director-one");
        var foreignPeople = await supervisor.GetFromJsonAsync<List<PersonDto>>(
            "/api/v1/caseload?userId=19");
        var foreignForm = foreignPeople!
            .Single(candidate => candidate.Id == 103)
            .Forms.First();

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/people/103/forms/{foreignForm.Type}/attestation",
            new { FormId = foreignForm.Id, CompletedOn = DateTime.Today, EvidenceNoteId = (int?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PendingAttestationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await owner.GetAsync("/api/v1/people/103/attestations/pending");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PendingRouteReturnsMatchingEvidenceWithoutCompletingTheForm()
    {
        var evidenceNoteId = await factory.CreatePendingAttestationEvidenceAsync();
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        var pending = await owner.GetFromJsonAsync<List<PendingAttestationDto>>(
            "/api/v1/people/102/attestations/pending");

        var suggestion = Assert.Single(pending!, candidate => candidate.EvidenceNoteId == evidenceNoteId);
        Assert.Equal("PCP", suggestion.FormType);
        var people = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.Null(people!.Single(candidate => candidate.Id == 102).Forms
            .Single(candidate => candidate.Id == suggestion.FormId).CompletedDate);
    }

    [Fact]
    public async Task RevocationRouteDoesNotExposeAnotherCaseload()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await factory.CreateAuthenticatedClientAsync("director-one");
        var foreignPeople = await supervisor.GetFromJsonAsync<List<PersonDto>>(
            "/api/v1/caseload?userId=19");
        var foreignForm = foreignPeople!
            .Single(candidate => candidate.Id == 103)
            .Forms.First();

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/people/103/forms/{foreignForm.Type}/attestation/revoke",
            new RevokeFormAttestationRequest(foreignForm.Id, "Unauthorized attempt."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SimultaneousAttestationsReturnATypedConflictForTheLoser()
    {
        _ = await factory.CreateNonCompliantReviewNoteAsync();
        using var firstClient = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var secondClient = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var people = await firstClient.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var person = people!.Single(candidate => candidate.Id == 102);
        var form = person.Forms.First(candidate => !candidate.IsCompliant);
        var path = $"/api/v1/people/{person.Id}/forms/{form.Type}/attestation";
        var payload = new AttestFormRequest(form.Id, DateTime.Today.AddDays(-3));

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(path, payload),
            secondClient.PostAsJsonAsync(path, payload));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var error = await conflict.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.Equal("form_attestation_changed", error?.Code);
        }
        finally
        {
            var revoke = await firstClient.PostAsJsonAsync(
                $"{path}/revoke",
                new RevokeFormAttestationRequest(form.Id, "Concurrency regression-test cleanup."));
            revoke.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task UpdateRejectsAFutureCompletionDateWithoutChangingStoredState()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = before!
            .SelectMany(person => person.Forms)
            .First();

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(DateTime.Today.AddDays(1), form.OpenedDate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var stored = after!
            .SelectMany(person => person.Forms)
            .Single(candidate => candidate.Id == form.Id);
        Assert.Equal(form.CompletedDate, stored.CompletedDate);
        Assert.Equal(form.IsCompliant, stored.IsCompliant);
    }

    [Fact]
    public async Task UpdateRejectsANonFutureCompletionChange()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = before!.SelectMany(person => person.Forms).First();
        var attempted = form.CompletedDate?.AddDays(-1) ?? DateTime.Today.AddDays(-10);

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(attempted, form.OpenedDate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.Equal(
            form.CompletedDate,
            after!.SelectMany(person => person.Forms)
                .Single(candidate => candidate.Id == form.Id).CompletedDate);
    }
}
