using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class DemoResetApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task CaseManagerCannotRequestAFullDemoReset()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/demo/reset", new DemoResetRequest("RESET DEMO"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExactTypedConfirmationIsRequiredBeforeCoordinatorIsCalled()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/demo/reset", new DemoResetRequest("reset demo"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CorrectConfirmationFailsClosedWhenResetServiceIsNotConfigured()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/demo/reset", new DemoResetRequest("RESET DEMO"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ResetRotationInvalidatesEveryPreviouslyIssuedToken()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        var previous = await factory.RotateDemoInstanceAsync();
        try
        {
            using var response = await client.GetAsync("/api/v1/admin/overview");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await factory.RestoreDemoInstanceAsync(previous);
        }
    }
}
