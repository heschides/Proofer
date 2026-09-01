using Sati.Views;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class DailyAgendaUiStructureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(DailyAgendaUiStructureTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void AgendaRunsOnlyAfterShellAndScratchpadInitialization()
    {
        var app = File.ReadAllText(Path.Combine(Root, "App.xaml.cs"));
        var shellWindow = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml.cs"));

        var initialize = app.IndexOf("await shellVm.InitializeAsync()", StringComparison.Ordinal);
        var show = app.IndexOf("shellWindow.Show()", initialize, StringComparison.Ordinal);
        var loaded = shellWindow.IndexOf("Loaded += async", StringComparison.Ordinal);
        var launch = shellWindow.IndexOf(
            "await _dailyAgendaLauncher.TryShowAsync(this, _shellViewModel)",
            loaded,
            StringComparison.Ordinal);

        Assert.True(initialize >= 0 && show > initialize);
        Assert.True(loaded >= 0 && launch > loaded);
    }

    [Fact]
    public void AgendaWindowCarriesThemeDemoKeyboardAndAutomationRequirements()
    {
        var view = File.ReadAllText(Path.Combine(Root, "Views", "DailyAgendaWindow.xaml"));

        Assert.DoesNotContain("Color=\"#", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Background=\"#", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{DynamicResource WindowBackgroundBrush}", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EnvironmentLabel", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Daily agenda items\"", view, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsSelected, Mode=TwoWay}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void AgendaWindowRendersAgainstOppositeLightAndDarkThemes()
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
                foreach (var theme in new[] { "PearlescentCream", "MidnightOpal" })
                {
                    dictionaries[themeIndex] = new ResourceDictionary
                    {
                        Source = new Uri($"/Sati;component/Themes/{theme}.xaml", UriKind.Relative)
                    };
                    var window = new DailyAgendaWindow();
                    WpfUiHarness.Realize(window, 860, 720);

                    var expected = Assert.IsAssignableFrom<Brush>(
                        Application.Current.FindResource("WindowBackgroundBrush"));
                    Assert.Same(expected, window.Background);
                }
            }
            finally
            {
                dictionaries[themeIndex] = originalTheme;
            }
        });
    }
}
