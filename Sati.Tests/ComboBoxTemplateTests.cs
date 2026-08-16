using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// App.xaml replaces the framework ComboBox template because the stock one paints
/// hard-coded near-white chrome that no theme can reach. That replacement has to
/// reproduce one subtlety exactly.
///
/// ComboBox writes SelectionBoxItem and SelectionBoxItemTemplate through read-only
/// property keys AFTER the control template is applied. A TemplateBinding does not
/// follow that later write, so the selection box silently falls back to the item's
/// ToString(): a record renders as "ThemeOption { DisplayName = ... }" instead of
/// the display member. An explicit ItemTemplate survives, which is what made the
/// defect look intermittent rather than total.
///
/// These properties must therefore be bound with RelativeSource TemplatedParent.
/// </summary>
public sealed class ComboBoxTemplateTests
{
    private static readonly string[] MustNotUseTemplateBinding =
    [
        "SelectionBoxItem",
        "SelectionBoxItemTemplate",
        "SelectionBoxItemStringFormat"
    ];

    [Fact]
    public void ComboBoxSelectionBoxUsesBindingsThatFollowLateWrites()
    {
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "App.xaml"));

        foreach (var property in MustNotUseTemplateBinding)
        {
            Assert.False(
                Regex.IsMatch(appXaml, $@"\{{\s*TemplateBinding\s+{Regex.Escape(property)}\s*\}}"),
                $"App.xaml binds {property} with TemplateBinding. ComboBox sets that property after " +
                "the template is applied, so a TemplateBinding never sees the value and the selection " +
                "box falls back to ToString(). Use " +
                $"{{Binding {property}, RelativeSource={{RelativeSource TemplatedParent}}}} instead.");
        }
    }

    [Fact]
    public void ComboBoxSelectionBoxStillBindsTheContentProperties()
    {
        // Guards the other direction: the properties must actually be bound. A
        // template that simply dropped them would pass the test above.
        var appXaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "App.xaml"));

        foreach (var property in MustNotUseTemplateBinding)
        {
            Assert.Matches(
                new Regex($@"\{{\s*Binding\s+{Regex.Escape(property)}\s*,\s*RelativeSource\s*=\s*\{{\s*RelativeSource\s+TemplatedParent\s*\}}\s*\}}"),
                appXaml);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
