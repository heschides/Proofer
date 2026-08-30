using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Exercises the Admin-only schema drift report. The route exists so the
/// reconciliation that has to classify each discrepancy can see it without a
/// temporary SQL firewall rule: the API already holds database access, so the
/// report is read from inside the network boundary rather than from a
/// workstation.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class SchemaDriftReportTests
{
    private readonly SatiApiFactory _factory;

    public SchemaDriftReportTests(SatiApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SchemaDriftReportRejectsAnonymousRequest()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/schema-drift");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The report names every table and column the deployment has. That is
    /// operational detail about the database, not caseload data, but it is not
    /// something a case manager has any reason to enumerate.
    /// </summary>
    [Fact]
    public async Task SchemaDriftReportIsAdminOnly()
    {
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-one");

        var response = await caseManager.GetAsync("/api/v1/admin/schema-drift");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SchemaDriftReportIsCleanAgainstTheTestDatabase()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var report = await admin.GetFromJsonAsync<SchemaDriftReport>("/api/v1/admin/schema-drift");

        Assert.NotNull(report);
        // The test database is created from this model, so nothing the model needs
        // can be absent. A failure here means the reader and the model disagree
        // about how a column is named or nullable, which would make every real
        // report noise.
        Assert.Empty(report.Blocking);
        Assert.Empty(report.Differences);
    }

    /// <summary>
    /// ApiDbContext owns no migration chain — all of them belong to SatiContext in
    /// the desktop project — so the route reports applied ids as data and returns
    /// no history verdict. Were it to pass its own empty chain as authoritative,
    /// every applied migration on the live database would be reported as
    /// unrecognized.
    /// </summary>
    [Fact]
    public async Task SchemaDriftReportReturnsNoHistoryVerdictBecauseTheApiOwnsNoChain()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var report = await admin.GetFromJsonAsync<SchemaDriftReport>("/api/v1/admin/schema-drift");

        Assert.NotNull(report);
        Assert.Empty(report.HistoryDifferences);
    }

    /// <summary>
    /// The API model maps only the tables the API serves. If the route ever
    /// declared that model authoritative, every desktop-only table would be
    /// reported as unexpected drift and the report would be unusable.
    /// </summary>
    [Fact]
    public async Task SchemaDriftReportNamesBothSourcesItCompared()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var report = await admin.GetFromJsonAsync<SchemaDriftReport>("/api/v1/admin/schema-drift");

        Assert.NotNull(report);
        Assert.Equal("The API model", report.ExpectedSource);
        Assert.Equal("the database", report.ActualSource);
    }
}
