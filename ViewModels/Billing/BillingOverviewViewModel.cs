using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data.Billing;
using Sati.Models.Billing;

namespace Sati.ViewModels.Billing;

public partial class BillingOverviewViewModel(IBillingService billingService) : ObservableObject
{
    [ObservableProperty] private string procedureCode = string.Empty;
    [ObservableProperty] private string? modifier;
    [ObservableProperty] private decimal? unitRate;
    [ObservableProperty] private string ediSubmitterId = string.Empty;
    [ObservableProperty] private string payerName = string.Empty;
    [ObservableProperty] private string payerId = string.Empty;
    [ObservableProperty] private string contactName = string.Empty;
    [ObservableProperty] private string contactPhone = string.Empty;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isBusy;

    public bool HasLoaded { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            var configuration = await billingService.GetBillingConfigurationAsync();
            ProcedureCode = configuration.ProcedureCode;
            Modifier = configuration.Modifier;
            UnitRate = configuration.UnitRate;
            EdiSubmitterId = configuration.EdiSubmitterId;
            PayerName = configuration.PayerName;
            PayerId = configuration.PayerId;
            ContactName = configuration.ContactName;
            ContactPhone = configuration.ContactPhone;
            HasLoaded = true;
            StatusMessage = "Billing configuration loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load billing configuration: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            IsBusy = true;
            await billingService.SaveBillingConfigurationAsync(new BillingConfiguration(
                ProcedureCode, Modifier, UnitRate, EdiSubmitterId,
                PayerName, PayerId, ContactName, ContactPhone));
            await LoadAsync();
            StatusMessage = "Billing configuration saved. Refresh the queue to apply it.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to save billing configuration: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
