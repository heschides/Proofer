using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Children;
using Sati.Views;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class WorkAgendaViewRenderTests
{
    [Fact]
    public void AllFiveGroupsAndTheScratchpadRemainUsableAtEasyEyesWidth()
    {
        WpfUiHarness.Run(() =>
        {
            var model = new ScratchpadViewModel(null!, null!)
            {
                ScratchpadFontSize = 18,
                ScratchpadContent = "Call the housing office after lunch."
            };

            model.PaperworkItems.Add(Item(NoteType.Form, "Complete the overdue assessment", 1,
                FormType.ComprehensiveAssessment));
            model.PaperworkItems.Add(Item(NoteType.Form, "Complete the first 90-day review", 2,
                FormType.Q1R));
            model.VisitItems.Add(Item(NoteType.Visit, "Home visit", 3));
            model.CallItems.Add(Item(NoteType.Phone, "Call the guardian", 4));
            model.EmailItems.Add(Item(NoteType.Email, "Email the provider", 5));
            model.FreeformItems.Add(Item(NoteType.Other, "Check the authorization portal", 6));

            var view = new ScratchpadView { DataContext = model };
            WpfUiHarness.Realize(view, 360, 700);

            foreach (var name in new[]
                     {
                         "Scheduled paperwork",
                         "Scheduled visits",
                         "Scheduled calls",
                         "Scheduled emails",
                         "Scheduled freeform work"
                     })
            {
                var list = WpfUiHarness.FindByAutomationName<ListBox>(view, name);
                Assert.Equal(Visibility.Visible, list.Visibility);
                Assert.True(list.ActualWidth > 250);
            }

            var scheduled = WpfUiHarness.FindByAutomationName<ScrollViewer>(
                view, "Today's scheduled work by type");
            var scratchpad = WpfUiHarness.FindByAutomationName<TextBox>(
                view, "Today's Work freeform scratchpad");
            var starts = WpfUiHarness.Descendants(view)
                .OfType<Button>()
                .Where(button => Equals(button.Content, "Start"))
                .ToList();

            Assert.True(scheduled.ActualHeight <= 360);
            Assert.True(scheduled.ScrollableHeight > 0);
            Assert.True(scratchpad.ActualHeight >= 100);
            Assert.Equal(6, starts.Count);
            Assert.All(starts, button => Assert.True(button.ActualWidth > 0));
            Assert.Equal(
                "Unstructured notes for today. Scheduled client work is listed above.",
                AutomationProperties.GetHelpText(scratchpad));

            SavePreview(view);
        });
    }

    private static WorkAgendaItem Item(
        NoteType type,
        string narrative,
        int personId,
        FormType? formType = null)
    {
        var note = Note.Create(
            narrative,
            DateTime.Today,
            NoteStatus.Scheduled,
            15,
            personId,
            formType,
            type);
        var person = Person.Rehydrate(personId, 41);
        person.FirstName = $"Client {personId}";
        person.LastName = "Example";
        note.Person = person;
        return new WorkAgendaItem(note);
    }

    private static void SavePreview(FrameworkElement view)
    {
        if (Environment.GetEnvironmentVariable("SATI_WORK_AGENDA_QA_OUTPUT")
            is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(
            (int)view.ActualWidth,
            (int)view.ActualHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, "work-agenda-easy-eyes.png"));
        encoder.Save(output);
    }
}
