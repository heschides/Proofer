using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The journal-entry route the Reminder note type writes through. The server owns
/// the prepend and the stamp, so these tests are about placement, the agency
/// clock, and scope — the desktop mirror of the same rules is in
/// Sati.Tests JournalReminderTests.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class JournalReminderApiTests
{
    private readonly SatiApiFactory _factory;

    public JournalReminderApiTests(SatiApiFactory factory) => _factory = factory;

    private static string Route(int personId) => $"/api/v1/people/{personId}/journal/entries";

    // A person whose journal was never written returns an empty body rather than
    // a JSON string, so the raw content is read instead of deserialized.
    private static async Task<string?> ReadJournalAsync(HttpClient client, int personId)
    {
        var response = await client.GetAsync($"/api/v1/people/{personId}/journal");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) || body == "null"
            ? null
            : JsonSerializer.Deserialize<string>(body);
    }

    [Fact]
    public async Task AnonymousCallerCannotAddAReminder()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route(101), new AddJournalReminderRequest("Anonymous reminder"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheEntryIsStoredAtTheTopAndTheStoredJournalComesBack()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest("Send the signed release."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadFromJsonAsync<string>();
        var stored = await ReadJournalAsync(client, personId);
        Assert.Equal(stored, returned);
        Assert.NotNull(returned);
        // The stamp is the FIRST line; the text follows it.
        var lines = returned!.Split("\r\n");
        Assert.Contains(JournalEntry.ReminderLabel, lines[0]);
        Assert.Equal("Send the signed release.", lines[1]);
    }

    [Fact]
    public async Task JournalTextTheCaseManagerTypedSurvivesUnderneathTheEntry()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        await client.PutAsJsonAsync(
            $"/api/v1/people/{personId}/journal",
            new SaveJournalRequest("Guardian prefers afternoon calls."));

        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest("Call the guardian."));

        var journal = await response.Content.ReadFromJsonAsync<string>();
        Assert.Contains("Call the guardian.", journal);
        Assert.EndsWith("Guardian prefers afternoon calls.", journal);
    }

    [Fact]
    public async Task TheNewestEntryIsAboveTheOlderOne()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        await client.PostAsJsonAsync(Route(personId), new AddJournalReminderRequest("Older entry"));
        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest("Newer entry"));

        var journal = await response.Content.ReadFromJsonAsync<string>();
        Assert.NotNull(journal);
        Assert.True(
            journal!.IndexOf("Newer entry", StringComparison.Ordinal) <
            journal.IndexOf("Older entry", StringComparison.Ordinal),
            "The newest entry must be above the older one.");
    }

    /// <summary>
    /// The stamp is what a case manager reads back, so it has to be the agency's
    /// wall clock. An Azure host's own local time is UTC, which in Maine is four or
    /// five hours ahead of the clock the case manager just looked at — this fails
    /// if the endpoint ever stamps from UtcNow instead of the agency clock.
    /// </summary>
    [Fact]
    public async Task TheStampIsTheAgencyWallClockAndNotTheHostsUtcTime()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var easternNow = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).DateTime;

        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest("Stamp check"));

        var journal = await response.Content.ReadFromJsonAsync<string>();
        var stampLine = journal!.Split("\r\n")[0];
        var stampText = stampLine.Split(" — ")[0];
        var stamped = DateTime.ParseExact(
            stampText, "MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
        Assert.True(
            Math.Abs((stamped - easternNow).TotalMinutes) < 5,
            $"Stamp {stamped} is not the agency wall clock {easternNow}.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyReminderIsRefused(string text)
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(Route(personId), new AddJournalReminderRequest(text));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var journal = await ReadJournalAsync(client, personId);
        Assert.Null(journal);
    }

    [Fact]
    public async Task AReminderLongerThanTheContractAllowsIsRefused()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var tooLong = new string('x', JournalEntry.MaxTextLength + 1);

        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest(tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var journal = await ReadJournalAsync(client, personId);
        Assert.Null(journal);
    }

    [Fact]
    public async Task AnotherAgencysClientCannotBeGivenAReminder()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.PostAsJsonAsync(
            Route(201), new AddJournalReminderRequest("Cross-agency reminder"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var ownerClient = await _factory.CreateAuthenticatedClientAsync("case-manager-two");
        var journal = await ownerClient.GetFromJsonAsync<string>("/api/v1/people/201/journal");
        Assert.Equal("Agency two journal", journal);
    }

    [Fact]
    public async Task AClientOnAnotherCaseloadInTheSameAgencyCannotBeGivenAReminder()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var response = await client.PostAsJsonAsync(
            Route(personId), new AddJournalReminderRequest("Not my caseload"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var ownerClient = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var journal = await ReadJournalAsync(ownerClient, personId);
        Assert.Null(journal);
    }

    [Fact]
    public async Task TheWriteIsAuditedAsAReminderAgainstTheActingUser()
    {
        var personId = await _factory.CreateBillingWorkflowPersonAsync();
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        await client.PostAsJsonAsync(Route(personId), new AddJournalReminderRequest("Audited reminder"));

        var events = await _factory.GetAuditEventsAsync("person.journal-reminder-added");
        var entry = Assert.Single(
            events,
            x => x.ResourceId == personId.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("Person", entry.ResourceType);
        Assert.Equal(12, entry.ActorUserId);
        Assert.Equal(1, entry.AgencyId);
    }
}
