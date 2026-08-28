using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class PersonCreationApiTests(SatiApiFactory factory)
{
    [Fact]
    public async Task ValidCreationAssignsAuthenticatedOwnerAndCommitsHistoryAndAudit()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var auditBefore = await factory.GetAuditEventsAsync("person.created");

        var response = await owner.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with
            {
                FirstName = "ApiCreate",
                LastName = Guid.NewGuid().ToString("N")[..10]
            });
        var created = await response.Content.ReadFromJsonAsync<PersonDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(12, created.UserId);
        Assert.Equal(1, created.AgencyId);
        Assert.Empty(created.Forms);

        var after = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.Equal(before!.Count + 1, after!.Count);
        Assert.Contains(after, person => person.Id == created.Id);

        var history = await admin.GetFromJsonAsync<List<PersonVersionDto>>(
            $"/api/v1/people/{created.Id}/history");
        var version = Assert.Single(history!);
        Assert.Equal("Created", version.ChangeKind);
        Assert.Equal(auditBefore.Count + 1, (await factory.GetAuditEventsAsync("person.created")).Count);
    }

    [Fact]
    public async Task AdminCanCreateAMarkedTestConsumerAndTheLifecycleRecordsIt()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var request = ValidRequest() with
        {
            FirstName = "Synthetic",
            LastName = Guid.NewGuid().ToString("N")[..10],
            IsTestData = true
        };

        var response = await admin.PostAsJsonAsync("/api/v1/people", request);
        var created = await response.Content.ReadFromJsonAsync<PersonDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(created!.IsTestData);
        var history = await admin.GetFromJsonAsync<List<PersonVersionDto>>(
            $"/api/v1/people/{created!.Id}/history");
        Assert.Contains(Assert.Single(history!).Changes,
            change => change.Field == "isTestData" && change.NewValue == "Yes");
    }

    [Fact]
    public async Task CaseManagerCannotForgeTheTestMarker()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");

        var response = await owner.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with { IsTestData = true });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("isTestData", out _));
        Assert.Equal(before!.Count,
            (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!.Count);
    }

    [Fact]
    public async Task TestDesignationCannotBeChangedByTheOrdinaryEditRoute()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var request = ValidRequest() with
        {
            FirstName = "Immutable",
            LastName = Guid.NewGuid().ToString("N")[..10],
            IsTestData = true
        };
        var createdResponse = await admin.PostAsJsonAsync("/api/v1/people", request);
        var created = await createdResponse.Content.ReadFromJsonAsync<PersonDto>();

        var editResponse = await admin.PutAsJsonAsync(
            $"/api/v1/people/{created!.Id}",
            request with { ExpectedRevision = created.Revision, IsTestData = false });
        var problem = await editResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, editResponse.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("isTestData", out _));
        var people = await admin.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        Assert.True(people!.Single(person => person.Id == created.Id).IsTestData);
    }

    [Theory]
    [InlineData("first", "firstName")]
    [InlineData("last", "lastName")]
    [InlineData("birth", "birthDate")]
    [InlineData("bio", "bio")]
    [InlineData("gender", "gender")]
    [InlineData("waiver", "waiver")]
    [InlineData("day-count", "dayProgramCount")]
    [InlineData("email", "email")]
    [InlineData("phone", "phoneNumber")]
    [InlineData("billing-state", "billingState")]
    [InlineData("rep-payee", "repPayeeMonthlyIncome")]
    public async Task InvalidCreationReturnsSpecificFieldAndWritesNothing(
        string scenario,
        string expectedField)
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var auditBefore = await factory.GetAuditEventsAsync("person.created");
        var request = scenario switch
        {
            "first" => ValidRequest() with { FirstName = new string('a', 51) },
            "last" => ValidRequest() with { LastName = " " },
            "birth" => ValidRequest() with { BirthDate = DateTime.Today.AddDays(1) },
            "bio" => ValidRequest() with { Bio = " " },
            "gender" => ValidRequest() with { Gender = "Invalid" },
            "waiver" => ValidRequest() with { Waiver = "Invalid" },
            "day-count" => ValidRequest() with { DayProgramCount = 0 },
            "email" => ValidRequest() with { Email = "not-an-email" },
            "phone" => ValidRequest() with { PhoneNumber = new string('1', 21) },
            "billing-state" => ValidRequest() with { BillingState = "MEE" },
            "rep-payee" => ValidRequest() with
            {
                CaseManagerIsRepPayee = true,
                RepPayeeMonthlyIncome = null,
                RepPayeeRegularCheckRequestNeeds = "Monthly rent"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var response = await owner.PostAsJsonAsync("/api/v1/people", request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
        Assert.Equal(before!.Count, (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!.Count);
        Assert.Equal(auditBefore.Count, (await factory.GetAuditEventsAsync("person.created")).Count);
    }

    [Fact]
    public async Task ValidInitialFormsAreSavedAsOneCreationGraph()
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var effective = DateTime.Today.AddMonths(-2);
        var forms = PersonSaveRules.FormTypes.Select((type, index) =>
            index < 4
                ? new SavePersonFormRequest(0, type, false, null, null)
                : new SavePersonFormRequest(0, type, true, effective, null)).ToList();

        var response = await owner.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with
            {
                FirstName = "ApiForms",
                LastName = Guid.NewGuid().ToString("N")[..10],
                EffectiveDate = effective,
                Waiver = "Section21",
                Forms = forms
            });
        var created = await response.Content.ReadFromJsonAsync<PersonDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(PersonSaveRules.FormTypes.Count, created.Forms.Count);
        Assert.All(created.Forms.Where(form => form.IsCompliant), form => Assert.NotNull(form.CompletedDate));
        Assert.All(created.Forms.Where(form => !form.IsCompliant), form => Assert.Null(form.CompletedDate));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("checked-without-date")]
    [InlineData("future-completion")]
    public async Task InvalidInitialFormsReturnFormsErrorAndCreateNoClient(string scenario)
    {
        using var owner = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var before = await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload");
        var forms = PersonSaveRules.FormTypes
            .Select(type => new SavePersonFormRequest(0, type, false, null, null))
            .ToList();
        forms = scenario switch
        {
            "missing" => forms.Skip(1).ToList(),
            "duplicate" => forms.Select((form, index) => index == 1
                ? form with { Type = forms[0].Type }
                : form).ToList(),
            "unknown" => forms.Select((form, index) => index == 0
                ? form with { Type = "Unknown" }
                : form).ToList(),
            "checked-without-date" => forms.Select((form, index) => index == 0
                ? form with { IsCompliant = true }
                : form).ToList(),
            "future-completion" => forms.Select((form, index) => index == 0
                ? form with { IsCompliant = true, CompletedDate = DateTime.Today.AddDays(1) }
                : form).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var response = await owner.PostAsJsonAsync(
            "/api/v1/people",
            ValidRequest() with
            {
                EffectiveDate = DateTime.Today.AddMonths(-2),
                Waiver = "Section21",
                Forms = forms
            });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("forms", out _));
        Assert.Equal(before!.Count, (await owner.GetFromJsonAsync<List<PersonDto>>("/api/v1/caseload"))!.Count);
    }

    private static SavePersonRequest ValidRequest() => new(
        "Jamie",
        "River",
        new DateTime(1990, 4, 3),
        "Unknown",
        null,
        "A valid API client creation request.",
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
        "jamie@example.test");
}
