using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The server side of AT request publication.
///
/// Two properties matter here and neither can be demonstrated from the desktop
/// client alone. First, the SIGNER IS DERIVED FROM THE TOKEN: there is no field
/// on SaveAtRequestRequest a caller could use to claim someone else signed, and
/// the recorded name must be the authenticated user's regardless of what the
/// payload says about case managers. Second, THE LOCK IS SERVER-SIDE: once a
/// request is published the API refuses further edits and deletes, so a client
/// that ignores its own disabled buttons still cannot rewrite a signed document.
///
/// Each test creates and cleans up its own request rather than seeding shared
/// fixtures, so nothing here perturbs the tenancy tests that assert exact counts.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class AtRequestPublicationApiTests
{
    private readonly SatiApiFactory _factory;

    public AtRequestPublicationApiTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PublishingRecordsWhoActuallyPublishedNotWhoseRequestItIs()
    {
        // The discriminating case. A request's CaseManagerName is the snapshot of
        // the client's owner, frozen on the form; the ATTESTATION is whoever
        // performed the act. They are the same person in the ordinary flow, which
        // is why this test uses a supervisor publishing a case manager's request —
        // an implementation that stamped the form's case-manager name, or any
        // other value carried on the record, passes every same-person test and
        // fails this one.
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");
        var created = await CreateAsync(caseManager);

        try
        {
            var response = await supervisor.PostAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}/publish", new PublishAtRequestRequest(created.Revision));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var published = await response.Content.ReadFromJsonAsync<AtRequestDto>();

            Assert.Equal("supervisor-one", published!.SignedByName);
            Assert.NotEqual(published.CaseManagerName, published.SignedByName);
            Assert.Equal("Supervisor", published.SignedByRole);
            Assert.NotNull(published.SignedAtUtc);
            Assert.NotNull(published.SignedByUserId);
            Assert.Equal(AtRequestPublication.AttestationStatement, published.AttestationStatement);
            Assert.Equal("Review", published.Status);
        }
        finally
        {
            await CleanUpAsync(caseManager, created.Id);
        }
    }

    [Fact]
    public async Task APublishedRequestCannotBeEdited()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            var published = await PublishAsync(client, created);

            var response = await client.PutAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}",
                SaveRequest(published, vendorName: "Rewritten After Signing", expectedRevision: published.Revision));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.Equal("published_at_request", error!.Code);

            var stored = await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{created.Id}");
            Assert.NotEqual("Rewritten After Signing", stored!.VendorName);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task APublishedRequestCannotBeDeleted()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            var published = await PublishAsync(client, created);

            var response = await client.DeleteAsync(
                $"/api/v1/at-requests/{created.Id}?expectedRevision={published.Revision}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.NotNull(await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{created.Id}"));
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task ARequestCannotBePublishedTwice()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            var published = await PublishAsync(client, created);

            var response = await client.PostAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}/publish", new PublishAtRequestRequest(published.Revision));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.Equal("published_at_request", error!.Code);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AnIncompleteRequestIsRefused()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client, vendorName: null, includeItem: false);

        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}/publish", new PublishAtRequestRequest(created.Revision));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var stored = await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{created.Id}");
            Assert.Null(stored!.SignedByName);
            Assert.Equal("Development", stored.Status);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AStalePublishIsRefused()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            // Someone else edits the request between the client loading it and
            // pressing Publish. The attestation must not attach to a version the
            // signer never saw.
            var edited = await client.PutAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}",
                SaveRequest(created, vendorName: "Changed Underneath", expectedRevision: created.Revision));
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

            var response = await client.PostAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}/publish", new PublishAtRequestRequest(created.Revision));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            Assert.Equal("stale_at_request", error!.Code);

            var stored = await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{created.Id}");
            Assert.Null(stored!.SignedByName);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AnotherAgencysRequestCannotBePublishedOrReopened()
    {
        // Request 1001 belongs to Person Two, whose case manager is in the other
        // agency. Publication must be gated by the same tenancy check as every
        // other route that touches it.
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var publish = await client.PostAsJsonAsync(
            "/api/v1/at-requests/1001/publish", new PublishAtRequestRequest(1));
        var reopen = await client.PostAsJsonAsync(
            "/api/v1/at-requests/1001/reopen", new ReopenAtRequestRequest(1));

        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reopen.StatusCode);
    }

    [Fact]
    public async Task ReopeningClearsTheAttestationAndRestoresEditing()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            var published = await PublishAsync(client, created);

            var response = await client.PostAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}/reopen", new ReopenAtRequestRequest(published.Revision));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var reopened = await response.Content.ReadFromJsonAsync<AtRequestDto>();
            Assert.Null(reopened!.SignedByName);
            Assert.Null(reopened.SignedAtUtc);
            Assert.Null(reopened.AttestationStatement);
            Assert.Equal("Development", reopened.Status);

            var edit = await client.PutAsJsonAsync(
                $"/api/v1/at-requests/{created.Id}",
                SaveRequest(reopened, vendorName: "Corrected Vendor", expectedRevision: reopened.Revision));
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AScreenshotRoundTripsThroughTheApi()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client, screenshot: SmallPng);

        try
        {
            var stored = await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{created.Id}");
            var item = Assert.Single(stored!.Items);

            Assert.Equal(Convert.ToBase64String(SmallPng), item.ScreenshotBase64);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task AnOversizedScreenshotIsRefusedByTheServer()
    {
        // The desktop caps this at the paste boundary. That limit is a courtesy
        // to the user and constrains nothing about what arrives in a body, so the
        // server applies the same shared rule to whatever it is handed.
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var oversized = new byte[AtRequestScreenshot.MaxBytes + 1];
        PngSignature.CopyTo(oversized, 0);

        var response = await client.PostAsJsonAsync("/api/v1/at-requests", SaveNew(oversized));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AScreenshotThatIsNotAPngIsRefusedByTheServer()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        // JPEG magic bytes — the right size, the wrong format.
        var response = await client.PostAsJsonAsync(
            "/api/v1/at-requests", SaveNew([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedBase64IsRefusedRatherThanThrowing()
    {
        // Convert.FromBase64String throws on bad input. A client mistake must
        // come back as a 400, not a 500.
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var request = SaveNew(null) with
        {
            Items = [new SaveAtRequestItemRequest(0, "Pie cutter", 25.99m, 3, null, "not-valid-base64!!!")]
        };

        var response = await client.PostAsJsonAsync("/api/v1/at-requests", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnotherAgencysClientsRequestListIsNotReadable()
    {
        // Person 201 belongs to the other agency. The per-client list is gated on
        // the CLIENT's owning user, like every other route that reaches a person.
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await client.GetAsync("/api/v1/people/201/at-requests");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheClientsListShowsTheirRequestsWithAttestationState()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(client);

        try
        {
            var draftRows = await client.GetFromJsonAsync<List<AtRequestListItemDto>>(
                "/api/v1/people/101/at-requests");
            var draft = Assert.Single(draftRows!, row => row.Id == created.Id);
            Assert.Null(draft.SignedByName);

            await PublishAsync(client, created);

            var publishedRows = await client.GetFromJsonAsync<List<AtRequestListItemDto>>(
                "/api/v1/people/101/at-requests");
            var published = Assert.Single(publishedRows!, row => row.Id == created.Id);

            Assert.Equal("case-manager-one", published.SignedByName);
            Assert.NotNull(published.SignedAtUtc);
            Assert.Equal("Review", published.Status);
        }
        finally
        {
            await CleanUpAsync(client, created.Id);
        }
    }

    [Fact]
    public async Task PublishingFreezesTheRateSoALaterSettingsChangeCannotRestateTheTotal()
    {
        // The regeneration requirement, at the API boundary. A published request's
        // reported total must not move when the agency's passthrough rate does.
        using var client = await _factory.CreateAuthenticatedClientAsync("admin-one");
        var settings = await client.GetFromJsonAsync<SettingsDto>("/api/v1/settings");

        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-one");
        var created = await CreateAsync(caseManager);

        try
        {
            var published = await PublishAsync(caseManager, created);
            Assert.NotNull(published.PassthroughRate);

            var filedRows = await caseManager.GetFromJsonAsync<List<AtRequestListItemDto>>(
                "/api/v1/people/101/at-requests");
            var filedTotal = Assert.Single(filedRows!, x => x.Id == created.Id).TotalCost;

            // The agency renegotiates.
            var changed = settings! with { PassthroughRate = settings.PassthroughRate + 0.10m };
            var settingsResponse = await client.PutAsJsonAsync("/api/v1/settings", changed);
            settingsResponse.EnsureSuccessStatusCode();

            var afterChange = await caseManager.GetFromJsonAsync<AtRequestDto>(
                $"/api/v1/at-requests/{created.Id}");
            var rows = await caseManager.GetFromJsonAsync<List<AtRequestListItemDto>>(
                "/api/v1/people/101/at-requests");
            var row = Assert.Single(rows!, x => x.Id == created.Id);

            Assert.Equal(published.PassthroughRate, afterChange!.PassthroughRate);
            Assert.Equal(filedTotal, row.TotalCost);
        }
        finally
        {
            // Put the agency's rate back so nothing else in the collection sees a
            // changed setting.
            await client.PutAsJsonAsync("/api/v1/settings",
                (await client.GetFromJsonAsync<SettingsDto>("/api/v1/settings"))!
                    with { PassthroughRate = settings!.PassthroughRate });
            await CleanUpAsync(caseManager, created.Id);
        }
    }

    // ---- Helpers ----

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] SmallPng = [.. PngSignature, .. new byte[32]];

    private static SaveAtRequestRequest SaveNew(byte[]? screenshot) => new(
        PersonId: 101, ClientName: "Person One", ClientEvergreenId: null,
        CaseManagerName: "case-manager-one", CaseManagerEmail: null, CaseManagerPhone: null,
        CaseManagerAgency: null, VendorName: "Maine AT Solutions",
        VendorBillingLocation: "1992205249-007", VendorProgramContact: null, VendorBillingContact: null,
        SalesTax: 0m, SalesTaxOverridden: false, SubmittedDate: null, DecisionDate: null,
        Status: "Development",
        Items: [new SaveAtRequestItemRequest(0, "Pie cutter", 25.99m, 3, null,
            screenshot is null ? null : Convert.ToBase64String(screenshot))],
        ExpectedRevision: 0);

    private static async Task<AtRequestDto> CreateAsync(
        HttpClient client,
        string? vendorName = "Maine AT Solutions",
        string caseManagerNameInPayload = "case-manager-one",
        bool includeItem = true,
        byte[]? screenshot = null)
    {
        var request = new SaveAtRequestRequest(
            PersonId: 101,
            ClientName: "Person One",
            ClientEvergreenId: null,
            CaseManagerName: caseManagerNameInPayload,
            CaseManagerEmail: null,
            CaseManagerPhone: null,
            CaseManagerAgency: null,
            VendorName: vendorName,
            VendorBillingLocation: vendorName is null ? null : "1992205249-007",
            VendorProgramContact: null,
            VendorBillingContact: null,
            SalesTax: 0m,
            SalesTaxOverridden: false,
            SubmittedDate: null,
            DecisionDate: null,
            Status: "Development",
            Items: includeItem
                ? [new SaveAtRequestItemRequest(0, "Pie cutter", 25.99m, 3, null,
                    screenshot is null ? null : Convert.ToBase64String(screenshot))]
                : [],
            ExpectedRevision: 0);

        var response = await client.PostAsJsonAsync("/api/v1/at-requests", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AtRequestDto>())!;
    }

    private static async Task<AtRequestDto> PublishAsync(HttpClient client, AtRequestDto request)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/at-requests/{request.Id}/publish", new PublishAtRequestRequest(request.Revision));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AtRequestDto>())!;
    }

    private static SaveAtRequestRequest SaveRequest(AtRequestDto source, string vendorName, int expectedRevision) =>
        new(source.PersonId, source.ClientName, source.ClientEvergreenId, source.CaseManagerName,
            source.CaseManagerEmail, source.CaseManagerPhone, source.CaseManagerAgency,
            vendorName, source.VendorBillingLocation, source.VendorProgramContact, source.VendorBillingContact,
            source.SalesTax, source.SalesTaxOverridden, source.SubmittedDate, source.DecisionDate, source.Status,
            [.. source.Items.Select(item => new SaveAtRequestItemRequest(0, item.Name, item.ItemCost, item.Quantity, item.Url))],
            expectedRevision);

    // Removes the request the test created. A published request has to be
    // reopened first — which is the lock working, so the cleanup path doubles as
    // a check that the escape hatch exists.
    private static async Task CleanUpAsync(HttpClient client, int id)
    {
        var current = await client.GetFromJsonAsync<AtRequestDto>($"/api/v1/at-requests/{id}");
        if (current is null)
            return;

        if (!string.IsNullOrEmpty(current.SignedByName))
        {
            var reopened = await client.PostAsJsonAsync(
                $"/api/v1/at-requests/{id}/reopen", new ReopenAtRequestRequest(current.Revision));
            current = (await reopened.Content.ReadFromJsonAsync<AtRequestDto>())!;
        }

        await client.DeleteAsync($"/api/v1/at-requests/{id}?expectedRevision={current.Revision}");
    }
}
