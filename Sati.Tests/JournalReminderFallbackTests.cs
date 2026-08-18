using System.Net;
using System.Text;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What the Demo client does when it is NEWER than the API it is talking to.
/// The journal-entries route answers 404 on a server that predates it, which is
/// indistinguishable at the status line from a client that is genuinely absent or
/// out of scope — so the client asks a second question before deciding, and only
/// falls back when the person demonstrably reads back.
///
/// The fallback is transitional. These tests are also its removal condition: when
/// no reachable deployment predates the route, they and it go together.
/// </summary>
public sealed class JournalReminderFallbackTests
{
    private const int PersonId = 1217;
    private const string EntriesRoute = "/api/v1/people/1217/journal/entries";
    private const string JournalRoute = "/api/v1/people/1217/journal";

    private static CloudPersonService ServiceOver(RecordingHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo.sati.invalid/") };
        var api = new CloudApiClient(http);
        api.SetAccessToken("test-token");
        return new CloudPersonService(api);
    }

    [Fact]
    public async Task ACurrentServerWritesThroughTheEntriesRouteAndNothingFallsBack()
    {
        using var handler = new RecordingHandler();
        handler.Respond(EntriesRoute, HttpStatusCode.OK, "\"August 18, 2026 3:42 PM — REMINDER\\r\\nCall back\"");

        var result = await ServiceOver(handler).AddJournalReminderAsync(PersonId, "Call back");

        Assert.False(result.UsedLegacyJournalWrite);
        Assert.Contains("Call back", result.Journal);
        Assert.Equal([$"POST {EntriesRoute}"], handler.Calls);
    }

    [Fact]
    public async Task AServerWithoutTheRouteStillGetsTheReminderAndSaysSo()
    {
        using var handler = new RecordingHandler();
        handler.Respond(EntriesRoute, HttpStatusCode.NotFound, string.Empty);
        handler.Respond(JournalRoute, HttpStatusCode.OK, "\"Guardian prefers afternoon calls.\"");

        var result = await ServiceOver(handler).AddJournalReminderAsync(PersonId, "Call the guardian.");

        Assert.True(result.UsedLegacyJournalWrite);
        Assert.Contains(JournalEntry.ReminderLabel, result.Journal);
        Assert.Contains("Call the guardian.", result.Journal);
        Assert.EndsWith("Guardian prefers afternoon calls.", result.Journal);

        // Tried the route, read the journal to disambiguate, wrote it back whole.
        Assert.Equal(
            [$"POST {EntriesRoute}", $"GET {JournalRoute}", $"PUT {JournalRoute}"],
            handler.Calls);
        Assert.Contains("Call the guardian.", handler.LastPutBody);
    }

    [Fact]
    public async Task AClientWhoseJournalIsStillEmptyIsNotAFailure()
    {
        using var handler = new RecordingHandler();
        handler.Respond(EntriesRoute, HttpStatusCode.NotFound, string.Empty);
        // An unset journal comes back as an EMPTY body, not as JSON null.
        handler.Respond(JournalRoute, HttpStatusCode.OK, string.Empty);

        var result = await ServiceOver(handler).AddJournalReminderAsync(PersonId, "First entry");

        Assert.True(result.UsedLegacyJournalWrite);
        Assert.StartsWith("August", result.Journal);
        Assert.EndsWith("First entry", result.Journal);
    }

    [Fact]
    public async Task AGenuinelyMissingClientStillFailsAndWritesNothing()
    {
        using var handler = new RecordingHandler();
        handler.Respond(EntriesRoute, HttpStatusCode.NotFound, string.Empty);
        handler.Respond(JournalRoute, HttpStatusCode.NotFound, string.Empty);

        var error = await Assert.ThrowsAsync<CloudApiException>(
            () => ServiceOver(handler).AddJournalReminderAsync(PersonId, "Not yours."));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.DoesNotContain($"PUT {JournalRoute}", handler.Calls);
    }

    [Fact]
    public async Task AScopeRefusalIsNotTreatedAsAMissingRoute()
    {
        using var handler = new RecordingHandler();
        handler.Respond(EntriesRoute, HttpStatusCode.Forbidden, string.Empty);

        var error = await Assert.ThrowsAsync<CloudApiException>(
            () => ServiceOver(handler).AddJournalReminderAsync(PersonId, "Refused."));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal([$"POST {EntriesRoute}"], handler.Calls);
    }

    /// <summary>
    /// The empty-journal read that <c>GetAsync&lt;string?&gt;</c> could not express,
    /// checked directly against the journal loader the client page uses.
    /// </summary>
    [Fact]
    public async Task ReadingAnEmptyJournalReturnsNullRatherThanThrowing()
    {
        using var handler = new RecordingHandler();
        handler.Respond(JournalRoute, HttpStatusCode.OK, string.Empty);

        var journal = await ServiceOver(handler).GetJournalAsync(PersonId);

        Assert.Null(journal);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];
        public string LastPutBody { get; private set; } = string.Empty;

        public void Respond(string path, HttpStatusCode status, string body) =>
            _responses[path] = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Put && request.Content is not null)
                LastPutBody = await request.Content.ReadAsStringAsync(cancellationToken);

            // An unmapped path is what a server without the route actually does.
            if (!_responses.TryGetValue(path, out var configured))
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty)
                };

            // PUT journal answers 204 on the real API.
            if (request.Method == HttpMethod.Put && configured.Status == HttpStatusCode.OK)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            return new HttpResponseMessage(configured.Status)
            {
                Content = new StringContent(configured.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
