using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// <c>PUT /api/v1/people/{personId}/owner</c> — moving a consumer between caseloads.
///
/// <para>
/// The route exists for caseload distribution after a Credible import: a supervisor onboards a
/// team's caseloads onto their own account and then hands them out. That makes it the first route
/// in Sati that deliberately changes <c>Person.UserId</c>, which decides who may read a clinical
/// record at all, so the denial tests below matter more than the happy path.
/// </para>
///
/// <para>
/// Each denial test is built so it would <b>pass real rows</b> if its gate were removed, in the
/// manner of <c>SupervisionGateTests</c>. A denial test whose actor would have got an empty result
/// anyway proves nothing. Verified by reverting each guard in turn and confirming the matching
/// test fails.
/// </para>
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class CaseloadTransferApiTests(SatiApiFactory factory)
{
    private const int CaseManagerOne = 12;
    private const int SupervisorOne = 13;
    private const int DemotedSupervisor = 18;
    private const int SuperviseeOfDemoted = 19;
    private const int CaseManagerTwo = 22;

    // Owned by user 19, who still reports to the demoted supervisor 18.
    private const int SuperviseeConsumerId = 103;

    // ---- The move that the import flow depends on ----

    [Fact]
    public async Task ASupervisorMovesAConsumerTheyHoldToOneOfTheirSupervisees()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);
        var auditBefore = await factory.GetAuditEventsAsync("person.reassigned");

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(CaseManagerOne, consumer.Revision));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ownership = await response.Content.ReadFromJsonAsync<CaseloadOwnershipDto>();
        Assert.NotNull(ownership);
        Assert.Equal(CaseManagerOne, ownership.UserId);

        // The revision has to move, or a case manager holding the old token could still
        // write over a consumer that is no longer theirs.
        Assert.True(ownership.Revision > consumer.Revision);

        var auditAfter = await factory.GetAuditEventsAsync("person.reassigned");
        Assert.Equal(auditBefore.Count + 1, auditAfter.Count);
    }

    [Fact]
    public async Task TheMoveLandsOnTheReceivingCaseloadAndLeavesTheSenders()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var receiver = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var consumer = await CreateConsumerAsync(supervisor);

        await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(CaseManagerOne, consumer.Revision));

        var receiverCaseload = await receiver.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var senderCaseload = await supervisor.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");

        Assert.Contains(receiverCaseload!, person => person.Id == consumer.Id);
        Assert.DoesNotContain(senderCaseload!, person => person.Id == consumer.Id);
    }

    [Fact]
    public async Task TheMoveIsRecordedInTheConsumersOwnHistory()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var consumer = await CreateConsumerAsync(supervisor);

        await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(CaseManagerOne, consumer.Revision));

        var history = await admin.GetFromJsonAsync<List<PersonVersionDto>>(
            $"/api/v1/people/{consumer.Id}/history");

        Assert.Contains(history!, version => version.ChangeKind == "Reassigned");
    }

    // ---- Denials ----

    // Pins the supervision-permission gate only. Note that it does NOT pin the supervisor-link
    // reach rule below: a plain case manager has no supervisees, so it would be refused by
    // either guard, and mutation testing confirmed it still passes with the reach rule removed.
    // ASupervisorCannotMoveAConsumerToACaseManagerTheyDoNotSupervise is what covers that.
    [Fact]
    public async Task ACaseManagerCannotMoveTheirOwnConsumerToAPeer()
    {
        using var caseManager = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var consumer = await CreateConsumerAsync(caseManager);

        var response = await caseManager.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(SuperviseeOfDemoted, consumer.Revision));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillOwnedByAsync(consumer.Id, CaseManagerOne);
    }

    // The demoted-supervisor shape from SupervisionGateTests: user 18 is still named as user
    // 19's supervisor in the database but no longer carries Supervision. Person 103 is really
    // user 19's, so removing the permission check hands this actor a real consumer to move.
    [Fact]
    public async Task ADemotedSupervisorCannotMoveTheirFormerSuperviseesConsumer()
    {
        using var demoted = await factory.CreateAuthenticatedClientAsync("demoted-supervisor-one");
        var consumer = await ReadConsumerAsAdminAsync(SuperviseeConsumerId);

        var response = await demoted.PutAsJsonAsync(
            $"/api/v1/people/{SuperviseeConsumerId}/owner",
            new TransferCaseloadRequest(DemotedSupervisor, consumer.Revision));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillOwnedByAsync(SuperviseeConsumerId, SuperviseeOfDemoted);
    }

    // Supervision is not agency-wide by default, and this is the only test that says so.
    // Supervisor one carries Supervision but not AgencyWideSupervision and supervises only
    // user 12; user 19 is a real case manager in the same agency who reports to somebody else.
    // Remove the supervisor-link half of the reach rule and this call succeeds — a supervisor
    // would be able to push consumers onto any caseload in the agency.
    [Fact]
    public async Task ASupervisorCannotMoveAConsumerToACaseManagerTheyDoNotSupervise()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(SuperviseeOfDemoted, consumer.Revision));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillOwnedByAsync(consumer.Id, SupervisorOne);
    }

    // The other side of the same rule: agency-wide reach is what lets a Director distribute
    // across the whole agency, and without this nothing would notice if that branch stopped
    // working and every Director import became undistributable.
    [Fact]
    public async Task ADirectorWithAgencyWideSupervisionCanMoveToAnyCaseloadInTheAgency()
    {
        using var director = await factory.CreateAuthenticatedClientAsync("director-one");
        var consumer = await CreateConsumerAsync(director);

        var response = await director.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(SuperviseeOfDemoted, consumer.Revision));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertStillOwnedByAsync(consumer.Id, SuperviseeOfDemoted);
    }

    // Tenant isolation on the target rather than the consumer: the supervisor legitimately
    // holds this consumer, and user 22 is a real case manager — in the other agency. Getting
    // this wrong moves a live clinical record across a tenant boundary.
    [Fact]
    public async Task ASupervisorCannotMoveAConsumerToACaseManagerInAnotherAgency()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(CaseManagerTwo, consumer.Revision));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillOwnedByAsync(consumer.Id, SupervisorOne);
    }

    // Tenant isolation on the consumer. Supervisor two is a genuine supervisor; person 101
    // simply is not theirs to move.
    [Fact]
    public async Task ASupervisorCannotMoveAConsumerBelongingToAnotherAgency()
    {
        using var otherAgency = await factory.CreateAuthenticatedClientAsync("supervisor-two");
        var consumer = await ReadConsumerAsAdminAsync(101);

        var response = await otherAgency.PutAsJsonAsync(
            "/api/v1/people/101/owner",
            new TransferCaseloadRequest(CaseManagerTwo, consumer.Revision));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStillOwnedByAsync(101, CaseManagerOne);
    }

    // A supervisor may only hand a consumer to someone who can actually work the caseload.
    // billing-only-one is in the right agency but carries no case management.
    [Fact]
    public async Task AConsumerCannotBeMovedToSomeoneWhoCannotHoldACaseload()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(15, consumer.Revision));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillOwnedByAsync(consumer.Id, SupervisorOne);
    }

    // ---- Concurrency ----

    // The pair this route has to lose to: a supervisor distributing an imported batch while
    // the consumer's profile is open somewhere else.
    [Fact]
    public async Task AStaleRevisionIsRefusedRatherThanOverwriting()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(CaseManagerOne, consumer.Revision + 7));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("stale_person", error!.Code);
        await AssertStillOwnedByAsync(consumer.Id, SupervisorOne);
    }

    // Authorization is decided before the revision token, so a caller who may not move this
    // consumer is not told whether their token was current.
    [Fact]
    public async Task AnUnauthorizedMoveIsRefusedBeforeTheRevisionIsConsidered()
    {
        using var otherAgency = await factory.CreateAuthenticatedClientAsync("supervisor-two");

        var response = await otherAgency.PutAsJsonAsync(
            "/api/v1/people/101/owner",
            new TransferCaseloadRequest(CaseManagerTwo, 9999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MovingAConsumerToTheCaseloadTheyAlreadyOccupyIsRejected()
    {
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        var consumer = await CreateConsumerAsync(supervisor);

        var response = await supervisor.PutAsJsonAsync(
            $"/api/v1/people/{consumer.Id}/owner",
            new TransferCaseloadRequest(SupervisorOne, consumer.Revision));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Helpers ----

    private static async Task<PersonDto> CreateConsumerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with { LastName = Guid.NewGuid().ToString("N")[..10] });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PersonDto>())!;
    }

    // Read through the Admin list rather than a caseload: these assertions have to see a
    // consumer regardless of which caseload it currently sits on, including the ones the
    // test is asserting it did NOT move to.
    private async Task<AdminPersonListItemDto> ReadConsumerAsAdminAsync(int personId)
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var people = await admin.GetFromJsonAsync<List<AdminPersonListItemDto>>("/api/v1/admin/people");
        return people!.Single(person => person.PersonId == personId);
    }

    private async Task AssertStillOwnedByAsync(int personId, int expectedUserId)
    {
        var person = await ReadConsumerAsAdminAsync(personId);
        Assert.Equal(expectedUserId, person.AssignedUserId);
    }

    private static SavePersonRequest ValidRequest() => new(
        "Transfer",
        "Subject",
        new DateTime(1990, 4, 3),
        "Unknown",
        null,
        "A consumer created for caseload transfer tests.",
        "None",
        null,
        null,
        null,
        null,
        false,
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        false,
        false,
        false,
        false,
        false,
        1,
        false,
        false,
        false,
        [],
        0,
        true,
        false,
        null,
        null,
        false,
        false,
        "transfer@example.test");
}
