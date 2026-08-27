using System.Windows;
using System.Windows.Controls;

namespace Sati.Services;

public static class TextShortcutTarget
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TextShortcutTarget),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    internal static bool TryInsert(TextBox textBox, string text)
    {
        if (!GetIsEnabled(textBox) || !textBox.IsEnabled || textBox.IsReadOnly || string.IsNullOrEmpty(text))
            return false;

        var insertionPoint = textBox.SelectionStart;
        textBox.SelectedText = text;
        textBox.CaretIndex = insertionPoint + text.Length;
        textBox.SelectionLength = 0;
        return true;
    }
}
