using Sati.Contracts.V1;
namespace Sati.Data;

public interface ISafetyPlanService
{
    Task<SafetyPlanDto?> GetAsync(int personId, DateTime cycleStart);
    Task<SafetyPlanDto> StartAsync(int personId, DateTime cycleStart);
    Task<SafetyPlanDto> ChangeAsync(SafetyPlanDto plan, string action, string? document = null, string? reason = null);
    Task<AgencyReleaseResult> GenerateAsync(int personId, DateTime cycleStart);
}
