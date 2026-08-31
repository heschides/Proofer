using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

public partial class BillingRemittancesViewModel(
    IBillingService billingService,
    ISessionService sessionService) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    public ObservableCollection<RemittanceClaimOutcomeDto> Outcomes { get; } = [];
    public ObservableCollection<RemittanceDepositDto> Deposits { get; } = [];
    [ObservableProperty] private string? statusMessage;
    public bool HasLoaded { get; private set; }

    public async Task LoadAsync()
    {
        if (!await _loadGate.WaitAsync(0))
            return;
        try
        {
            Outcomes.Clear();
            Deposits.Clear();
            var actor = CurrentActor();
            foreach (var outcome in await billingService.GetRemittanceOutcomesAsync(actor))
                Outcomes.Add(outcome);
            foreach (var deposit in await billingService.GetRemittanceDepositsAsync(actor))
                Deposits.Add(deposit);
            HasLoaded = true;
            StatusMessage = Outcomes.Count == 0
                ? "No remittance claim outcomes have been received."
                : $"Showing {Deposits.Count} deposit reconciliation(s) and {Outcomes.Count} claim outcome(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load remittance history: {ex.Message}";
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private AgencyActor CurrentActor() => sessionService.CurrentUser?.ToAgencyActor()
        ?? throw new UnauthorizedAccessException("A signed-in user is required.");
}
