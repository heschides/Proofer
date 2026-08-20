using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The routes that store and use an SSN.
///
/// The point of most of these is what does NOT come back. An SSN is decrypted in
/// exactly one place — the form fill — and leaves this process only as pixels inside
/// a PDF. Everything else, including the route that just stored it, answers with a
/// mask.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class SsnAndFormApiTests
{
    private const int OwnPerson = 101;
    private const int OtherAgencyPerson = 201;
    private const string Ssn = "123-45-6789";

    private readonly SatiApiFactory _factory;

    public SsnAndFormApiTests(SatiApiFactory factory) => _factory = factory;

    private static string SsnRoute(int personId) => $"/api/v1/people/{personId}/ssn";
    private static string FormRoute(int personId) => $"/api/v1/people/{personId}/forms.pdf";

    private Task<HttpClient> CaseManagerAsync() =>
        _factory.CreateAuthenticatedClientAsync("case-manager-one");

    [Fact]
    public async Task AnonymousCallerCannotReadOrWriteAnSsn()
    {
        using var client = _factory.CreateAnonymousClient();

        var read = await client.GetAsync(SsnRoute(OwnPerson));
        var write = await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    /// <summary>
    /// A consumer on another agency's caseload is not found, not forbidden — the same
    /// answer the other person routes give, so the route does not confirm that a
    /// person id exists in a tenant the caller cannot see.
    /// </summary>
    [Fact]
    public async Task AnotherAgencysConsumerIsNotReachable()
    {
        using var client = await CaseManagerAsync();

        var read = await client.GetAsync(SsnRoute(OtherAgencyPerson));
        var write = await client.PutAsJsonAsync(SsnRoute(OtherAgencyPerson), new SsnUpdateRequest(Ssn));
        var form = await client.PostAsJsonAsync(
            FormRoute(OtherAgencyPerson),
            new DhhsFormRequest(nameof(DhhsFormDefinition.FormKey.AuthorizedRepresentative)));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, form.StatusCode);
    }

    /// <summary>
    /// Storing a number answers with the mask. This is the containment rule at its
    /// most tempting point: the caller just sent the number, so echoing it back feels
    /// harmless — and would put a plaintext SSN in a response body, a proxy log, and
    /// a client cache.
    /// </summary>
    [Fact]
    public async Task StoringAnSsnAnswersWithTheMaskAndNeverTheNumber()
    {
        using var client = await CaseManagerAsync();

        var response = await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("123456789", body);
        Assert.DoesNotContain("123-45-6789", body);
        Assert.Contains("***-**-6789", body);

        var status = await response.Content.ReadFromJsonAsync<SsnStatusDto>();
        Assert.True(status!.IsOnFile);
        Assert.Equal("***-**-6789", status.Masked);
    }

    [Fact]
    public async Task ReadingBackReturnsOnlyTheMask()
    {
        using var client = await CaseManagerAsync();
        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));

        var response = await client.GetAsync(SsnRoute(OwnPerson));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("123456789", body);
        Assert.Contains("***-**-6789", body);
    }

    /// <summary>
    /// Shape-checked before encryption, because afterwards nothing can look at it
    /// again. A transposed digit that reaches an official form is a rejected
    /// application.
    /// </summary>
    [Theory]
    [InlineData("666-12-3456")]
    [InlineData("123-00-6789")]
    [InlineData("12345")]
    public async Task ANumberThatIsNeverIssuedIsRejected(string candidate)
    {
        using var client = await CaseManagerAsync();

        var response = await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(candidate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Clearing removes the tail as well. Leaving it would keep a consumer who asked
    /// to be removed partially on file, with a mask claiming a number that can no
    /// longer be produced.
    /// </summary>
    [Fact]
    public async Task ClearingRemovesTheMaskToo()
    {
        using var client = await CaseManagerAsync();
        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));

        var response = await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(null));
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<SsnStatusDto>();
        Assert.False(status!.IsOnFile);
        Assert.Equal(SsnMask.NotOnFile, status.Masked);
    }

    /// <summary>
    /// The one operation permitted to decrypt. The number reaches the caller only
    /// inside the PDF — which is the disclosure this whole feature exists to produce,
    /// and the reason the fill is audited.
    /// </summary>
    [Fact]
    public async Task TheFormFillIsTheOnlyPlaceTheNumberComesBack()
    {
        using var client = await CaseManagerAsync();
        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));

        var response = await client.PostAsJsonAsync(
            FormRoute(OwnPerson),
            new DhhsFormRequest(nameof(DhhsFormDefinition.FormKey.AuthorizedRepresentative)));
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var pdf = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(pdf);
    }

    /// <summary>
    /// A selection naming a demographic box is refused rather than ignored. Silently
    /// dropping it would let a case manager believe they recorded a consumer's choice
    /// that the PDF never received.
    /// </summary>
    [Fact]
    public async Task ASelectionThatIsNotAConsentFieldIsRefused()
    {
        using var client = await CaseManagerAsync();

        var response = await client.PostAsJsonAsync(
            FormRoute(OwnPerson),
            new DhhsFormRequest(
                nameof(DhhsFormDefinition.FormKey.AuthorizedRepresentative),
                Checks: new Dictionary<string, bool> { ["Individual's Name"] = true }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownFormIsRefused()
    {
        using var client = await CaseManagerAsync();

        var response = await client.PostAsJsonAsync(
            FormRoute(OwnPerson), new DhhsFormRequest("NotARealForm"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Nothing the SSN paths log contains the number.
    ///
    /// Redaction fails silently by nature — the request succeeds, the response is
    /// correct, and the number is in a log file. The only way to hold the line is to
    /// read the log and look. This exercises the write, the read-back, a rejected
    /// number, and the form fill, because the rejection path is where a value is most
    /// likely to end up quoted in a message.
    /// </summary>
    [Fact]
    public async Task NothingTheSsnPathsLogEverContainsTheNumber()
    {
        using var client = await CaseManagerAsync();
        CapturingLoggerProvider.Clear();

        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));
        await client.GetAsync(SsnRoute(OwnPerson));
        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest("666-12-3456"));
        await client.PostAsJsonAsync(
            FormRoute(OwnPerson),
            new DhhsFormRequest(nameof(DhhsFormDefinition.FormKey.AuthorizedRepresentative)));

        var logged = CapturingLoggerProvider.Captured();
        Assert.DoesNotContain("123456789", logged);
        Assert.DoesNotContain("123-45-6789", logged);
        Assert.DoesNotContain("666123456", logged);
        Assert.DoesNotContain("666-12-3456", logged);
    }

    /// <summary>
    /// A rejected number is not quoted back. The validation message has to say what is
    /// wrong without repeating the value, or the response body and every log that
    /// touches it carry the number the request was rejected for.
    /// </summary>
    [Fact]
    public async Task ARejectionDoesNotQuoteTheNumberItRejected()
    {
        using var client = await CaseManagerAsync();

        var response = await client.PutAsJsonAsync(
            SsnRoute(OwnPerson), new SsnUpdateRequest("666-12-3456"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("666", body);
    }

    /// <summary>
    /// Rotation is a non-event for stored rows. The row records the key version that
    /// wrapped it, so a number stored before a rotation still fills a form after one —
    /// no backfill, no downtime, no unreadable consumers.
    /// </summary>
    [Fact]
    public async Task ANumberStoredBeforeARotationStillFillsAForm()
    {
        using var client = await CaseManagerAsync();
        await client.PutAsJsonAsync(SsnRoute(OwnPerson), new SsnUpdateRequest(Ssn));

        SatiApiFactory.TestVault.Rotate();

        var response = await client.PostAsJsonAsync(
            FormRoute(OwnPerson),
            new DhhsFormRequest(nameof(DhhsFormDefinition.FormKey.AuthorizedRepresentative)));

        response.EnsureSuccessStatusCode();
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }
}
