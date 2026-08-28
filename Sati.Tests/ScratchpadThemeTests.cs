using Sati.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ScratchpadThemeTests
{
    [Fact]
    public void BothScratchpadEditorsUseTheDarkThemesPrimaryTextAndCaretColors()
    {
        WpfUiHarness.Run(() =>
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var themeIndex = dictionaries
                .Select((dictionary, index) => new { dictionary, index })
                .Single(item => item.dictionary.Source?.OriginalString.Contains(
                    "Themes/", StringComparison.OrdinalIgnoreCase) == true &&
                    !item.dictionary.Source.OriginalString.EndsWith(
                        "States.xaml", StringComparison.OrdinalIgnoreCase))
                .index;
            var originalTheme = dictionaries[themeIndex];

            try
            {
                dictionaries[themeIndex] = new ResourceDictionary
                {
                    Source = new Uri(
                        "/Sati;component/Themes/HarborNight.xaml",
                        UriKind.Relative)
                };
                var view = new ScratchpadView();
                WpfUiHarness.Realize(view);
                var expected = Assert.IsType<SolidColorBrush>(
                    Application.Current.FindResource("TextPrimaryBrush"));

                void AssertEditor(string name)
                {
                    var editor = WpfUiHarness.FindByAutomationName<TextBox>(view, name);
                    Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(editor.Foreground).Color);
                    Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(editor.CaretBrush).Color);
                }

                AssertEditor("Today's Work");
                var tabs = Assert.Single(
                    WpfUiHarness.Descendants(view).OfType<TabControl>());
                tabs.SelectedIndex = 1;
                WpfUiHarness.Realize(view);
                AssertEditor("Tomorrow's Agenda for the next workday");
            }
            finally
            {
                dictionaries[themeIndex] = originalTheme;
            }
        });
    }
}
