using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class AnnualDocumentSelectionTests
{
    [Fact]
    public void ChangingSafetyCycleClearsLoadedPlanBeforeAnyAction()
    {
        var service = new SafetyService();
        var vm = new SafetyPlanViewModel(service, new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.True(vm.CanEdit);
        vm.CycleStart = vm.CycleStart!.Value.AddYears(1);
        Assert.False(vm.CanEdit);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void ChangingPacketCycleClearsPriorArtifactsAndReceiptAction()
    {
        var vm = new AnnualDocumentsViewModel(new AnnualService(), null!, new SettingsServiceStub(), new Session());
        vm.SetPerson(Person.CreatePerson(12, "Synthetic", "Person", "", DateTime.Today.AddYears(-30), DateTime.Today.AddYears(-1), WaiverType.Section21, new Settings()));
        Assert.True(vm.CanSavePacket);
        vm.CycleStart = vm.CycleStart!.Value.AddYears(1);
        Assert.False(vm.CanSavePacket);
        Assert.Empty(vm.Artifacts);
    }

    private sealed class Session : ISessionService
    {
        public bool AllowComplianceOverride { get; set; }
        public User? CurrentUser { get; private set; } = User.Create(12, "synthetic", "Synthetic Author", "hash", "salt", UserRole.CaseManager, null, 1);
        public void SetUser(User user) => CurrentUser = user;
    }
    private sealed class SettingsServiceStub : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => throw new NotSupportedException();
    }
    private sealed class AnnualService : IAnnualDocumentService
    {
        public Task<AnnualDocumentsStatusDto> GetStatusAsync(int id, DateTime cycle) => Task.FromResult(
            new AnnualDocumentsStatusDto(new(cycle, cycle.AddDays(-30), cycle.AddYears(1).AddDays(-1), true), [], [], ""));
        public Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int id, AcknowledgeDocumentRequest request) => throw new NotSupportedException();
        public Task<VerifyDocumentResult> VerifyAsync(int id, VerifyDocumentRequest request) => throw new NotSupportedException();
        public Task<AgencyReleaseResult> SavePacketAsync(int id, DateTime cycle) => throw new NotSupportedException();
    }
    private sealed class SafetyService : ISafetyPlanService
    {
        public Task<SafetyPlanDto?> GetAsync(int id, DateTime cycle) => Task.FromResult<SafetyPlanDto?>(
            new(1, id, 12, cycle, "Draft", 1, 0, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, SafetyPlanRules.EmptyDocumentJson()));
        public Task<SafetyPlanDto> StartAsync(int id, DateTime cycle) => throw new NotSupportedException();
        public Task<SafetyPlanDto> ChangeAsync(SafetyPlanDto plan, string action, string? document = null, string? reason = null) => throw new NotSupportedException();
        public Task<AgencyReleaseResult> GenerateAsync(int id, DateTime cycle) => throw new NotSupportedException();
    }
}
