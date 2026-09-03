using Sati.Contracts.V1;
namespace Sati.Data.Cloud;

public sealed class CloudSafetyPlanService(CloudApiClient api, ISessionService session) : ISafetyPlanService
{
    private int ActorId => session.CurrentUser?.Id ?? throw new UnauthorizedAccessException();
    public async Task<SafetyPlanDto?> GetAsync(int personId, DateTime cycleStart)
    {
        // No plan yet is a legitimate null response, not an API failure.
        var bytes = await api.GetBytesAsync($"/api/v1/people/{personId}/safety-plans/latest?cycleStart={cycleStart:yyyy-MM-dd}");
        return bytes.Length == 0 ? null : System.Text.Json.JsonSerializer.Deserialize<SafetyPlanDto>(bytes,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
    public Task<SafetyPlanDto> StartAsync(int personId, DateTime cycleStart) =>
        api.PostAsync<object, SafetyPlanDto>($"/api/v1/people/{personId}/safety-plans/draft?authorUserId={ActorId}&cycleStart={cycleStart:yyyy-MM-dd}", new {});
    public Task<SafetyPlanDto> ChangeAsync(SafetyPlanDto plan, string action, string? document = null, string? reason = null) => action switch
    {
        "save" => api.PutAsync<SaveSafetyPlanDocumentRequest, SafetyPlanDto>($"/api/v1/safety-plans/{plan.Id}/document", new(document ?? plan.DocumentJson, plan.Revision)),
        "submit" => api.PostAsync<object, SafetyPlanDto>($"/api/v1/safety-plans/{plan.Id}/submit?authorUserId={ActorId}&expectedRevision={plan.Revision}", new {}),
        "approve" or "return" => api.PostAsync<ReviewSafetyPlanRequest, SafetyPlanDto>($"/api/v1/safety-plans/{plan.Id}/{action}", new(plan.Revision, reason)),
        _ => throw new ArgumentException("Unknown safety-plan action.")
    };
    public async Task<AgencyReleaseResult> GenerateAsync(int personId, DateTime cycleStart)
    {
        var response = await api.PostBytesWithHeaderAsync($"/api/v1/people/{personId}/documents/SafetyPlan",
            new RenderAnnualDocumentRequest(cycleStart), "Content-Disposition");
        var name = $"Safety-Plan-{personId}.pdf";
        if (System.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(response.HeaderValues.FirstOrDefault(), out var disposition))
        {
            var suggested = System.IO.Path.GetFileName((disposition.FileNameStar ?? disposition.FileName)?.Trim('"'));
            if (!string.IsNullOrWhiteSpace(suggested)) name = suggested;
        }
        return new(response.Bytes, name);
    }
}
