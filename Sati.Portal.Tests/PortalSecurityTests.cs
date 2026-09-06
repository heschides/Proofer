using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Signatures;
using Xunit;

namespace Sati.Portal.Tests;

public sealed class PortalSecurityTests
{
    [Fact]
    public async Task PublicLandingContainsNoClientDataAndSetsPrivacyHeaders()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client();
        var response = await client.GetAsync("/s/" + new string('a', 64)); response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic Person", html);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portal/state")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portal/document.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/people/2")).StatusCode);
    }

    [Fact]
    public async Task HttpsCannotBeForgedWithAnUntrustedForwardingHeader()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/portal/bootstrap");
        request.Headers.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task PostingWithoutTheRequestBoundCsrfTokenCannotConsumeAPinAttempt()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client(); var token = await factory.Issue();
        var rejected = await client.PostAsJsonAsync("/portal/auth", new { token, pin = "12345678" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<SignatureDbContext>().SignatureRequests.SingleAsync()).FailedPinAttempts);
        await Csrf(client);
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/portal/auth", new { token, pin = "12345678" })).StatusCode);
    }

    [Fact]
    public async Task FullSigningUsesSecureCookieAndRevokesOldSigningSession()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client(); var token = await factory.Issue(); await Csrf(client);
        var auth = await client.PostAsJsonAsync("/portal/auth", new { token, pin = PortalFactory.Pin }); auth.EnsureSuccessStatusCode();
        await Bind(client, auth);
        var cookie = auth.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Sati-Signing="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionToken", await auth.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var oldCookie = cookie.Split(';')[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/portal/sign", new { typedName = "Synthetic Person", agreesToIntent = true })).StatusCode);
        var original = await client.GetAsync(DocumentPath(client)); original.EnsureSuccessStatusCode();
        Assert.Equal("attachment", original.Content.Headers.ContentDisposition?.DispositionType);
        (await client.PostAsJsonAsync("/portal/consent", new { canAccessAndRetain = true, acceptsElectronicRecords = true })).EnsureSuccessStatusCode();
        var signed = await client.PostAsJsonAsync("/portal/sign", new { typedName = "Synthetic Person", agreesToIntent = true }); signed.EnsureSuccessStatusCode();
        await Bind(client, signed);
        Assert.Equal("Signed", (await signed.Content.ReadFromJsonAsync<SignaturePortalDetails>())!.State);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/portal/sign", new { typedName = "Synthetic Person", agreesToIntent = true })).StatusCode);
        using var attacker = factory.Client(false); attacker.DefaultRequestHeaders.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync("/portal/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(DocumentPath(client))).StatusCode); // durable decision; package worker has not run in this test.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/portal/auth", new { token, pin = PortalFactory.Pin })).StatusCode);
        (await client.PostAsJsonAsync("/portal/auth", new { token, pin = PortalFactory.Pin, receipt = true })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LogoutRevokesTheServerSessionSoCopiedCookieCannotBeReused()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client(); var token = await factory.Issue(); await Csrf(client);
        var response = await client.PostAsJsonAsync("/portal/auth", new { token, pin = PortalFactory.Pin }); response.EnsureSuccessStatusCode();
        await Bind(client, response);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Sati-Signing=")).Split(';')[0];
        (await client.PostAsJsonAsync("/portal/logout", new { })).EnsureSuccessStatusCode();
        using var copied = factory.Client(false); copied.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.NotFound, (await copied.GetAsync("/portal/state")).StatusCode);
    }

    [Fact]
    public async Task FiveWrongCodesLockAcrossSeparateBrowsers()
    {
        await using var factory = new PortalFactory(); var token = await factory.Issue();
        for (var i = 0; i < 5; i++)
        {
            using var client = factory.Client(); await Csrf(client);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/portal/auth", new { token, pin = "83572019" })).StatusCode);
        }
        using var correct = factory.Client(); await Csrf(correct);
        Assert.Equal(HttpStatusCode.NotFound, (await correct.PostAsJsonAsync("/portal/auth", new { token, pin = PortalFactory.Pin })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(5, (await scope.ServiceProvider.GetRequiredService<SignatureDbContext>().SignatureRequests.SingleAsync()).FailedPinAttempts);
    }

    [Fact]
    public async Task ForgedCookieAndOversizedBodyCannotReachProtectedWork()
    {
        await using var factory = new PortalFactory(); using var client = factory.Client(false);
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Sati-Signing=R." + new string('a', 64));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portal/state")).StatusCode);
        using var oversized = new StringContent("{\"token\":\"" + new string('a', 9000) + "\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/portal/auth", oversized)).StatusCode);
    }

    [Fact]
    public async Task ASecondTabCannotMakeTheFirstTabSignOrDownloadADifferentDocument()
    {
        await using var factory = new PortalFactory(); using var browser = factory.Client();
        var first = await factory.Issue(); await Csrf(browser);
        var authA = await browser.PostAsJsonAsync("/portal/auth", new { token = first, pin = PortalFactory.Pin }); authA.EnsureSuccessStatusCode();
        var bindingA = await Bind(browser, authA);
        (await browser.GetAsync(DocumentPath(browser))).EnsureSuccessStatusCode();
        (await browser.PostAsJsonAsync("/portal/consent", new { canAccessAndRetain = true, acceptsElectronicRecords = true })).EnsureSuccessStatusCode();

        // Browser tabs share the cookie. A second request has the same signer name but a different document.
        var second = await factory.Issue();
        var authB = await browser.PostAsJsonAsync("/portal/auth", new { token = second, pin = PortalFactory.Pin }); authB.EnsureSuccessStatusCode();
        var bindingB = await Bind(browser, authB);
        (await browser.GetAsync(DocumentPath(browser))).EnsureSuccessStatusCode();
        (await browser.PostAsJsonAsync("/portal/consent", new { canAccessAndRetain = true, acceptsElectronicRecords = true })).EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Remove("X-Sati-Session"); browser.DefaultRequestHeaders.Add("X-Sati-Session", bindingA);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/portal/consent", new { canAccessAndRetain = true, acceptsElectronicRecords = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/portal/decision", new { decision = "decline", reason = "Wrong tab must not change this request" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/portal/extend", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsJsonAsync("/portal/logout", new { })).StatusCode);
        var wrongSign = await browser.PostAsJsonAsync("/portal/sign", new { typedName = "Synthetic Person", agreesToIntent = true });
        Assert.Equal(HttpStatusCode.Conflict, wrongSign.StatusCode);
        Assert.Equal("signature_session_changed", (await wrongSign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.GetAsync("/portal/document.pdf?session=" + bindingA)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.GetAsync("/portal/state")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SignatureDbContext>().SignatureCompletions.ToListAsync());
        browser.DefaultRequestHeaders.Remove("X-Sati-Session"); browser.DefaultRequestHeaders.Add("X-Sati-Session", bindingB);
        (await browser.PostAsJsonAsync("/portal/sign", new { typedName = "Synthetic Person", agreesToIntent = true })).EnsureSuccessStatusCode();
    }

    private static async Task<string> Bind(HttpClient client, HttpResponseMessage response)
    {
        var details = JsonSerializer.Deserialize<SignaturePortalDetails>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        client.DefaultRequestHeaders.Remove("X-Sati-Session");
        client.DefaultRequestHeaders.Add("X-Sati-Session", details.SessionBinding);
        return details.SessionBinding;
    }
    private static string DocumentPath(HttpClient client) => "/portal/document.pdf?session=" + client.DefaultRequestHeaders.GetValues("X-Sati-Session").Single();

    private static async Task Csrf(HttpClient client)
    {
        var bootstrap = await client.GetFromJsonAsync<JsonElement>("/portal/bootstrap");
        client.DefaultRequestHeaders.Remove("X-Sati-CSRF");
        client.DefaultRequestHeaders.Add("X-Sati-CSRF", bootstrap.GetProperty("csrfToken").GetString());
    }
}

internal sealed class PortalFactory : WebApplicationFactory<PortalProgram>
{
    public const string Pin = "58392716";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly MemoryBlobs blobs = new();
    private readonly TestKey key = new();
    private int nextArtifact = 3;
    public PortalFactory() => connection.Open();
    public HttpClient Client(bool cookies = true)
    {
        var client = CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = cookies });
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SignatureDbContext>().Database.EnsureCreated();
        return client;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sati:ExpectedEnvironment"] = "Testing", ["Sati:ExpectedDatabaseName"] = "SatiApiTests",
            ["Signatures:Enabled"] = "true", ["Signatures:PortalBaseUri"] = "https://localhost/"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SignatureOptions>();
            services.AddSingleton(new SignatureOptions { Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests", PortalBaseUri = "https://localhost/" });
            services.RemoveAll<DbContextOptions<SignatureDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SignatureDbContext>>();
            services.AddDbContext<SignatureDbContext>(options => options.UseSqlite(connection));
            services.RemoveAll<ISignatureBlobStore>(); services.AddSingleton<ISignatureBlobStore>(blobs);
            services.RemoveAll<ISigningPinKeyWrapper>(); services.AddSingleton<ISigningPinKeyWrapper>(key);
        });
    }
    public async Task<string> Issue()
    {
        await using var scope = Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS TestSources(Id INTEGER, AgencyId INTEGER, PersonId INTEGER, Kind TEXT, CycleStart TEXT, Origin TEXT, ContentSha256 TEXT, ByteCount INTEGER, BlankFieldsJson TEXT, SupersededByArtifactId INTEGER NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE VIEW IF NOT EXISTS SignatureSourceDocuments AS SELECT * FROM TestSources");
        byte[] pdf;
        using (var document = new PdfSharp.Pdf.PdfDocument()) { document.AddPage(); using var stream = new MemoryStream(); document.Save(stream, false); pdf = stream.ToArray(); }
        var artifact = nextArtifact++;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TestSources VALUES({artifact},1,2,'PrivacyPractices','2026-01-01','GeneratedInSati',{SignatureSecrets.Hash(pdf)},{pdf.LongLength},'[]',NULL)");
        var options = scope.ServiceProvider.GetRequiredService<SignatureOptions>(); var outbox = new SignatureOutboxProtector(key);
        var workflow = new SignatureWorkflow(db, new(options), options, blobs, new(key), outbox, TimeProvider.System);
        var actor = new SignatureActor(1, 7);
        await workflow.FreezeAsync(actor, 2, artifact, new(Guid.NewGuid(), pdf, true));
        var request = await workflow.CreateAsync(actor, new(Guid.NewGuid(), 2, artifact, SignerCapacity.Consumer, null, Pin, Pin, true, true, null), new("Synthetic Person", "synthetic@example.test"));
        return new Uri((await outbox.UnprotectAsync(await db.SignatureOutbox.SingleAsync(x => x.RequestId == request.Id))).Link).Segments.Last();
    }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
    private sealed class MemoryBlobs : ISignatureBlobStore
    {
        private readonly Dictionary<string, byte[]> values = [];
        public Task WriteOnceAsync(string path, byte[] content, CancellationToken cancellationToken = default) { values.Add(path, content.ToArray()); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(values[path].ToArray());
    }
    private sealed class TestKey : ISigningPinKeyWrapper, ISignatureOutboxKeyWrapper
    {
        public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default) => Task.FromResult(new WrappedDataKey(dataKey.ToArray(), "test-only"));
        public Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default) => Task.FromResult(wrappedKey.ToArray());
    }
}
