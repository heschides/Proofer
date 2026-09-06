using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Billing;

public partial class BillingRemittancesViewModel(
    IBillingService billingService,
    ISessionService sessionService) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly LatestRequestTracker _accountLoads = new();
    public ObservableCollection<RemittanceClaimOutcomeDto> Outcomes { get; } = [];
    public ObservableCollection<RemittanceDepositDto> Deposits { get; } = [];
    [ObservableProperty] private string? statusMessage;
    public bool HasLoaded { get; private set; }

    public async Task LoadAsync(bool waitForExisting = false)
    {
        if (waitForExisting)
            await _loadGate.WaitAsync();
        else if (!await _loadGate.WaitAsync(0))
            return;
        var account = sessionService.CurrentUser;
        var request = _accountLoads.Begin();
        try
        {
            var actor = account?.ToAgencyActor()
                ?? throw new UnauthorizedAccessException("A signed-in user is required.");
            var outcomes = await billingService.GetRemittanceOutcomesAsync(actor);
            var deposits = await billingService.GetRemittanceDepositsAsync(actor);
            if (!_accountLoads.IsCurrent(request) || !ReferenceEquals(sessionService.CurrentUser, account))
                return;
            Outcomes.Clear();
            Deposits.Clear();
            foreach (var outcome in outcomes)
                Outcomes.Add(outcome);
            foreach (var deposit in deposits)
                Deposits.Add(deposit);
            HasLoaded = true;
            StatusMessage = Outcomes.Count == 0
                ? "No remittance claim outcomes have been received."
                : $"Showing {Deposits.Count} deposit reconciliation(s) and {Outcomes.Count} claim outcome(s).";
        }
        catch (Exception ex)
        {
            if (_accountLoads.IsCurrent(request) && ReferenceEquals(sessionService.CurrentUser, account))
                StatusMessage = $"Unable to load remittance history: {ex.Message}";
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void ClearForAccountSwitch()
    {
        _accountLoads.Invalidate();
        Outcomes.Clear();
        Deposits.Clear();
        StatusMessage = null;
        HasLoaded = false;
    }

}
