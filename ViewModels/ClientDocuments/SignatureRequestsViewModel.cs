using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>Staff coordination only. Signer consent and signing remain in the separate portal.</summary>
public partial class SignatureRequestsViewModel(ISignatureService service, ISessionService session) : ObservableObject
{
    private readonly LatestRequestTracker loads = new();
    private int personId;
    private bool active;
    private bool applyingServerResult;
    private int? loadedUserId;
    private Guid createKey = Guid.NewGuid();
    private Guid replaceKey = Guid.NewGuid();
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private string explanation = "Electronic signing has not been loaded.";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private DocumentArtifactDto? selectedArtifact;
    [ObservableProperty] private SignatureSignerDto? selectedSigner;
    [ObservableProperty] private SignatureRequestDto? selectedRequest;
    [ObservableProperty] private bool completenessReviewed;
    [ObservableProperty] private bool identityConfirmed;
    [ObservableProperty] private bool emailConfirmed;
    [ObservableProperty] private string authorityEvidence = "";
    [ObservableProperty] private string reason = "";
    [ObservableProperty] private int expiryHours = SignatureRules.DefaultExpiryHours;
    public IReadOnlyList<SignatureMeaningEntry> Catalog => SignatureMeaningCatalog.All;
    public ObservableCollection<DocumentArtifactDto> Artifacts { get; } = [];
    public ObservableCollection<SignatureSignerDto> Signers { get; } = [];
    public ObservableCollection<SignatureRequestDto> Requests { get; } = [];
    public string ScopeNotice => SignatureRules.ScopeNotice;
    public string ConsumerContext => personId > 0 ? $"Consumer record {personId}. Confirm this is the intended consumer before preparing a request." : "Select a consumer to review signing requests.";
    public string PinExplanation => SigningPinRules.Explanation;
    public string DocumentExplanation => SelectedArtifact is { } artifact && Enum.TryParse<AnnualDocumentKind>(artifact.Kind, out var kind)
        ? SignatureMeaningCatalog.Find(kind)?.Explanation ?? "This document cannot be signed." : "Choose a current document record.";
    public string ElectronicReceiptStatus => Requests.Any(x => x.DocumentName == "Notice of Privacy Practices" && x.State == "Signed" &&
        Artifacts.Any(a => a.Id == x.DocumentArtifactId))
        ? "An electronic notice receipt is recorded with the signer's own name and capacity in the request history. Staff receipt records remain separate."
        : "No completed electronic receipt is shown for the current privacy notice.";
    public bool CanCreate => active && IsEnabled && !IsBusy && IsCurrentAccount && SelectedArtifact is { Origin: "GeneratedInSati", BlankFields.Count: 0 } a &&
        Enum.TryParse<AnnualDocumentKind>(a.Kind, out var kind) && SelectedSigner is { } s && SignatureMeaningCatalog.CanRequest(kind, s.Capacity);
    public bool CanManage => active && IsEnabled && !IsBusy && IsCurrentAccount && SelectedRequest is not null;
    public bool CanFreeze => CanCreate && CompletenessReviewed;
    public bool CanReplace => CanManage && SelectedRequest is { } r && (r.State is "Issued" or "Viewed" or "Expired" or "Revoked") &&
        SelectedSigner is { } s && s.Capacity.ToString() == r.SignerCapacity && s.ContactId == r.SignerContactId;
    public bool CanRevoke => CanManage && SelectedRequest is { } r && SignatureRules.IsOpen(r.State);
    public bool CanWithdrawAuthorization => CanManage && SelectedRequest is { State: "Signed", Meaning: "Authorization", AuthorizationRevokedAtUtc: null };
    public bool CanDownloadSigned => CanManage && SelectedRequest?.HasSignedPackage == true;
    private bool IsCurrentAccount => loadedUserId is not null && loadedUserId == session.CurrentUser?.Id;
    public Func<Task<byte[]?>>? ChooseFreezePdfAsync { get; set; }
    public event Action<AgencyReleaseResult>? FileReady;
    public event Action? ClearSensitiveInputs;
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnIsEnabledChanged(bool value) => NotifyState();
    partial void OnCompletenessReviewedChanged(bool value) => NotifyState();
    private void InvalidateSelection() { if (!applyingServerResult) { loads.Invalidate(); IsBusy = false; } }
    partial void OnSelectedArtifactChanged(DocumentArtifactDto? value) { InvalidateSelection(); ResetAffirmations(); createKey = Guid.NewGuid(); OnPropertyChanged(nameof(DocumentExplanation)); NotifyState(); }
    partial void OnSelectedSignerChanged(SignatureSignerDto? value) { InvalidateSelection(); ResetAffirmations(); createKey = Guid.NewGuid(); NotifyState(); }
    partial void OnSelectedRequestChanged(SignatureRequestDto? value) { InvalidateSelection(); replaceKey = Guid.NewGuid(); Reason = ""; SelectedSigner = null; ResetAffirmations(); NotifyState(); }
    private void ResetAffirmations() { CompletenessReviewed = false; IdentityConfirmed = false; EmailConfirmed = false; AuthorityEvidence = ""; ClearSensitiveInputs?.Invoke(); }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CanCreate), nameof(CanFreeze), nameof(CanManage), nameof(CanReplace), nameof(CanRevoke), nameof(CanWithdrawAuthorization), nameof(CanDownloadSigned), nameof(ElectronicReceiptStatus) }) OnPropertyChanged(name);
    }
    public void SetContext(int id, IReadOnlyList<DocumentArtifactDto> artifacts)
    {
        loads.Invalidate(); personId = id; loadedUserId = session.CurrentUser?.Id; IsBusy = false;
        OnPropertyChanged(nameof(ConsumerContext));
        SelectedArtifact = null; SelectedSigner = null; SelectedRequest = null; Requests.Clear(); Signers.Clear(); Artifacts.Clear();
        foreach (var artifact in artifacts) Artifacts.Add(artifact);
        IsEnabled = false; Message = ""; ResetAffirmations(); NotifyState();
        if (active && personId > 0) _ = RefreshAsync();
    }
    public void SetActive(bool value)
    {
        active = value; loads.Invalidate(); IsBusy = false; ClearSensitiveInputs?.Invoke(); ResetAffirmations();
        if (!value) { Requests.Clear(); Signers.Clear(); SelectedSigner = null; SelectedRequest = null; Message = ""; }
        else if (personId > 0) _ = RefreshAsync();
        NotifyState();
    }
    private bool Current(int ticket, int id, int? userId) => active && loads.IsCurrent(ticket) && id == personId &&
        userId is not null && userId == session.CurrentUser?.Id && userId == loadedUserId;
    [RelayCommand] public async Task RefreshAsync()
    {
        if (!active || personId <= 0 || IsBusy || !IsCurrentAccount) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true; Message = "";
        try
        {
            var availability = await service.GetAvailabilityAsync();
            if (!Current(ticket, id, userId)) return;
            IsEnabled = availability.Enabled; Explanation = availability.Explanation;
            if (!availability.Enabled) return;
            var signers = await service.GetSignersAsync(id);
            var requests = await service.GetRequestsAsync(id);
            if (!Current(ticket, id, userId)) return;
            Signers.Clear(); foreach (var signer in signers) Signers.Add(signer);
            Requests.Clear(); foreach (var request in requests) Requests.Add(request);
            applyingServerResult = true;
            try { SelectedRequest = null; SelectedSigner = null; }
            finally { applyingServerResult = false; }
            NotifyState();
        }
        catch (Exception) { if (Current(ticket, id, userId)) { IsEnabled = false; Message = "Signing requests could not be loaded. Check your connection and reload."; } }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
    [RelayCommand] public async Task FreezeAsync()
    {
        if (!CanFreeze || ChooseFreezePdfAsync is null || SelectedArtifact is not { } artifact) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        byte[]? pdf = null;
        try
        {
            pdf = await ChooseFreezePdfAsync();
            if (pdf is null || !Current(ticket, id, userId)) return;
            if (pdf.Length is <= 0 or > SignatureRules.MaximumPdfBytes) { Message = "Choose a PDF no larger than 15 MB."; return; }
            await service.FreezeAsync(id, artifact.Id, new(Guid.NewGuid(), pdf, true));
            if (Current(ticket, id, userId)) Message = "The exact saved document is retained for signing. Create a request after verifying the signer and email.";
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The document was not frozen. Choose the exact complete PDF saved when this current record was generated."; }
        finally { if (pdf is not null) Array.Clear(pdf); if (Current(ticket, id, userId)) IsBusy = false; }
    }
    // PIN values exist only for this attempt, never as bindable or persisted view-model fields.
    public async Task SubmitAsync(string pin, string confirmPin, bool replace)
    {
        try
        {
            if (!(replace ? CanReplace : CanCreate)) return;
            if (!IdentityConfirmed || !EmailConfirmed || !SigningPinRules.IsValid(pin) || pin != confirmPin)
            { Message = "Confirm identity and the preferred email, then enter the same new valid code twice."; return; }
            var artifact = SelectedArtifact; var signer = SelectedSigner; var selected = SelectedRequest;
            await RunMutation(async () => replace
                ? await service.ReplaceAsync(selected!.Id, new(replaceKey, selected.Revision, pin, confirmPin, true, true, Reason, signer!.Name, signer.Email))
                : await service.CreateAsync(new(createKey, personId, artifact!.Id, signer!.Capacity, signer.ContactId, pin, confirmPin, true, true, AuthorityEvidence, ExpiryHours, signer.Name, signer.Email)));
        }
        finally { ClearSensitiveInputs?.Invoke(); }
    }
    private async Task RunMutation(Func<Task<SignatureRequestDto>> operation)
    {
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true; Message = "";
        try
        {
            var result = await operation();
            if (!Current(ticket, id, userId)) return;
            var prior = Requests.FirstOrDefault(x => x.Id == result.Id); if (prior is not null) Requests.Remove(prior);
            Requests.Insert(0, result);
            applyingServerResult = true;
            try { SelectedRequest = result; }
            finally { applyingServerResult = false; }
            createKey = Guid.NewGuid();
            Message = "The request history was updated. Reload to review all requests and delivery status."; ResetAffirmations(); NotifyState();
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The request could not be updated. Reload its current status, check the required fields, and try again."; }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
    [RelayCommand] private Task RevokeAsync() => CanRevoke && SelectedRequest is { } r ? RunMutation(() => service.RevokeAsync(r.Id, new(r.Revision, Reason))) : Task.CompletedTask;
    [RelayCommand] private Task WithdrawAuthorizationAsync() => CanWithdrawAuthorization && SelectedRequest is { } r ? RunMutation(() => service.WithdrawAuthorizationAsync(r.Id, new(r.Revision, Reason))) : Task.CompletedTask;
    [RelayCommand] private Task DownloadOriginalAsync() => DownloadAsync(false);
    [RelayCommand] private Task DownloadSignedAsync() => DownloadAsync(true);
    private async Task DownloadAsync(bool signed)
    {
        if (!CanManage || SelectedRequest is not { } r || (signed && !r.HasSignedPackage)) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        try
        {
            var file = signed ? await service.GetSignedAsync(r.Id) : await service.GetOriginalAsync(r.Id);
            if (Current(ticket, id, userId)) FileReady?.Invoke(file);
            else Array.Clear(file.Pdf);
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The retained document could not be downloaded. Reload and try again."; }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
}
