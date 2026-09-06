using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class SignatureRequestsViewModelTests
{
    [Fact]
    public async Task LateRequestListCannotRepopulateADifferentConsumerOrHiddenWorkspace()
    {
        var fake = new Service(); var session = new Session(); var vm = Ready(fake, session);
        var delayed = new TaskCompletionSource<IReadOnlyList<SignatureRequestDto>>();
        fake.List = _ => delayed.Task;
        var load = vm.RefreshAsync(); vm.SetContext(202, []); vm.SetActive(false);
        delayed.SetResult([Request(101)]); await load;
        Assert.Empty(vm.Requests); Assert.Empty(vm.Signers); Assert.False(vm.CanManage);
    }

    [Fact]
    public async Task AccountChangeDuringPdfChoicePreventsUploadAndClearsBytes()
    {
        var fake = new Service(); var session = new Session(); var vm = Ready(fake, session);
        Select(vm); var delayed = new TaskCompletionSource<byte[]?>(); var bytes = new byte[] { 1, 2, 3 };
        vm.ChooseFreezePdfAsync = () => delayed.Task;
        var freeze = vm.FreezeAsync(); session.SetUser(User.Create(21, "other", "Other", "h", "s", UserRole.CaseManager, null, 2));
        delayed.SetResult(bytes); await freeze;
        Assert.Equal(0, fake.Freezes); Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task SelectionChangeDuringPdfChoicePreventsWrongArtifactUpload()
    {
        var fake = new Service(); var vm = Ready(fake, new Session()); Select(vm);
        var delayed = new TaskCompletionSource<byte[]?>(); vm.ChooseFreezePdfAsync = () => delayed.Task;
        var freeze = vm.FreezeAsync(); vm.SelectedArtifact = Artifact(2);
        delayed.SetResult([1, 2, 3]); await freeze;
        Assert.Equal(0, fake.Freezes);
    }

    [Fact]
    public async Task HiddenOrChangedAccountCannotReceiveDownloadedDocument()
    {
        var fake = new Service(); var vm = Ready(fake, new Session()); vm.SelectedRequest = Request(101);
        var delayed = new TaskCompletionSource<AgencyReleaseResult>(); fake.Download = _ => delayed.Task;
        var published = 0; vm.FileReady += _ => published++;
        var download = vm.DownloadOriginalCommand.ExecuteAsync(null); vm.SetActive(false);
        var bytes = new byte[] { 3, 2, 1 }; delayed.SetResult(new(bytes, "original.pdf")); await download;
        Assert.Equal(0, published); Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task PinAttemptRequiresAffirmationsAndAlwaysClearsMaskedInputs()
    {
        var fake = new Service(); var vm = Ready(fake, new Session()); Select(vm);
        var clears = 0; vm.ClearSensitiveInputs += () => clears++;
        await vm.SubmitAsync("73925814", "73925814", false);
        Assert.Equal(0, fake.Creates); Assert.True(clears > 0);
        vm.IdentityConfirmed = true; vm.EmailConfirmed = true;
        await vm.SubmitAsync("73925814", "73925814", false);
        Assert.Equal(1, fake.Creates); Assert.False(vm.IdentityConfirmed); Assert.False(vm.EmailConfirmed);
        Assert.True(clears > 1);
    }

    [Fact]
    public void ReplacementRequiresExplicitSelectionOfTheSameCurrentSigner()
    {
        var vm = Ready(new Service(), new Session()); Select(vm); vm.SelectedRequest = Request(101);
        Assert.Null(vm.SelectedSigner); Assert.False(vm.CanReplace);
        vm.SelectedSigner = new(SignerCapacity.Guardian, 20, "Another Signer", "guardian@example.test");
        Assert.False(vm.CanReplace);
        vm.SelectedSigner = vm.Signers[0]; Assert.True(vm.CanReplace);
    }

    [Theory]
    [InlineData("SafetyPlan", "GeneratedInSati")]
    [InlineData("ReleaseDhhs", "GeneratedInSati")]
    [InlineData("MedicalRecordsRequest", "GeneratedInSati")]
    [InlineData("PrivacyPractices", "Draft")]
    [InlineData("PrivacyPractices", "RecordedAsExternal")]
    public void PolicyBlockedAndNonfinalArtifactsHaveNoRequestAction(string kind, string origin)
    {
        var vm = Ready(new Service(), new Session()); Select(vm);
        vm.SelectedArtifact = Artifact(1) with { Kind = kind, Origin = origin };
        Assert.False(vm.CanCreate);
    }

    private static SignatureRequestsViewModel Ready(Service service, Session session)
    {
        var vm = new SignatureRequestsViewModel(service, session); vm.SetContext(101, [Artifact(1)]); vm.SetActive(true); return vm;
    }
    private static void Select(SignatureRequestsViewModel vm)
    { vm.SelectedArtifact = vm.Artifacts[0]; vm.SelectedSigner = vm.Signers[0]; vm.CompletenessReviewed = true; }
    private static DocumentArtifactDto Artifact(int id) => new(id, 101, 1, "PrivacyPractices", DateTime.Today,
        "GeneratedInSati", DateTime.UtcNow, 12, "hash", 3, "notice.pdf", [], null);
    private static SignatureRequestDto Request(int personId) => new(1, Guid.NewGuid(), personId, 1, "Notice of Privacy Practices",
        "ReceiptAcknowledgment", "Synthetic Signer", "Consumer", "synthetic@example.test", "Issued", 1,
        DateTime.UtcNow, DateTime.UtcNow.AddDays(3), 0, false, "Suppressed", false, null, null, null, []);
    private sealed class Session : ISessionService
    {
        public bool AllowComplianceOverride { get; set; }
        public User? CurrentUser { get; private set; } = User.Create(12, "synthetic", "Synthetic Author", "h", "s", UserRole.CaseManager, null, 1);
        public void SetUser(User user) => CurrentUser = user;
    }
    private sealed class Service : ISignatureService
    {
        public int Freezes; public int Creates;
        public Func<int, Task<IReadOnlyList<SignatureRequestDto>>> List = _ => Task.FromResult<IReadOnlyList<SignatureRequestDto>>([]);
        public Func<int, Task<AgencyReleaseResult>> Download = _ => Task.FromResult(new AgencyReleaseResult([1], "original.pdf"));
        public Task<SignatureAvailabilityDto> GetAvailabilityAsync() => Task.FromResult(new SignatureAvailabilityDto(true, "Fictional-data testing", "Suppressed"));
        public Task<IReadOnlyList<SignatureSignerDto>> GetSignersAsync(int id) => Task.FromResult<IReadOnlyList<SignatureSignerDto>>([new(SignerCapacity.Consumer, null, "Synthetic Signer", "synthetic@example.test")]);
        public Task<IReadOnlyList<SignatureRequestDto>> GetRequestsAsync(int id) => List(id);
        public Task<FrozenSignatureDocumentDto> FreezeAsync(int id, int artifact, FreezeSignatureDocumentRequest r)
        { Freezes++; return Task.FromResult(new FrozenSignatureDocumentDto(1, artifact, "hash", r.Pdf.Length, DateTime.UtcNow)); }
        public Task<SignatureRequestDto> CreateAsync(CreateSignatureRequest r) { Creates++; return Task.FromResult(Request(r.PersonId)); }
        public Task<SignatureRequestDto> ReplaceAsync(int id, ReplaceSignatureRequest r) => Task.FromResult(Request(101));
        public Task<SignatureRequestDto> RevokeAsync(int id, SignatureReasonRequest r) => Task.FromResult(Request(101) with { State = "Revoked" });
        public Task<SignatureRequestDto> WithdrawAuthorizationAsync(int id, SignatureReasonRequest r) => Task.FromResult(Request(101));
        public Task<AgencyReleaseResult> GetOriginalAsync(int id) => Download(id);
        public Task<AgencyReleaseResult> GetSignedAsync(int id) => Download(id);
    }
}
