using System.Net;
using System.Text;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class SettingsAccessTests
{
    [Theory]
    [InlineData(UserRole.CaseManager, false)]
    [InlineData(UserRole.Supervisor, false)]
    [InlineData(UserRole.Director, false)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.PlatformOperator, false)]
    public void OnlyAgencyAdminsCanManageOperationalSettings(UserRole role, bool expected) =>
        Assert.Equal(expected, SettingsAccessPolicy.CanManageAgencySettings(role));

    [Fact]
    public async Task CloudRejectionBecomesAnExpectedSettingsSaveFailure()
    {
        using var http = new HttpClient(new ForbiddenSettingsHandler())
        {
            BaseAddress = new Uri("https://demo.sati.invalid/")
        };
        var api = new CloudApiClient(http);
        api.SetAccessToken("test-token");
        var service = new CloudSettingsService(api);

        var error = await Assert.ThrowsAsync<SettingsSaveException>(() =>
            service.SaveAsync(new Settings()));

        Assert.Contains("not permitted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<CloudApiException>(error.InnerException);
    }

    private sealed class ForbiddenSettingsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
    }
}
