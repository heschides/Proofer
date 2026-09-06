using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The nine supervision gates, and the one inside <c>TenantAccess.CanAccessUserAsync</c>.
///
/// <para>
/// Added after the 2026-08-30 audit found them completely uncovered: all 278 API tests passed
/// with every <c>!actor.HasSupervisorPermissions</c> guard replaced by <c>false</c>. The reason
/// the obvious test does not work is that each query beneath these gates is independently scoped
/// by <c>SupervisorId</c>, so an ordinary case manager gets an empty list whether the gate is
/// present or not. A denial test built on such an actor passes for the wrong reason.
/// </para>
///
/// <para>
/// So every test here acts as <c>demoted-supervisor-one</c>: a user carrying only case management
/// who is still named as user 19's supervisor. That is not a contrived shape — it is what a real
/// database holds the moment somebody's supervision permission is revoked while their supervisees
/// still point at them. Remove any of these gates and this actor is handed real rows, a real
/// consumer, and a real note. That is what makes each assertion below load-bearing.
/// </para>
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class SupervisionGateTests
{
    private const int DemotedSupervisorId = 18;
    private const int SuperviseeId = 19;
    private const int SuperviseeNoteId = 507;

    private readonly SatiApiFactory _factory;

    public SupervisionGateTests(SatiApiFactory factory) => _factory = factory;

    private Task<HttpClient> DemotedSupervisorAsync() =>
        _factory.CreateAuthenticatedClientAsync("demoted-supervisor-one");

    // ---- The supervisory review surface ----

    [Fact]
    public async Task ADemotedSupervisorCannotListSupervisees()
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.GetAsync("/api/v1/supervisor/supervisees");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/supervisor/notes/page")]
    [InlineData("/api/v1/supervisor/notes/filters")]
    [InlineData("/api/v1/supervisor/notes?compliant=true&allSupervisees=true")]
    [InlineData("/api/v1/supervisor/notes?compliant=false&allSupervisees=true")]
    public async Task ADemotedSupervisorCannotReadTheReviewQueue(string path)
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Approval is the transition the whole workflow exists to protect: it is the only way a note
    // reaches Approved, and an approved note is what billing draws from.
    [Theory]
    [InlineData("approve")]
    [InlineData("approve-override")]
    [InlineData("return")]
    public async Task ADemotedSupervisorCannotActOnASuperviseesNote(string action)
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{SuperviseeNoteId}/{action}",
            new SupervisorNoteActionRequest("Reviewed and acceptable.", 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The note must still be Logged afterwards. A 403 that had nevertheless written the
    // transition would be the worst of both outcomes, and the status code alone would hide it.
    [Fact]
    public async Task ARefusedApprovalLeavesTheNoteUntouched()
    {
        using var client = await DemotedSupervisorAsync();

        await client.PostAsJsonAsync(
            $"/api/v1/supervisor/notes/{SuperviseeNoteId}/approve",
            new SupervisorNoteActionRequest(null, 1));

        var status = await _factory.GetNoteStatusAsync(SuperviseeNoteId);
        Assert.Equal<int?>(NoteWorkflow.Logged, status);
    }

    // ---- User management ----

    [Fact]
    public async Task ADemotedSupervisorCannotCreateUsers()
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserRequest(
                "minted-by-demoted", "Minted By Demoted", UserPermissions.CaseManagement,
                DemotedSupervisorId, 1, null, null, "correct-horse-battery-staple"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ADemotedSupervisorCannotEditTheUserStillAssignedToThem()
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/users/{SuperviseeId}",
            new SaveUserRequest(
                "supervisee-of-demoted-one", "Renamed By Demoted",
                UserPermissions.CaseManagement, DemotedSupervisorId, 1, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ADemotedSupervisorCannotResetTheirFormerSuperviseesPassword()
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/users/{SuperviseeId}/password",
            new ResetPasswordRequest("correct-horse-battery-staple"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- The caseload gate inside TenantAccess ----

    [Fact]
    public async Task ADemotedSupervisorCannotReadTheirFormerSuperviseesCaseload()
    {
        using var client = await DemotedSupervisorAsync();

        var response = await client.GetAsync($"/api/v1/caseload?userId={SuperviseeId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Positive control ----

    /// <summary>
    /// Without this, a defect that refused everything would make every test above pass. A real
    /// supervisor must still reach exactly the same surface for their own assigned case manager.
    /// </summary>
    [Fact]
    public async Task ARealSupervisorStillReachesTheirOwnSupervisees()
    {
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        Assert.Equal(
            HttpStatusCode.OK,
            (await supervisor.GetAsync("/api/v1/supervisor/supervisees")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await supervisor.GetAsync(
                "/api/v1/supervisor/notes?compliant=true&allSupervisees=true")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await supervisor.GetAsync("/api/v1/caseload?userId=12")).StatusCode);
    }

    /// <summary>
    /// And the supervisory scope stays a scope. A real supervisor may not reach a case manager
    /// assigned to somebody else, which is the check that would otherwise be satisfied by
    /// granting supervision to everyone.
    /// </summary>
    [Fact]
    public async Task ARealSupervisorCannotReachACaseManagerAssignedElsewhere()
    {
        using var supervisor = await _factory.CreateAuthenticatedClientAsync("supervisor-one");

        var response = await supervisor.GetAsync($"/api/v1/caseload?userId={SuperviseeId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
