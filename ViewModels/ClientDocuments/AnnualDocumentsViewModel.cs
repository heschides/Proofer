using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.ClientDocuments;

public partial class AnnualDocumentsViewModel(IAnnualDocumentService service, IDocumentTemplateService templates,
    ISettingsService settings, ISessionService session, SignatureRequestsViewModel? signatures = null) : ObservableObject
{
    public SignatureRequestsViewModel? Signatures { get; } = signatures;
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private AnnualDocumentsStatusDto? status;
    private int activeTicket;
    private bool applyingCycle;
    [ObservableProperty] private DateTime? cycleStart;
    [ObservableProperty] private DateTime? receivedOn;
    [ObservableProperty] private string goodFaithEffortReason = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string reminder = "";
    [ObservableProperty] private string windowDescription = "";
    [ObservableProperty] private string templateBody = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int verificationArtifactId;
    public ObservableCollection<DocumentArtifactDto> Artifacts { get; } = [];
    public bool CanSavePacket => !IsBusy && status?.Window.IsOpen == true && person?.UserId == session.CurrentUser?.Id;
    public bool CanRecordReceipt => !IsBusy && status?.Artifacts.Any(x => x.Kind == "PrivacyPractices" && x.Origin == "GeneratedInSati") == true;
    public bool CanManageTemplates => session.CurrentUser?.HasAdminPermissions == true;
    public string ReceiptStatus => status?.Artifacts.FirstOrDefault(x => x.Kind == "PrivacyPractices") is { } notice &&
        status.AcknowledgedArtifactIds.Contains(notice.Id) ? "Receipt or good-faith effort is recorded for the current notice." : "Receipt or good-faith effort has not been recorded for the current notice.";
    public event Action<AgencyReleaseResult>? FileReady;
    public Func<Task<(string Hash, long Length)?>>? ChooseVerificationFileAsync { get; set; }
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnCycleStartChanged(DateTime? value)
    {
        if (applyingCycle) return;
        requests.Invalidate(); status = null; Artifacts.Clear(); IsBusy = false;
        Signatures?.SetContext(person?.Id ?? 0, []);
        ReceivedOn = null; GoodFaithEffortReason = ""; VerificationArtifactId = 0;
        Reminder = ""; WindowDescription = ""; Message = "Load the selected annual cycle."; NotifyState();
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanSavePacket)); OnPropertyChanged(nameof(CanRecordReceipt));
        OnPropertyChanged(nameof(CanManageTemplates)); OnPropertyChanged(nameof(ReceiptStatus));
    }
    public void SetPerson(Person? selected)
    {
        requests.Invalidate(); person = selected; status = null; Artifacts.Clear();
        Signatures?.SetContext(selected?.Id ?? 0, []);
        IsBusy = false; ReceivedOn = null; GoodFaithEffortReason = ""; VerificationArtifactId = 0;
        Message = ""; Reminder = ""; WindowDescription = "";
        CycleStart = selected?.EffectiveDate is DateTime effective ? AnnualPacketWindow.SuggestedCycle(effective, DateTime.Today, 30) : null;
        NotifyState(); _ = InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        if (person?.EffectiveDate is not DateTime effective) return;
        var ticket = requests.Begin(); var id = person.Id; IsBusy = true;
        try
        {
            var policy = await settings.LoadAsync();
            var cycle = AnnualPacketWindow.SuggestedCycle(effective, DateTime.Today, policy.AnnualPacketOpenDaysBefore);
            var result = await service.GetStatusAsync(id, cycle);
            if (requests.IsCurrent(ticket))
            {
                // This response already owns the request ticket; the user-change callback must not invalidate it.
                applyingCycle = true;
                try { CycleStart = cycle; }
                finally { applyingCycle = false; }
                Apply(result);
            }
        }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "Annual documents could not be loaded. Check the effective date and try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    private void Apply(AnnualDocumentsStatusDto value)
    {
        status = value; Artifacts.Clear(); foreach (var artifact in value.Artifacts) Artifacts.Add(artifact);
        Signatures?.SetContext(person?.Id ?? 0, value.Artifacts);
        WindowDescription = value.Window.IsOpen ? $"Packet available through {value.Window.EndsOn:d}." : $"Packet opens {value.Window.OpensOn:d}.";
        Reminder = value.Reminder; NotifyState();
    }
    private async Task Run(Func<int, DateTime, Task<string?>> operation)
    {
        if (person is null || CycleStart is null || IsBusy) return;
        var id = person.Id; var cycle = CycleStart.Value.Date; var ticket = requests.Begin(); activeTicket = ticket; IsBusy = true; Message = "";
        try
        {
            var resultMessage = await operation(id, cycle);
            var updated = await service.GetStatusAsync(id, cycle);
            if (requests.IsCurrent(ticket)) { Apply(updated); Message = resultMessage ?? ""; }
        }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The operation could not be completed. Check the dates and required fields, then reload."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    [RelayCommand] private Task ReloadAsync() => Run((_, _) => Task.FromResult<string?>(null));
    [RelayCommand] private Task GenerateNoticeAsync()
    {
        var ticket = 0;
        return Run(async (id, cycle) => { ticket = activeTicket; var result = await templates.GeneratePrivacyPracticesAsync(id, cycle);
            if (requests.IsCurrent(ticket)) FileReady?.Invoke(result); return "Notice generated. Record its receipt separately."; });
    }
    [RelayCommand] private Task SavePacketAsync()
    {
        return Run(async (id, cycle) => { var ticket = activeTicket; var result = await service.SavePacketAsync(id, cycle);
            if (requests.IsCurrent(ticket)) FileReady?.Invoke(result); return "Packet generated. Review MANIFEST.txt for outstanding work."; });
    }
    [RelayCommand] private Task AcknowledgeAsync()
    {
        var notice = status?.Artifacts.FirstOrDefault(x => x.Kind == "PrivacyPractices" && x.Origin == "GeneratedInSati");
        if (notice is null) return Task.CompletedTask;
        var request = new AcknowledgeDocumentRequest(notice.Id, ReceivedOn, GoodFaithEffortReason);
        return Run(async (id, _) => { await service.AcknowledgeAsync(id, request); return "Privacy notice receipt recorded."; });
    }
    [RelayCommand] private async Task VerifyAsync()
    {
        if (ChooseVerificationFileAsync is null || VerificationArtifactId <= 0) { Message = "Enter the artifact ID from the manifest or list."; return; }
        if (IsBusy) return;
        var artifactId = VerificationArtifactId; var ticket = requests.Begin();
        var result = await ChooseVerificationFileAsync();
        if (result is null || !requests.IsCurrent(ticket)) return;
        await Run(async (id, _) => (await service.VerifyAsync(id, new(artifactId, result.Value.Hash, result.Value.Length))).Message);
    }
    [RelayCommand] private async Task LoadTemplateAsync()
    {
        if (!CanManageTemplates) return;
        try { var versions = await templates.GetVersionsAsync(AnnualDocumentKind.PrivacyPractices);
            TemplateBody = versions.OrderByDescending(x => x.AgencyId is not null).ThenByDescending(x => x.Version).FirstOrDefault()?.Body ?? ""; }
        catch (Exception) { Message = "The privacy template could not be loaded."; }
    }
    [RelayCommand] private async Task PublishTemplateAsync()
    {
        if (!CanManageTemplates) return;
        try { await templates.PublishAsync(AnnualDocumentKind.PrivacyPractices, TemplateBody); Message = "A new agency template version was published."; }
        catch (Exception) { Message = "The template was not published. Check its text and supported tokens."; }
    }
}
