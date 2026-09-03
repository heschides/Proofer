using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sati.ViewModels.ClientDocuments;

public partial class SafetyPlanViewModel(ISafetyPlanService service, ISessionService session) : ObservableObject
{
    private readonly LatestRequestTracker requests = new();
    private Person? person;
    private SafetyPlanDto? plan;
    [ObservableProperty] private DateTime? cycleStart;
    [ObservableProperty] private string message = "Select a consumer.";
    [ObservableProperty] private string returnReason = "";
    [ObservableProperty] private bool isBusy;
    public ObservableCollection<SafetyPlanSectionViewModel> Sections { get; } = [];
    public bool CanAuthor => person is not null && session.CurrentUser is { } actor &&
        SafetyPlanRules.CanAuthor(actor.Id, actor.Permissions, person.UserId);
    public bool CanEdit => CanAuthor && plan?.Status == "Draft" && !IsBusy;
    public bool CanReview => plan is not null && session.CurrentUser is { } actor &&
        SafetyPlanRules.CanReview(actor.Id, actor.Permissions, plan.AuthorUserId) && plan.Status == "ReadyForReview" && !IsBusy;
    public string Status => plan is null ? "No plan for this cycle" : $"Version {plan.Version} · {plan.Status}";
    public event Action<AgencyReleaseResult>? PdfReady;
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnCycleStartChanged(DateTime? value)
    {
        requests.Invalidate(); plan = null; Sections.Clear(); ReturnReason = ""; IsBusy = false;
        Message = "Load or start the plan for this cycle."; NotifyState();
    }
    public void SetPerson(Person? selected)
    {
        requests.Invalidate(); person = selected; plan = null; Sections.Clear(); IsBusy = false;
        CycleStart = selected?.EffectiveDate is DateTime effective ? AnnualDocumentCycle.CurrentStart(effective, DateTime.Today) : null;
        ReturnReason = ""; Message = ""; NotifyState();
        _ = ReloadAsync();
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanAuthor)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanReview)); OnPropertyChanged(nameof(Status));
    }
    private void Apply(SafetyPlanDto? value)
    {
        plan = value; Sections.Clear();
        if (value is not null)
        {
            var document = JsonSerializer.Deserialize<SafetyPlanDocument>(value.DocumentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            foreach (var section in document.Sections) Sections.Add(new(section.Id, section.Text));
            ReturnReason = value.ReturnReason ?? "";
        }
        NotifyState();
    }
    private async Task Run(Func<int, DateTime, Task<SafetyPlanDto?>> operation)
    {
        if (IsBusy) return;
        if (person is null || CycleStart is null) { Message = "Select a consumer with an effective date."; return; }
        var id = person.Id; var cycle = CycleStart.Value.Date; var ticket = requests.Begin();
        IsBusy = true; Message = "";
        try { var value = await operation(id, cycle); if (requests.IsCurrent(ticket)) Apply(value); }
        catch (SafetyPlanWorkflowException error) { if (requests.IsCurrent(ticket)) Message = error.Message; }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The operation could not be completed. Check the cycle and permissions, then reload to check the latest version."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
    [RelayCommand] private Task ReloadAsync() => Run(service.GetAsync);
    [RelayCommand] private Task StartAsync() => Run(async (id, cycle) => await service.StartAsync(id, cycle));
    [RelayCommand] private Task SaveAsync() => Change("save");
    [RelayCommand] private Task SubmitAsync() => Change("submit");
    [RelayCommand] private Task ApproveAsync() => Change("approve");
    [RelayCommand] private Task ReturnAsync() => Change("return");
    private Task Change(string action)
    {
        if (plan is null || IsBusy) return Task.CompletedTask;
        var snapshot = plan; var reason = ReturnReason;
        var document = JsonSerializer.Serialize(new SafetyPlanDocument(1, Sections.Select(x => new SafetyPlanSection(x.Id, x.Text)).ToList()));
        return Run(async (_, _) =>
        {
            if (action == "submit") snapshot = await service.ChangeAsync(snapshot, "save", document);
            return await service.ChangeAsync(snapshot, action, action == "save" ? document : null, reason);
        });
    }
    [RelayCommand] private async Task GenerateAsync()
    {
        if (person is null || CycleStart is null || IsBusy) return;
        var ticket = requests.Begin(); var id = person.Id; var cycle = CycleStart.Value; IsBusy = true;
        try { var pdf = await service.GenerateAsync(id, cycle); if (requests.IsCurrent(ticket)) PdfReady?.Invoke(pdf); }
        catch (Exception) { if (requests.IsCurrent(ticket)) Message = "The PDF could not be generated. Save and reload the plan, then try again."; }
        finally { if (requests.IsCurrent(ticket)) IsBusy = false; }
    }
}

public partial class SafetyPlanSectionViewModel(string id, string text) : ObservableObject
{
    public string Id { get; } = id;
    public string Title => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Id.Replace('-', ' '));
    [ObservableProperty] private string text = text;
}
