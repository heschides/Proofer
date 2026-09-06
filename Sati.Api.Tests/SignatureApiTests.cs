using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Signatures;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class SignatureApiTests(SatiApiFactory factory) : IAsyncLifetime
{
    private static readonly byte[] Pdf = TestPdf();
    private static byte[] TestPdf()
    {
        using var document = new PdfSharp.Pdf.PdfDocument(); document.AddPage();
        using var stream = new MemoryStream(); document.Save(stream, false); return stream.ToArray();
    }
    public async Task InitializeAsync()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        factory.Services.GetRequiredService<SignatureOptions>().Enabled = true;
        factory.Services.GetRequiredService<SignatureOptions>().PortalBaseUri = "https://signing.example.test/";
    }
    public Task DisposeAsync() { factory.Services.GetRequiredService<SignatureOptions>().Enabled = false; return Task.CompletedTask; }

    [Fact]
    public async Task DisabledFeatureReportsUnavailableAndRefusesStaffOperations()
    {
        factory.Services.GetRequiredService<SignatureOptions>().Enabled = false;
        using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        Assert.False((await staff.GetFromJsonAsync<SignatureAvailabilityDto>("/api/v1/signatures/availability"))!.Enabled);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/v1/people/101/signature-signers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(101, 1))).StatusCode);
    }

    [Theory]
    [InlineData("case-manager-two")]
    [InlineData("billing-only-one")]
    [InlineData("supervisee-of-demoted-one")]
    public async Task ForeignAndUnauthorizedStaffCannotReadFreezeOrCreate(string username)
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync(username);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/people/{source.PersonId}/signature-signers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/people/{source.PersonId}/signature-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Freeze(staff, source)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id))).StatusCode);
    }

    [Fact]
    public async Task FreezeRequiresExactKnownBytesAndCompletenessReview()
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        Assert.Equal(HttpStatusCode.BadRequest, (await Freeze(staff, source, [1, 2, 3])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Freeze(staff, source, reviewed: false)).StatusCode);
        var response = await Freeze(staff, source); response.EnsureSuccessStatusCode();
        var frozen = (await response.Content.ReadFromJsonAsync<FrozenSignatureDocumentDto>())!;
        Assert.Equal(source.Id, frozen.DocumentArtifactId); Assert.Equal(source.ContentSha256, frozen.ContentSha256);
        Assert.Equal(Pdf.Length, frozen.ByteCount);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task AnOrdinaryConsumerCannotBeUsedForSyntheticSigningEvenByAdmin()
    {
        var source = await Source();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(x => x.Id == source.PersonId);
            person.IsTestData = false; await db.SaveChangesAsync();
        }
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        foreach (var response in new[] { await Freeze(admin, source), await admin.GetAsync($"/api/v1/people/{source.PersonId}/signature-signers"),
            await admin.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id)) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("signature_test_consumer_required", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        }
    }

    [Theory]
    [InlineData("PrivacyPractices", "Draft", "[]")]
    [InlineData("PrivacyPractices", "RecordedAsExternal", "[]")]
    [InlineData("PrivacyPractices", "GeneratedInSati", "[\"Required field\"]")]
    [InlineData("PrivacyPractices", "GeneratedInSati", "null")]
    [InlineData("SafetyPlan", "GeneratedInSati", "[]")]
    [InlineData("ReleaseDhhs", "GeneratedInSati", "[]")]
    [InlineData("MedicalRecordsRequest", "GeneratedInSati", "[]")]
    public async Task NoStaffRoleCanFreezeIncompleteOrPolicyBlockedDocuments(string kind, string origin, string blanks)
    {
        var source = await Source(kind, origin, blanks); using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        Assert.Equal(HttpStatusCode.BadRequest, (await Freeze(admin, source)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApiDbContext>().FrozenSignatureDocuments.AnyAsync(x => x.DocumentArtifactId == source.Id));
    }

    [Fact]
    public async Task CreateDerivesSignerFromCurrentConsumerAndNeverRecordsStaffReceipt()
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var request = Create(source.PersonId, source.Id);
        var response = await staff.PostAsJsonAsync("/api/v1/signature-requests", request); response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        Assert.Equal("Synthetic Signing Consumer", result.SignerName);
        Assert.Equal("synthetic-signing@example.test", result.DeliveryEmail);
        Assert.Equal("Issued", result.State); Assert.False(result.HasSignedPackage);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var repeat = await staff.PostAsJsonAsync("/api/v1/signature-requests", request); repeat.EnsureSuccessStatusCode();
        Assert.Equal(result.Id, (await repeat.Content.ReadFromJsonAsync<SignatureRequestDto>())!.Id);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.False(await db.DocumentAcknowledgments.AnyAsync(x => x.DocumentArtifactId == source.Id));
        Assert.False(await db.SignatureConsents.AnyAsync(x => x.RequestId == result.Id));
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(request.Pin, json); Assert.DoesNotContain("tokenSha256", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pinHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForeignContactCannotBeSelectedAsGuardian()
    {
        var source = await Source(); var other = await Source();
        int contactId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var contact = new ServerPersonContact { PersonId = other.PersonId, Kind = "Guardian", FirstName = "Foreign", LastName = "Contact", Email = "guardian@example.test" };
            db.PersonContacts.Add(contact); await db.SaveChangesAsync(); contactId = contact.Id;
        }
        using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var response = await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id) with
        { SignerCapacity = SignerCapacity.Guardian, SignerContactId = contactId, AuthorityEvidence = "Synthetic appointment reference",
            ExpectedSignerName = "Foreign Contact", ExpectedDeliveryEmail = "guardian@example.test" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StaffDocumentBytesAreReleasedOnlyAfterDurableReadAudit()
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var created = await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id)); created.EnsureSuccessStatusCode();
        var request = (await created.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        var download = await staff.GetAsync($"/api/v1/signature-requests/{request.Id}/original.pdf"); download.EnsureSuccessStatusCode();
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync()); Assert.True(download.Headers.CacheControl?.NoStore);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var evidence = await db.AuditEvents.AsNoTracking().SingleAsync(x => x.Action == "signature.staff-document-released" && x.ResourceId == request.Id.ToString());
        Assert.Contains(source.ContentSha256!, evidence.MetadataJson);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER SignatureTestRejectReleaseAudit BEFORE INSERT ON AuditEvents
            WHEN NEW.Action = 'signature.staff-document-released'
            BEGIN SELECT RAISE(ABORT, 'Synthetic audit failure'); END;
            """);
        try
        {
            var blocked = await staff.GetAsync($"/api/v1/signature-requests/{request.Id}/original.pdf");
            Assert.Equal(HttpStatusCode.InternalServerError, blocked.StatusCode);
            Assert.NotEqual("application/pdf", blocked.Content.Headers.ContentType?.MediaType);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS SignatureTestRejectReleaseAudit"); }
    }

    [Fact]
    public async Task ChangedSignerDetailsMustBeShownAndConfirmedBeforeCreateOrReplace()
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var create = Create(source.PersonId, source.Id);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(x => x.Id == source.PersonId);
        person.Email = "new-synthetic@example.test"; await db.SaveChangesAsync();
        var staleCreate = await staff.PostAsJsonAsync("/api/v1/signature-requests", create);
        Assert.Equal(HttpStatusCode.Conflict, staleCreate.StatusCode);
        Assert.Equal("signature_signer_changed", (await staleCreate.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        var created = await staff.PostAsJsonAsync("/api/v1/signature-requests", create with { ExpectedDeliveryEmail = person.Email }); created.EnsureSuccessStatusCode();
        var request = (await created.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        person.LastName = "Changed Signing Consumer"; await db.SaveChangesAsync();
        var replace = new ReplaceSignatureRequest(Guid.NewGuid(), request.Revision, "84295731", "84295731", true, true,
            "Synthetic recovery", request.SignerName, request.DeliveryEmail);
        var staleReplace = await staff.PostAsJsonAsync($"/api/v1/signature-requests/{request.Id}/replace", replace);
        Assert.Equal(HttpStatusCode.Conflict, staleReplace.StatusCode);
        Assert.Equal("signature_signer_changed", (await staleReplace.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        var replacement = await staff.PostAsJsonAsync($"/api/v1/signature-requests/{request.Id}/replace", replace with { ExpectedSignerName = "Synthetic Changed Signing Consumer" });
        replacement.EnsureSuccessStatusCode();
        var replaced = (await replacement.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        Assert.NotEqual(request.Id, replaced.Id); Assert.Equal("Synthetic Changed Signing Consumer", replaced.SignerName);
        Assert.Equal("Revoked", (await db.SignatureRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id)).State);
    }

    [Theory]
    [InlineData("firstName")]
    [InlineData("email")]
    public async Task UpdatingConsumerSignerDetailsRevokesItsOpenInvitation(string field)
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var created = await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id)); created.EnsureSuccessStatusCode();
        var request = (await created.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        var person = (await staff.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload?userId=12"))!.Single(x => x.Id == source.PersonId);
        var edit = JsonSerializer.SerializeToNode(person, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        edit[field] = field == "email" ? "new-synthetic@example.test" : "Changed";
        edit["expectedRevision"] = person!.Revision;
        var changed = await staff.PutAsJsonAsync($"/api/v1/people/{source.PersonId}", edit); changed.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var retained = await db.SignatureRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal("Revoked", retained.State); Assert.True(retained.AuthenticationVersion > 1);
        Assert.Equal("Synthetic Signing Consumer", retained.SignerName);
        Assert.True(await db.SignatureEvents.AnyAsync(x => x.RequestId == request.Id && x.Kind == "SignerRecordChanged"));
    }

    [Theory]
    [InlineData("agency", "profile")]
    [InlineData("agency", "contact")]
    [InlineData("agency", "archive")]
    [InlineData("billing-owner", "profile")]
    [InlineData("billing-owner", "contact")]
    [InlineData("billing-owner", "archive")]
    [InlineData("other-caseload", "profile")]
    [InlineData("other-caseload", "contact")]
    [InlineData("other-caseload", "archive")]
    public async Task ProfileAndContactMutationsRequireCurrentCaseManagementOwner(string boundary, string operation)
    {
        var source = await Source(); using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var input = new SavePersonContactRequest("Synthetic", "Guardian", "Guardian", null, null, null, "guardian@example.test", false, false);
        var response = await owner.PostAsJsonAsync($"/api/v1/people/{source.PersonId}/contacts", input); response.EnsureSuccessStatusCode();
        var contact = (await response.Content.ReadFromJsonAsync<PersonContactDto>())!;
        var person = (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload?userId=12"))!.Single(x => x.Id == source.PersonId);
        var edit = JsonSerializer.SerializeToNode(person, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        edit["firstName"] = "Changed"; edit["expectedRevision"] = person.Revision;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var stored = await db.People.SingleAsync(x => x.Id == source.PersonId);
            if (boundary == "agency") stored.AgencyId = 2;
            if (boundary == "billing-owner") stored.UserId = 15;
            await db.SaveChangesAsync();
        }
        using var caller = await factory.CreateAuthenticatedClientAsync(boundary == "billing-owner" ? "billing-only-one" : boundary == "other-caseload" ? "admin-one" : "case-manager-one");
        if (operation == "profile") Assert.Equal(HttpStatusCode.NotFound, (await caller.PutAsJsonAsync($"/api/v1/people/{source.PersonId}", edit)).StatusCode);
        if (operation == "contact") Assert.Equal(HttpStatusCode.NotFound, (await caller.PutAsJsonAsync($"/api/v1/people/{source.PersonId}/contacts/{contact.Id}", input with { FirstName = "Changed" })).StatusCode);
        if (operation == "archive") Assert.Equal(HttpStatusCode.NotFound, (await caller.DeleteAsync($"/api/v1/contacts/{contact.Id}")).StatusCode);
        using var verifyScope = factory.Services.CreateScope(); var verify = verifyScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal("Synthetic", (await verify.People.SingleAsync(x => x.Id == source.PersonId)).FirstName);
        Assert.Equal("Synthetic", (await verify.PersonContacts.SingleAsync(x => x.Id == contact.Id)).FirstName);
        Assert.True((await verify.PersonContacts.SingleAsync(x => x.Id == contact.Id)).IsActive);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("kind")]
    [InlineData("archive")]
    [InlineData("phone")]
    public async Task GuardianChangesRevokeOpenRequestsOnlyWhenSigningIdentityChanges(string change)
    {
        var source = await Source(); using var staff = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var contactInput = new SavePersonContactRequest("Synthetic", "Guardian", "Guardian", null, null, null, "guardian@example.test", false, false);
        var contactResponse = await staff.PostAsJsonAsync($"/api/v1/people/{source.PersonId}/contacts", contactInput); contactResponse.EnsureSuccessStatusCode();
        var contact = (await contactResponse.Content.ReadFromJsonAsync<PersonContactDto>())!;
        (await Freeze(staff, source)).EnsureSuccessStatusCode();
        var created = await staff.PostAsJsonAsync("/api/v1/signature-requests", Create(source.PersonId, source.Id) with
        { SignerCapacity = SignerCapacity.Guardian, SignerContactId = contact.Id, AuthorityEvidence = "Synthetic appointment", ExpectedSignerName = "Synthetic Guardian", ExpectedDeliveryEmail = contact.Email });
        created.EnsureSuccessStatusCode(); var request = (await created.Content.ReadFromJsonAsync<SignatureRequestDto>())!;
        Assert.Equal(contact.Id, request.SignerContactId);
        var edited = change == "archive" ? await staff.DeleteAsync($"/api/v1/contacts/{contact.Id}")
            : await staff.PutAsJsonAsync($"/api/v1/people/{source.PersonId}/contacts/{contact.Id}", change switch
            { "name" => contactInput with { FirstName = "Changed" }, "email" => contactInput with { Email = "changed-guardian@example.test" },
                "kind" => contactInput with { Kind = "Personal" }, _ => contactInput with { Phone = "2075550100" } });
        edited.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var retained = await db.SignatureRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal(change == "phone" ? "Issued" : "Revoked", retained.State);
        Assert.Equal("Synthetic Guardian", retained.SignerName); Assert.Equal("guardian@example.test", retained.DeliveryEmail);
        Assert.Equal(change != "phone", await db.SignatureEvents.AnyAsync(x => x.RequestId == request.Id && x.Kind == "SignerRecordChanged"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FrozenDocumentBlocksConsumerDeletionBeforeDependentRowsChange(bool testDataRoute)
    {
        var graph = await factory.CreateTestConsumerGraphAsync();
        var source = await Source(personId: graph.PersonId);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one"); (await Freeze(admin, source)).EnsureSuccessStatusCode();
        if (!testDataRoute)
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var person = await db.People.SingleAsync(x => x.Id == graph.PersonId);
            person.IsTestData = false; person.CreatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        }
        var before = await factory.GetTestConsumerGraphAsync(graph.PersonId);
        var response = testDataRoute
            ? await admin.PostAsJsonAsync($"/api/v1/admin/test-data/consumers/{graph.PersonId}/delete", new DeleteTestConsumerRequest(graph.Revision, TestDataDeletionRules.ConsumerAttestation))
            : await admin.PostAsJsonAsync($"/api/v1/admin/consumers/{graph.PersonId}/delete-in-window", new DeleteConsumerInWindowRequest(graph.Revision, ConsumerDeletionRules.ConsumerAttestation, "Synthetic retained-signature regression"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("consumer_has_signature_history", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        Assert.Equal(before, await factory.GetTestConsumerGraphAsync(graph.PersonId));
    }

    private Task<HttpResponseMessage> Freeze(HttpClient staff, ServerDocumentArtifact source, byte[]? bytes = null, bool reviewed = true) =>
        staff.PostAsJsonAsync($"/api/v1/people/{source.PersonId}/documents/{source.Id}/freeze", new FreezeSignatureDocumentRequest(Guid.NewGuid(), bytes ?? Pdf, reviewed));
    private static CreateSignatureRequest Create(int personId, int artifactId) => new(Guid.NewGuid(), personId, artifactId,
        SignerCapacity.Consumer, null, "73925814", "73925814", true, true, null,
        ExpectedSignerName: "Synthetic Signing Consumer", ExpectedDeliveryEmail: "synthetic-signing@example.test");
    private async Task<ServerDocumentArtifact> Source(string kind = "PrivacyPractices", string origin = "GeneratedInSati", string blanks = "[]", int? personId = null)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        if (personId is null)
        {
            var person = new ServerPerson { AgencyId = 1, UserId = 12, FirstName = "Synthetic", LastName = "Signing Consumer", Email = "synthetic-signing@example.test",
                BirthDate = new(1990, 1, 1), Bio = "Synthetic biography", DayProgramCount = 1, EffectiveDate = DateTime.Today, CreatedAtUtc = DateTime.UtcNow, IsTestData = true };
            db.People.Add(person); await db.SaveChangesAsync(); personId = person.Id;
        }
        var artifact = new ServerDocumentArtifact { AgencyId = 1, PersonId = personId.Value, Kind = kind, Origin = origin,
            CycleStart = DateTime.Today, GeneratedAtUtc = DateTime.UtcNow, GeneratedByUserId = 12,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(Pdf)), ByteCount = Pdf.Length, SuggestedFileName = "synthetic-notice.pdf", BlankFieldsJson = blanks };
        db.DocumentArtifacts.Add(artifact); await db.SaveChangesAsync(); return artifact;
    }
}
