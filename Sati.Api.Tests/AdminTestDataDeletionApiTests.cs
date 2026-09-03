using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class AdminTestDataDeletionApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task AdminAffirmationDeletesTheWholeTestConsumerGraphAndRetainsAuditEvidence()
    {
        var seed = await factory.CreateTestConsumerGraphAsync();
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                Request(seed));
            var result = await response.Content.ReadFromJsonAsync<TestConsumerDeletionResultDto>();
            var after = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            var deletionEvents = (await factory.GetAuditEventsAsync("test-data.consumer-deleted"))
                .Where(candidate => candidate.ResourceId == seed.PersonId.ToString())
                .ToList();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
            Assert.NotNull(result);
            Assert.Equal(seed.PersonId, result.PersonId);
            Assert.Equal(before.RelatedRecords, result.RelatedRecordsDeleted);
            Assert.Equal(1, result.FormsDeleted);
            Assert.Equal(1, result.FormAttestationsDeleted);
            Assert.Equal(1, result.NotesDeleted);
            Assert.Equal(1, result.ContactsDeleted);
            Assert.Equal(1, result.ReviewsDeleted);
            Assert.Equal(1, result.AppointmentsDeleted);
            Assert.Equal(1, result.AssessmentsDeleted);
            Assert.Equal(1, result.AtRequestsDeleted);
            Assert.Equal(1, result.AtRequestItemsDeleted);
            Assert.Equal(1, result.PersonVersionsDeleted);
            Assert.Equal(1, result.PersonProvidersDeleted);
            Assert.Equal(0, after.People);
            Assert.Equal(0, after.RelatedRecords);
            Assert.Equal(0, after.ClaimLines);
            Assert.Equal(2, after.AuditEvents); // fixture event plus deletion event

            var auditEvent = Assert.Single(deletionEvents);
            Assert.Equal(1, auditEvent.AgencyId);
            Assert.Equal(11, auditEvent.ActorUserId);
            Assert.Equal("Person", auditEvent.ResourceType);
            Assert.Contains(TestDataDeletionRules.ConsumerAttestation, auditEvent.MetadataJson);
            Assert.Contains("relatedRecordsDeleted", auditEvent.MetadataJson);
            Assert.DoesNotContain("Disposable", auditEvent.MetadataJson);
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task CaseManagerCannotDeleteTestConsumerData()
    {
        var seed = await factory.CreateTestConsumerGraphAsync();
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                Request(seed));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task AdminCannotDeleteAnotherAgencysTestConsumer()
    {
        var seed = await factory.CreateTestConsumerGraphAsync(agencyId: 2);
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                Request(seed));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task MissingAffirmationFailsBeforeAnyRecordIsDeleted()
    {
        var seed = await factory.CreateTestConsumerGraphAsync();
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                new DeleteTestConsumerRequest(seed.Revision, ""));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_test_data_attestation", error?.Code);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task StaleRevisionRollsBackWithoutADeletionAuditEvent()
    {
        var seed = await factory.CreateTestConsumerGraphAsync();
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                new DeleteTestConsumerRequest(
                    seed.Revision - 1,
                    TestDataDeletionRules.ConsumerAttestation));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            var events = (await factory.GetAuditEventsAsync("test-data.consumer-deleted"))
                .Where(candidate => candidate.ResourceId == seed.PersonId.ToString());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("stale_test_consumer", error?.Code);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
            Assert.Empty(events);
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task BillingClaimRecordBlocksDeletionAndExplainsTheSafeNextStep()
    {
        var seed = await factory.CreateTestConsumerGraphAsync(withClaimLine: true);
        try
        {
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                Request(seed));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("test_consumer_has_claims", error?.Code);
            Assert.Contains("billing claim records", error?.Message);
            Assert.Contains("help menu", error?.Message);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    [Fact]
    public async Task AdminCannotDeleteAConsumerThatWasNotMarkedTestAtCreation()
    {
        var seed = await factory.CreateTestConsumerGraphAsync();
        try
        {
            await factory.SetTestConsumerMarkerAsync(seed.PersonId, false);
            var before = await factory.GetTestConsumerGraphAsync(seed.PersonId);
            using var client = await factory.CreateAuthenticatedClientAsync("admin-one");

            var response = await client.PostAsJsonAsync(
                $"/api/v1/admin/test-data/consumers/{seed.PersonId}/delete",
                Request(seed));
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("consumer_not_test_data", error?.Code);
            Assert.Equal(before, await factory.GetTestConsumerGraphAsync(seed.PersonId));
        }
        finally
        {
            await factory.RemoveTestConsumerGraphAsync(seed.PersonId);
        }
    }

    private static DeleteTestConsumerRequest Request(TestConsumerSeed seed) =>
        new(seed.Revision, TestDataDeletionRules.ConsumerAttestation);
}
