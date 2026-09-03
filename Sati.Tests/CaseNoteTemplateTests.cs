using Sati.Models;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class CaseNoteTemplateTests
{
    private static VisitDocumentation DocumentedMeeting() => new()
    {
        ConsumerPresent = true,
        Settings = [VisitSetting.ConsumerHome],
        Appearances = [VisitAppearance.NeatAndAppropriatelyDressed],
        Participations = [VisitParticipation.ParticipatedThroughout],
        SafetyObservations = [VisitSafetyObservation.NoConcernsObserved],
        GoalsReviewed = true,
        ServicesDiscussed = true,
        SettingDetails = "Kitchen table.",
        ObservationDetails = "Apartment was clean and heated.",
        Attendees = [new VisitAttendeeSnapshot { FullName = "Dana Reed", Role = "Guardian" }]
    };

    [Fact]
    public void TemplateGroupsTheCheckedFactsUnderProfessionalHeadings()
    {
        var template = CaseNoteTemplateComposer.Compose(DocumentedMeeting());

        Assert.Contains("MEETING DETAILS", template);
        Assert.Contains("OBSERVATIONS", template);
        Assert.Contains("DISCUSSION AND ACTIVITY", template);

        // Ordering matters: a reader should meet the setting before the observations.
        Assert.True(template.IndexOf("MEETING DETAILS", StringComparison.Ordinal)
            < template.IndexOf("OBSERVATIONS", StringComparison.Ordinal));
        Assert.True(template.IndexOf("OBSERVATIONS", StringComparison.Ordinal)
            < template.IndexOf("DISCUSSION AND ACTIVITY", StringComparison.Ordinal));

        Assert.Contains("Dana Reed", template);
        Assert.Contains("Kitchen table.", template);
        Assert.Contains("Apartment was clean and heated.", template);
        Assert.DoesNotContain(CaseNoteTemplateComposer.NarrativeHeader, template);
    }

    [Fact]
    public void AnUntouchedMeetingSectionProducesNoTemplate()
    {
        Assert.False(CaseNoteTemplateComposer.HasContent(null));
        Assert.False(CaseNoteTemplateComposer.HasContent(new VisitDocumentation()));
        Assert.Equal(string.Empty, CaseNoteTemplateComposer.Compose(new VisitDocumentation()));
    }

    [Fact]
    public void ExistingNarrativeIsKeptVerbatimBelowTheHeader()
    {
        var existing = "Dana asked about transportation." + Environment.NewLine
            + Environment.NewLine
            + "She will call the broker herself.";

        var merged = CaseNoteTemplateComposer.Merge(
            CaseNoteTemplateComposer.Compose(DocumentedMeeting()),
            existing);

        var headerIndex = merged.IndexOf(CaseNoteTemplateComposer.NarrativeHeader, StringComparison.Ordinal);
        Assert.True(headerIndex > 0);

        // Every character of the original text survives, in order, after the header.
        Assert.Contains(existing, merged[headerIndex..]);
        Assert.True(merged.IndexOf("MEETING DETAILS", StringComparison.Ordinal) < headerIndex);
    }

    [Fact]
    public void TheHeaderIsWrittenEvenWhenTheNarrativeIsEmpty()
    {
        var merged = CaseNoteTemplateComposer.Merge(
            CaseNoteTemplateComposer.Compose(DocumentedMeeting()),
            null);

        Assert.EndsWith(CaseNoteTemplateComposer.NarrativeHeader, merged);
    }

    [Fact]
    public void RunningItTwiceStacksRatherThanDiscardingEarlierWork()
    {
        var template = CaseNoteTemplateComposer.Compose(DocumentedMeeting());
        var once = CaseNoteTemplateComposer.Merge(template, "Original observation.");
        var twice = CaseNoteTemplateComposer.Merge(template, once);

        // The deliberate trade: the command never removes text, so a second press
        // nests instead of replacing. Nothing the case manager wrote is lost.
        Assert.Contains("Original observation.", twice);
        Assert.Equal(2, twice.Split(CaseNoteTemplateComposer.NarrativeHeader).Length - 1);
    }

    [Fact]
    public void TemplateWordingComesFromTheDraftCompilerNotASecondCopy()
    {
        var visit = DocumentedMeeting();
        var template = CaseNoteTemplateComposer.Compose(visit);

        foreach (var fact in Sati.Services.LocalAi.CaseNoteFactCompiler.VisitFacts(visit))
            Assert.Contains(fact.Text.Trim(), template);
    }
}
