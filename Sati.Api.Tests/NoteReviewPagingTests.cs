using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;
namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class NoteReviewPagingTests(SatiApiFactory factory)
{
    [Fact]
    public async Task InitialCloudRequestSupportsOmittedOptionalCursorAndFilter()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var page = await client.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            "/api/v1/supervisor/notes/page?afterId=0");
        Assert.NotNull(page);
        Assert.InRange(page.Notes.Count, 0, 10);
        Assert.All(page.Notes, note => Assert.Equal(12, note.Person!.UserId));
    }

    [Fact]
    public async Task FilterChoicesAndAllFourFiltersStayInsideReviewScope()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var narrative = $"review-filter-{Guid.NewGuid():N}";
        var eventDate = new DateTime(2026, 9, 6);
        var createdResponse = await caseManager.PostAsJsonAsync("/api/v1/notes", new SaveNoteRequest(
            narrative, eventDate, "Logged", 15, null, 101, null, "Contact", null, null));
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<NoteDto>();
        var options = await client.GetFromJsonAsync<NoteReviewFilterOptions>(
            "/api/v1/supervisor/notes/filters");

        Assert.NotNull(options);
        Assert.Contains(options.CaseManagers, option => option.UserId == 12);
        Assert.Contains(options.Clients, option => option.PersonId == 101 && option.UserId == 12);
        Assert.DoesNotContain(options.Clients, option => option.PersonId == 201);

        var page = await client.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            "/api/v1/supervisor/notes/page?userId=12&personId=101" +
            $"&fromDate=2026-09-06&toDate=2026-09-06&searchTerm={Uri.EscapeDataString(narrative)}");

        var note = Assert.Single(page!.Notes);
        Assert.Equal(created!.Id, note.Id);
    }

    [Fact]
    public async Task PersonFilterOutsideReviewScopeIsForbidden()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");

        var response = await client.GetAsync(
            "/api/v1/supervisor/notes/page?personId=201");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("case-manager-one")]
    [InlineData("supervisor-two")]
    public async Task BatchThresholdCannotGrantReviewAccess(string username)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(username);
        var id = await factory.CreateNoteInStatusAsync(2, noteType: 1);
        var (_, revision) = await factory.GetNoteStateAsync(id);
        var response = await client.PostAsJsonAsync($"/api/v1/supervisor/notes/{id}/approve",
            new SupervisorNoteActionRequest(null, revision, 4));
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(2, (await factory.GetNoteStateAsync(id)).Item1);
    }

    [Fact]
    public async Task PagesAreBoundedAndDoNotSkipAfterApproval()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var personId = await factory.CreateBillingWorkflowPersonAsync();
        var ids = new List<int>();
        for (var i = 0; i < 23; i++)
            ids.Add(await factory.CreateNoteInStatusAsync(2, personId));
        var first = await client.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            $"/api/v1/supervisor/notes/page?afterId=0&throughId={ids[^1]}&userId=12&personId={personId}");
        Assert.NotNull(first);
        Assert.Equal(10, first.Notes.Count);
        foreach (var note in first.Notes)
        {
            var response = await client.PostAsJsonAsync($"/api/v1/supervisor/notes/{note.Id}/approve",
                new SupervisorNoteActionRequest(null, note.Revision));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var second = await client.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            $"/api/v1/supervisor/notes/page?afterId={first.NextAfterId}&throughId={first.ThroughId}&userId=12&personId={personId}");
        var third = await client.GetFromJsonAsync<NoteReviewPage<NoteDto>>(
            $"/api/v1/supervisor/notes/page?afterId={second!.NextAfterId}&throughId={first.ThroughId}&userId=12&personId={personId}");
        Assert.Equal(10, second.Notes.Count);
        Assert.Equal(3, third!.Notes.Count);
        Assert.Null(third.NextAfterId);
        Assert.Equal(ids.AsEnumerable().Reverse(),
            first.Notes.Concat(second.Notes).Concat(third.Notes).Select(n => n.Id));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/supervisor/notes/page?userId=22")).StatusCode);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(0, false)]
    public async Task BatchThresholdIsCheckedByTheServer(int minutes, bool allowed)
    {
        using var client = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var id = await factory.CreateNoteInStatusAsync(2, minutes: minutes, noteType: 1);
        var (_, revision) = await factory.GetNoteStateAsync(id);
        var response = await client.PostAsJsonAsync($"/api/v1/supervisor/notes/{id}/approve",
            new SupervisorNoteActionRequest(null, revision, 4));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        var (status, _) = await factory.GetNoteStateAsync(id);
        Assert.Equal(allowed ? 6 : 2, status);
    }
}
