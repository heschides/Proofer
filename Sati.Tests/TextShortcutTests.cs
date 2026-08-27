using Sati.Data;
using Sati.Services;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class TextShortcutTests
{
    [Fact]
    public async Task ShortcutsAreSeparatedBySatiUserAndEnvironment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Sati.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "text-shortcuts.json");
        try
        {
            var production = Service(SatiDataEnvironment.Production, path);
            await production.LoadForUserAsync(11);
            await production.SaveForUserAsync(11, Values("Production user 11"));

            await production.LoadForUserAsync(12);
            Assert.All(production.GetActiveTexts(), Assert.Empty);
            await production.SaveForUserAsync(12, Values("Production user 12"));

            await production.LoadForUserAsync(11);
            Assert.Equal("Production user 11", production.GetTextForDigit(1));

            var demo = Service(SatiDataEnvironment.Demo, path);
            await demo.LoadForUserAsync(11);
            Assert.All(demo.GetActiveTexts(), Assert.Empty);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ServiceRejectsShortcutTextOverTwoHundredCharacters()
    {
        var path = Path.Combine(Path.GetTempPath(), "Sati.Tests", $"{Guid.NewGuid():N}.json");
        var service = Service(SatiDataEnvironment.Demo, path);
        var values = Values(string.Empty);
        values[0] = new string('x', TextShortcutService.MaximumTextLength + 1);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveForUserAsync(11, values));

        Assert.Contains("200", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertionReplacesTheSelectionAndLeavesTheCaretAfterTheSnippet()
    {
        WpfUiHarness.Run(() =>
        {
            var textBox = new TextBox { Text = "Start end" };
            TextShortcutTarget.SetIsEnabled(textBox, true);
            textBox.Select(6, 3);

            Assert.True(TextShortcutTarget.TryInsert(textBox, "middle"));
            Assert.Equal("Start middle", textBox.Text);
            Assert.Equal(textBox.Text.Length, textBox.CaretIndex);
            Assert.Equal(0, textBox.SelectionLength);
        });
    }

    [Fact]
    public void InsertionRefusesUnmarkedOrReadOnlyTextBoxes()
    {
        WpfUiHarness.Run(() =>
        {
            var unmarked = new TextBox { Text = "Keep" };
            Assert.False(TextShortcutTarget.TryInsert(unmarked, " changed"));
            Assert.Equal("Keep", unmarked.Text);

            var readOnly = new TextBox { Text = "Keep", IsReadOnly = true };
            TextShortcutTarget.SetIsEnabled(readOnly, true);
            Assert.False(TextShortcutTarget.TryInsert(readOnly, " changed"));
            Assert.Equal("Keep", readOnly.Text);
        });
    }

    [Fact]
    public void OnlyNumberRowDigitsMapToShortcutNumbers()
    {
        Assert.True(TextShortcutHook.TryMapDigit(0x31, out var one));
        Assert.Equal(1, one);
        Assert.True(TextShortcutHook.TryMapDigit(0x30, out var zero));
        Assert.Equal(0, zero);
        Assert.False(TextShortcutHook.TryMapDigit(0x61, out _)); // Number-pad 1.
    }

    [Fact]
    public void OnlyTheNoteNarrativeAndTwoScratchpadEditorsOptIntoInsertion()
    {
        var views = Path.Combine(RepositoryRoot(), "Views");
        var targets = Directory.GetFiles(views, "*.xaml")
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Count = File.ReadAllText(path).Split(
                    "TextShortcutTarget.IsEnabled=\"True\"",
                    StringSplitOptions.None).Length - 1
            })
            .Where(item => item.Count > 0)
            .ToList();

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, item => item.Name == "NoteEntryView.xaml" && item.Count == 1);
        Assert.Contains(targets, item => item.Name == "ScratchpadView.xaml" && item.Count == 2);
    }

    private static TextShortcutService Service(SatiDataEnvironment environment, string path) =>
        new(new DataEnvironmentInfo(environment, environment.ToString()), path);

    private static string[] Values(string first)
    {
        var values = new string[TextShortcutService.ShortcutCount];
        values[0] = first;
        return values;
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
