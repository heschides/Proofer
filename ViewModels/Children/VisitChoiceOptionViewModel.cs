using CommunityToolkit.Mvvm.ComponentModel;

namespace Sati.ViewModels.Children;

public sealed partial class VisitChoiceOptionViewModel<T>(T value, string displayLabel)
    : ObservableObject where T : struct, Enum
{
    public T Value { get; } = value;
    public string DisplayLabel { get; } = displayLabel;

    [ObservableProperty]
    private bool isSelected;
}
