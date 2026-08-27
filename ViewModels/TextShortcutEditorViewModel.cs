using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Services;

namespace Sati.ViewModels;

public sealed partial class TextShortcutEditorViewModel(int digit) : ObservableObject
{
    public int Digit { get; } = digit;
    public string GestureLabel => $"Win+Shift+{Digit}";
    public string CharacterCount => $"{Text.Length}/{TextShortcutService.MaximumTextLength}";

    [ObservableProperty]
    private string text = string.Empty;

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(CharacterCount));
}
