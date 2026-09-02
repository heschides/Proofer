using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Where the Credible import controls sit in <c>ClientsView</c>.
///
/// <para>
/// This exists because the import button shipped invisible. It was placed in the entry-form
/// Border at the top of the file — which lives in a <c>ColumnDefinition Width="0"</c> and renders
/// nothing — rather than in the Add Client form the user actually sees. Every unit and render
/// test still passed: <c>ConsumerImportViewRenderTests</c> renders the panel in isolation with a
/// DataContext, so it proves the panel works and says nothing about whether anything can reach
/// it.
/// </para>
///
/// <para>
/// A structural assertion rather than a render test because rendering the whole of
/// <c>ClientsView</c> needs the full view model graph. This is narrower, but it pins the exact
/// thing that broke: the import button must sit beside the button that saves.
/// </para>
/// </summary>
public sealed class CredibleImportPlacementTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(CredibleImportPlacementTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    private static XDocument ClientsView() =>
        XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

    [Fact]
    public void TheImportButtonSitsInTheSameActionRowAsTheSaveButton()
    {
        var document = ClientsView();

        var import = Single(document, "Button", "Command", "ConsumerImport.ChooseFileCommand");
        var save = Single(document, "Button", "Command", "SubmitCommand");

        // Same immediate parent: whatever container holds the button that writes a consumer is
        // by definition on screen whenever the form is.
        Assert.Same(save.Parent, import.Parent);
    }

    // The specific mistake. That Border sits in a zero-width column, so anything inside it is
    // laid out and never seen.
    [Fact]
    public void TheImportControlsAreNotInTheZeroWidthEntryPanel()
    {
        var document = ClientsView();

        var entryPanel = EntryPanel(document);

        foreach (var element in ImportElements(document))
            Assert.DoesNotContain(entryPanel, element.Ancestors());
    }

    [Fact]
    public void TheReviewPanelAndItsRefusalMessageAreBothPresent()
    {
        var document = ClientsView();

        Assert.Single(document.Descendants(Presentation + "ContentControl").Where(element =>
            element.Attribute("Content")?.Value.Contains("Binding ConsumerImport}") == true));

        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Visibility")?.Value.Contains("ConsumerImport.HasRefusal") == true);
    }

    // Existing-profile import is agency-disabled by default. The view model's policy property
    // keeps creation available while exposing edit-mode import only after the setting is enabled.
    [Fact]
    public void TheImportButtonUsesTheDefaultOffExistingProfilePolicy()
    {
        var import = Single(ClientsView(), "Button", "Command", "ConsumerImport.ChooseFileCommand");

        var visibility = import.Attribute("Visibility")?.Value;

        Assert.NotNull(visibility);
        Assert.Contains("CanImportCredibleIntoCurrentForm", visibility);
        Assert.Contains("BoolToVisibilityConverter", visibility);
    }

    // The visible label is deliberately short, so the accessible name is what actually says
    // what the button does. It is not decoration.
    [Fact]
    public void TheImportButtonCarriesAnAccessibleName()
    {
        var import = Single(ClientsView(), "Button", "Command", "ConsumerImport.ChooseFileCommand");

        var accessibleName = import.Attribute("AutomationProperties.Name")?.Value;

        Assert.False(string.IsNullOrWhiteSpace(accessibleName));
        Assert.Contains("Credible", accessibleName);
    }

    // The three buttons share one row with the "required to save" panel. Without an explicit
    // height they stretch to that panel, which is what made them read as a wall of blocks.
    [Fact]
    public void TheActionButtonsAreGivenAnExplicitHeightRatherThanStretching()
    {
        var document = ClientsView();

        var style = document.Descendants(Presentation + "Style").Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "FormActionButton"));

        Assert.Contains(style.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Height");
        Assert.Contains(style.Descendants(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "VerticalAlignment" &&
            setter.Attribute("Value")?.Value == "Top");
    }

    // Only the button that writes a record carries the accent, so the other two read as the
    // alternatives they are rather than three equal choices.
    [Fact]
    public void OnlyTheSaveButtonIsStyledAsPrimary()
    {
        var document = ClientsView();

        var import = Single(document, "Button", "Command", "ConsumerImport.ChooseFileCommand");
        var cancel = Single(document, "Button", "Command", "CancelClientEditCommand");
        var save = Single(document, "Button", "Command", "SubmitCommand");

        Assert.Equal("{StaticResource FormActionButton}", import.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource FormActionButton}", cancel.Attribute("Style")?.Value);
        Assert.Equal("{StaticResource FormActionPrimaryButton}", save.Attribute("Style")?.Value);
    }

    private static IEnumerable<XElement> ImportElements(XDocument document) =>
        document.Descendants().Where(element =>
            element.Attributes().Any(attribute =>
                attribute.Value.Contains("ConsumerImport", StringComparison.Ordinal)));

    /// <summary>
    /// The Border of the legacy entry form — the one in a <c>ColumnDefinition Width="0"</c>.
    /// </summary>
    private static XElement EntryPanel(XDocument document) =>
        document.Descendants(Presentation + "Border").First(border =>
            border.Attribute("Visibility")?.Value.Contains("IsEntryPanelOpen") == true);

    /// <summary>
    /// Finds the one matching element in the form the user can actually see.
    ///
    /// <para>
    /// Scoped past the legacy entry panel because that panel is a near-complete duplicate of the
    /// client form — its own save button, its own save-error banner — so an unscoped search finds
    /// two of everything. That duplication is the reason the import button went in the wrong
    /// place to begin with.
    /// </para>
    /// </summary>
    private static XElement Single(
        XDocument document, string elementName, string attributeName, string contains)
    {
        var deadPanel = EntryPanel(document);

        var matches = document.Descendants(Presentation + elementName)
            .Where(element =>
                element.Attribute(attributeName)?.Value.Contains(contains, StringComparison.Ordinal)
                    == true &&
                !element.Ancestors().Contains(deadPanel))
            .ToList();

        return Assert.Single(matches);
    }
}
