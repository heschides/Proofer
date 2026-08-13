using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.ViewModels.Billing
{
    public partial class BillingQueueItemViewModel : ObservableObject
    {
        public BillingValidationResult Result { get; }

        [ObservableProperty] private bool isSelected;

        public bool IsValid => Result.IsValid;
        public bool IsComplianceOverride => Result.Note.ComplianceOverride;
        public decimal BillingUnits => BillingRules.CalculateSection13Units(Result.Note.Minutes);
        public string ProcedureDisplay { get; }

        public BillingQueueItemViewModel(BillingValidationResult result, BillingConfiguration configuration)
        {
            Result = result;
            ProcedureDisplay = string.IsNullOrWhiteSpace(configuration.Modifier)
                ? configuration.ProcedureCode
                : $"{configuration.ProcedureCode} {configuration.Modifier}";
        }
    }
}
