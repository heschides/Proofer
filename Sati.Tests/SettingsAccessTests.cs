using System.Net;
using System.Text;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.Services;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class SettingsAccessTests
{
    [Theory]
    [InlineData(UserPermissions.None, false)]
    [InlineData(UserPermissions.CaseManagement, false)]
    [InlineData(UserPermissions.Supervision, false)]
    [InlineData(UserPermissions.Administration, true)]
    [InlineData(UserPermissions.Billing, false)]
    public void OnlyAgencyAdministrationPermissionCanManageOperationalSettings(
        UserPermissions permissions,
        bool expected) =>
        Assert.Equal(expected, SettingsAccessPolicy.CanManageAgencySettings(permissions));

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

    [Fact]
    public void PersonalTextShortcutTabIsNotRestrictedToAgencyAdmins()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.xaml"));
        var shortcutTabStart = view.IndexOf("Header=\"Text shortcuts\"", StringComparison.Ordinal);
        var nextTab = view.IndexOf("<TabItem", shortcutTabStart + 1, StringComparison.Ordinal);
        var shortcutTab = view[shortcutTabStart..nextTab];

        Assert.DoesNotContain("CanManageAgencySettings", shortcutTab, StringComparison.Ordinal);
        Assert.Contains("SaveTextShortcutsCommand", shortcutTab, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));

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
