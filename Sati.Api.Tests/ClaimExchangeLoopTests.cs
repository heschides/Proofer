using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The whole loop over HTTP: submit a billing period to the mock clearinghouse, and find
/// the acknowledgements, claim outcomes, and deposit afterwards in the read models the
/// billing screens already use.
///
/// Before this existed, Sati could generate an 837P and record that it had done so, and
/// nothing else. Every row behind the submission home, the denial worklist, and the
/// deposit reconciliation screen came from a seed.
/// </summary>
[Collection(SatiApiCollection.Name)]
public sealed class ClaimExchangeLoopTests
{
    // Agency 2's submitted period, which has a claim line carrying the immutable snapshot
    // the 837P is generated from.
    private const int SubmittedPeriodId = 1202;

    private readonly SatiApiFactory _factory;

    public ClaimExchangeLoopTests(SatiApiFactory factory) => _factory = factory;

    private static async Task<MockClearinghouseResultDto> RunAsync(
        HttpClient client, MockClearinghouseScenario scenario)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/mock-clearinghouse",
            new MockClearinghouseRequest(scenario));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MockClearinghouseResultDto>();
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public async Task TheMockClearinghouseIsAdminOnly()
    {
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-two");

        var response = await caseManager.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/mock-clearinghouse",
            new MockClearinghouseRequest(MockClearinghouseScenario.Accepted));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IngestionIsAdminOnly()
    {
        using var caseManager = await _factory.CreateAuthenticatedClientAsync("case-manager-two");

        var response = await caseManager.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/responses",
            new ClaimResponseIngestRequest("ISA*00*~"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A period belonging to another agency must not be reachable, or a crafted response
    /// could be attached to another tenant's billing history.
    /// </summary>
    [Fact]
    public async Task AnotherAgencysPeriodIsNotFound()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-one");

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/mock-clearinghouse",
            new MockClearinghouseRequest(MockClearinghouseScenario.Accepted));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnUnrecognisedDocumentIsRefusedRatherThanRecorded()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/billing/periods/{SubmittedPeriodId}/responses",
            new ClaimResponseIngestRequest(
                "ISA*00*          *00*          *ZZ*A              *ZZ*B              *260830*1200*^*00501*000000123*0*T*:~\nST*270*0001~\nSE*2*0001~\n"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The happy path, end to end: syntax accepted, claims accepted, paid, and every row
    /// visible afterwards through the endpoints the billing screens read.
    /// </summary>
    [Fact]
    public async Task AnAcceptedSubmissionBecomesEventsOutcomesAndADeposit()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        var result = await RunAsync(admin, MockClearinghouseScenario.Accepted);

        Assert.Equal(
            [
                nameof(BillingSubmissionStage.FunctionalAccepted),
                nameof(BillingSubmissionStage.ClaimAccepted),
                nameof(BillingSubmissionStage.Paid)
            ],
            result.StagesRecorded);
        Assert.True(result.DepositRecorded);
        Assert.True(result.ClaimOutcomesRecorded > 0);

        var submissions = await admin.GetFromJsonAsync<List<BillingSubmissionHistoryDto>>(
            "/api/v1/billing/submissions");
        Assert.NotNull(submissions);
        Assert.Contains(submissions, row =>
            row.BillingPeriodId == SubmittedPeriodId &&
            row.Stage == nameof(BillingSubmissionStage.Paid));

        var remittances = await admin.GetFromJsonAsync<List<RemittanceClaimOutcomeDto>>(
            "/api/v1/billing/remittances");
        Assert.NotNull(remittances);
        Assert.Contains(remittances, row =>
            row.BillingPeriodId == SubmittedPeriodId &&
            row.Status == nameof(RemittanceClaimStatus.Paid));

        var deposits = await admin.GetFromJsonAsync<List<RemittanceDepositDto>>(
            "/api/v1/billing/remittance-deposits");
        Assert.NotNull(deposits);
        Assert.Contains(deposits, row => row.PayerName == "MOCK PAYER");
    }

    /// <summary>
    /// Everything the mock produces declares itself a test interchange, and ingestion
    /// takes that from the document rather than from configuration. If this ever recorded
    /// synthetic rows as real, the billing screens would present fabricated payments as
    /// genuine.
    /// </summary>
    [Fact]
    public async Task EverythingIngestedFromTheMockIsMarkedSynthetic()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        await RunAsync(admin, MockClearinghouseScenario.Accepted);

        var remittances = await admin.GetFromJsonAsync<List<RemittanceClaimOutcomeDto>>(
            "/api/v1/billing/remittances");
        Assert.NotNull(remittances);
        Assert.All(
            remittances.Where(row => row.BillingPeriodId == SubmittedPeriodId),
            row => Assert.True(row.IsSynthetic));
    }

    /// <summary>
    /// A denial has to reach the worklist as a denial. This is the state the aging and
    /// CARC-group screens were built for and that nothing had ever produced.
    /// </summary>
    [Fact]
    public async Task ADeniedRemittanceReachesTheWorklistAsDenied()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        await RunAsync(admin, MockClearinghouseScenario.Denied);

        var remittances = await admin.GetFromJsonAsync<List<RemittanceClaimOutcomeDto>>(
            "/api/v1/billing/remittances");
        Assert.NotNull(remittances);
        Assert.Contains(remittances, row =>
            row.BillingPeriodId == SubmittedPeriodId &&
            row.Status == nameof(RemittanceClaimStatus.Denied) &&
            row.ReasonCode == "29");
    }

    /// <summary>
    /// A rejected file never reaches the payer, so no claim outcome and no deposit should
    /// appear from it. Recording a payment for a batch the clearinghouse refused would be
    /// worse than recording nothing.
    /// </summary>
    [Fact]
    public async Task ASyntaxRejectionRecordsTheRefusalAndNothingElse()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        var result = await RunAsync(admin, MockClearinghouseScenario.SyntaxRejected);

        Assert.Equal([nameof(BillingSubmissionStage.FunctionalRejected)], result.StagesRecorded);
        Assert.Equal(0, result.ClaimOutcomesRecorded);
        Assert.False(result.DepositRecorded);
        Assert.Null(result.ClaimAcknowledgement);
        Assert.Null(result.RemittanceAdvice);
    }

    /// <summary>
    /// The deposit is recorded from what the payer said it sent, not from the sum of the
    /// claims. A provider-level adjustment makes those differ with every claim correct,
    /// and deriving one from the other would hide the discrepancy the reconciliation
    /// screen exists to surface.
    /// </summary>
    [Fact]
    public async Task AProviderLevelAdjustmentLeavesTheDepositShortOfTheClaimTotal()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync("admin-two");

        await RunAsync(admin, MockClearinghouseScenario.ProviderLevelAdjustment);

        var deposits = await admin.GetFromJsonAsync<List<RemittanceDepositDto>>(
            "/api/v1/billing/remittance-deposits");
        Assert.NotNull(deposits);
        var deposit = deposits.First(row => row.ProviderLevelAdjustmentAmount != 0m);

        Assert.Equal(25m, deposit.ProviderLevelAdjustmentAmount);
        Assert.NotEqual(deposit.ClaimPaymentAmount, deposit.RemittancePaymentAmount);
    }
}
