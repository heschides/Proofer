using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models.Billing;

namespace Sati.ViewModels.Billing;

public partial class BillingGenerationStageRow : ObservableObject
{
    private readonly Action _selectionChanged;

    public BillingGenerationStageRow(
        BillingPeriod period,
        bool isSelected,
        Action selectionChanged)
    {
        Period = period;
        _selectionChanged = selectionChanged;
        this.isSelected = isSelected;
    }

    public BillingPeriod Period { get; }
    public int ClaimCount => Period.Lines.Count;
    public string CaseManagerName => string.IsNullOrWhiteSpace(Period.CaseManagerName)
        ? $"Case manager #{Period.UserId}"
        : Period.CaseManagerName;
    public string BillingMonth => new DateTime(Period.Year, Period.Month, 1).ToString("MMMM yyyy");
    public string ReadinessSummary
    {
        get
        {
            var errors = Period.Lines.SelectMany(line => line.ReadinessErrors)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return errors.Count == 0 ? "Ready for 837" : string.Join("; ", errors);
        }
    }

    [ObservableProperty] private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
