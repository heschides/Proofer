using System.Net;
using System.Net.Http.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class FormCompletionApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task UpdateRejectsAFutureCompletionDateWithoutChangingStoredState()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var form = before!
            .SelectMany(person => person.Forms)
            .First();

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/forms/{form.Id}",
            new UpdateFormRequest(DateTime.Today.AddDays(1), form.OpenedDate));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var stored = after!
            .SelectMany(person => person.Forms)
            .Single(candidate => candidate.Id == form.Id);
        Assert.Equal(form.CompletedDate, stored.CompletedDate);
        Assert.Equal(form.IsCompliant, stored.IsCompliant);
    }
}
