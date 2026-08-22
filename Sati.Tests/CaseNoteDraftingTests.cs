using Sati.Contracts.V1;
using Sati.Models;
using Sati.Services.LocalAi;
using Xunit;

namespace Sati.Tests;

public sealed class CaseNoteDraftingTests
{
    [Fact]
    public void CompilerTurnsEverySelectedVisitControlIntoARequiredFact()
    {
        var visit = new VisitDocumentation
        {
            ConsumerPresent = true,
            Setting = VisitSetting.ConsumerHome,
            SettingDetails = "Living room",
            Appearance = VisitAppearance.NeatAndAppropriatelyDressed,
            Participation = VisitParticipation.ParticipatedWithSupport,
            SafetyObservation = VisitSafetyObservation.NoConcernsObserved,
            ExpressedPreferences = true,
            AskedQuestions = true,
            MadeChoices = true,
            CommunicationSupportUsed = true,
            GoalsReviewed = true,
            ServicesDiscussed = true,
            DocumentsReviewed = true,
            AdditionalAttendees = "Pat Lee, guardian",
            ObservationDetails = "Consumer pointed to the preferred schedule.",
            Attendees =
            [
                new VisitAttendeeSnapshot
                {
                    FullName = "Robin Smith",
                    Role = "Shared Living Provider",
                    Organization = "Example Supports"
                }
            ]
        };

        var snapshot = CaseNoteFactCompiler.Build(
            7,
            "Reviewed the weekly schedule; follow-up: send the revised copy.",
            NoteType.Visit,
            null,
            "Case Manager",
            "Alex",
            visit);

        var ids = snapshot.Facts.Where(fact => fact.Required).Select(fact => fact.Id).ToHashSet();
        Assert.Contains("RAW-001", ids);
        Assert.Contains("RAW-002", ids);
        Assert.Contains("VISIT-CONSUMER-PRESENCE", ids);
        Assert.Contains("VISIT-SETTING", ids);
        Assert.Contains("VISIT-SETTING-DETAIL", ids);
        Assert.Contains("VISIT-APPEARANCE", ids);
        Assert.Contains("VISIT-PARTICIPATION", ids);
        Assert.Contains("VISIT-SAFETY", ids);
        Assert.Contains("VISIT-ATTENDEE-001", ids);
        Assert.Contains("VISIT-ADDITIONAL-ATTENDEES", ids);
        Assert.Contains("VISIT-PREFERENCES", ids);
        Assert.Contains("VISIT-QUESTIONS", ids);
        Assert.Contains("VISIT-CHOICES", ids);
        Assert.Contains("VISIT-COMMUNICATION-SUPPORT", ids);
        Assert.Contains("VISIT-GOALS", ids);
        Assert.Contains("VISIT-SERVICES", ids);
        Assert.Contains("VISIT-DOCUMENTS", ids);
        Assert.Contains("VISIT-OBSERVATION-DETAIL", ids);
        Assert.True(snapshot.Facts.Single(fact => fact.Id == "RAW-002").Usage.HasFlag(CaseNoteFactUsage.FollowUp));
    }

    [Fact]
    public void CompilerOmitsUncheckedAndNotDocumentedControls()
    {
        var snapshot = CaseNoteFactCompiler.Build(
            7,
            "Brief check-in.",
            NoteType.Visit,
            null,
            "Case Manager",
            "Alex",
            new VisitDocumentation());

        var ids = snapshot.Facts.Select(fact => fact.Id).ToList();
        Assert.Equal(["RAW-001", CaseNoteDraftRules.NoFollowUpFactId], ids);
    }

    [Theory]
    [InlineData(true, "The consumer was present.")]
    [InlineData(false, "The consumer was not present.")]
    public void CompilerRepresentsEveryExplicitPresenceChoice(bool present, string expected)
    {
        var fact = CompileVisit(new VisitDocumentation { ConsumerPresent = present })
            .Single(item => item.Id == "VISIT-CONSUMER-PRESENCE");

        Assert.True(fact.Required);
        Assert.Equal(expected, fact.Text);
    }

    [Theory]
    [InlineData(VisitSetting.ConsumerHome, "consumer's home")]
    [InlineData(VisitSetting.Community, "community setting")]
    [InlineData(VisitSetting.AgencyOffice, "agency office")]
    [InlineData(VisitSetting.ProviderLocation, "provider location")]
    [InlineData(VisitSetting.Other, "another setting")]
    public void CompilerRepresentsEveryDocumentedSettingChoice(VisitSetting setting, string requiredValue)
    {
        var fact = CompileVisit(new VisitDocumentation { Setting = setting })
            .Single(item => item.Id == "VISIT-SETTING");

        Assert.Contains(requiredValue, fact.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requiredValue, fact.RequiredTerms!);
    }

    [Theory]
    [InlineData(VisitAppearance.NeatAndAppropriatelyDressed, "neat")]
    [InlineData(VisitAppearance.ConcernObserved, "appearance concern")]
    [InlineData(VisitAppearance.NotObserved, "not observed")]
    public void CompilerRepresentsEveryDocumentedAppearanceChoice(VisitAppearance appearance, string requiredValue)
    {
        var fact = CompileVisit(new VisitDocumentation { Appearance = appearance })
            .Single(item => item.Id == "VISIT-APPEARANCE");

        Assert.Contains(requiredValue, fact.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fact.RequiredTerms!, term => term.Contains(requiredValue, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(VisitParticipation.ParticipatedThroughout, "participated throughout")]
    [InlineData(VisitParticipation.ParticipatedWithSupport, "support")]
    [InlineData(VisitParticipation.LimitedParticipation, "limited")]
    [InlineData(VisitParticipation.Declined, "declined")]
    public void CompilerRepresentsEveryDocumentedParticipationChoice(
        VisitParticipation participation,
        string requiredValue)
    {
        var fact = CompileVisit(new VisitDocumentation { Participation = participation })
            .Single(item => item.Id == "VISIT-PARTICIPATION");

        Assert.Contains(requiredValue, fact.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fact.RequiredTerms!, term => term.Contains(requiredValue, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(VisitSafetyObservation.NoConcernsObserved, "No health or safety concerns")]
    [InlineData(VisitSafetyObservation.ConcernObserved, "A health or safety concern")]
    public void CompilerRepresentsEveryDocumentedSafetyChoice(
        VisitSafetyObservation safety,
        string expectedText)
    {
        var fact = CompileVisit(new VisitDocumentation { SafetyObservation = safety })
            .Single(item => item.Id == "VISIT-SAFETY");

        Assert.StartsWith(expectedText, fact.Text, StringComparison.Ordinal);
        Assert.True(fact.Required);
    }

    [Theory]
    [InlineData(VisitParticipation.NotAssessed)]
    [InlineData(VisitParticipation.NotDocumented)]
    public void CompilerDoesNotTurnNonFindingsIntoParticipationFacts(VisitParticipation participation)
    {
        var facts = CompileVisit(new VisitDocumentation { Participation = participation });

        Assert.DoesNotContain(facts, item => item.Id == "VISIT-PARTICIPATION");
    }

    [Theory]
    [InlineData(VisitSafetyObservation.NotAssessed)]
    [InlineData(VisitSafetyObservation.NotDocumented)]
    public void CompilerDoesNotTurnNonFindingsIntoSafetyFacts(VisitSafetyObservation safety)
    {
        var facts = CompileVisit(new VisitDocumentation { SafetyObservation = safety });

        Assert.DoesNotContain(facts, item => item.Id == "VISIT-SAFETY");
    }

    [Fact]
    public void CompilerMakesEveryRoughSentenceAndBulletARequiredFact()
    {
        var snapshot = CaseNoteFactCompiler.Build(
            7,
            "Called guardian.\n- Reviewed transportation; Consumer asked a question.\n* Follow-up: call Friday.",
            NoteType.Contact,
            null,
            "Case Manager",
            "Alex",
            null);

        var rawFacts = snapshot.Facts.Where(fact => fact.Category == "Rough note").ToList();
        Assert.Equal(4, rawFacts.Count);
        Assert.All(rawFacts, fact => Assert.True(fact.Required));
        Assert.Equal(
            ["Called guardian.", "Reviewed transportation", "Consumer asked a question.", "Follow-up: call Friday."],
            rawFacts.Select(fact => fact.Text));
    }

    [Fact]
    public void CompilerDoesNotMistakeTheScheduleNounForAFollowUpInstruction()
    {
        var snapshot = CaseNoteFactCompiler.Build(
            7,
            "Hanna asked for the schedule by email.",
            NoteType.Contact,
            null,
            "Case Manager",
            "Alex",
            null);

        var fact = snapshot.Facts.Single(item => item.Id == "RAW-001");
        Assert.Equal(CaseNoteFactUsage.Narrative, fact.Usage);
    }

    [Fact]
    public void FingerprintCoversEverySelectedClientAndTemplateInput()
    {
        var baseline = BuildFingerprint(new VisitDocumentation());
        var variants = new List<VisitDocumentation>
        {
            new() { ConsumerPresent = true },
            new() { Setting = VisitSetting.Community },
            new() { Appearance = VisitAppearance.ConcernObserved },
            new() { Participation = VisitParticipation.ParticipatedWithSupport },
            new() { SafetyObservation = VisitSafetyObservation.NoConcernsObserved },
            new() { ExpressedPreferences = true },
            new() { AskedQuestions = true },
            new() { MadeChoices = true },
            new() { CommunicationSupportUsed = true },
            new() { GoalsReviewed = true },
            new() { ServicesDiscussed = true },
            new() { DocumentsReviewed = true },
            new() { SettingDetails = "Library" },
            new() { ObservationDetails = "Consumer selected Tuesday." },
            new() { AdditionalAttendees = "Mia, guardian" },
            new()
            {
                Attendees =
                [
                    new VisitAttendeeSnapshot { FullName = "Robin Smith", Role = "Provider" }
                ]
            }
        };

        Assert.All(variants, visit => Assert.NotEqual(baseline, BuildFingerprint(visit)));
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            8, "Brief check-in.", NoteType.Visit, null, "Case Manager", "Alex", new VisitDocumentation()).Fingerprint);
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            7, "Different narrative.", NoteType.Visit, null, "Case Manager", "Alex", new VisitDocumentation()).Fingerprint);
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            7, "Brief check-in.", NoteType.Contact, null, "Case Manager", "Alex", null).Fingerprint);
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            7, "Brief check-in.", NoteType.Visit, FormType.PCP, "Case Manager", "Alex", new VisitDocumentation()).Fingerprint);
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            7, "Brief check-in.", NoteType.Visit, null, "Different Manager", "Alex", new VisitDocumentation()).Fingerprint);
        Assert.NotEqual(baseline, CaseNoteFactCompiler.Build(
            7, "Brief check-in.", NoteType.Visit, null, "Case Manager", "Taylor", new VisitDocumentation()).Fingerprint);
    }

    [Fact]
    public void GroundingRejectsAnOmittedRequiredFact()
    {
        var facts = Facts();
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("CCM documented that the consumer called CCM.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("RAW-002", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsARoughFragmentThatIsCitedButOnlyPartlyRepresented()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called about transportation.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("The consumer called.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("'transportation'", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsUnsupportedNamesNumbersAndUnknownFactIds()
    {
        var facts = Facts();
        var plan = new CaseNoteDraftPlan(
            [
                new CaseNoteDraftSentence(
                    "CCM spoke with Jordan for 45 minutes.",
                    ["RAW-001", "NOT-A-FACT"]),
                new CaseNoteDraftSentence("Transportation was discussed.", ["RAW-002"])
            ],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Jordan", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("45", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("NOT-A-FACT", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRequiresExplicitFollowUpInsteadOfTheFallbackWhenOneWasSupplied()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called CCM.", "Rough note", CaseNoteFactUsage.Narrative),
            new("RAW-002", "Follow-up: call on Friday.", "Rough note", CaseNoteFactUsage.Narrative | CaseNoteFactUsage.FollowUp),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [
                new CaseNoteDraftSentence("The consumer called CCM.", ["RAW-001"]),
                new CaseNoteDraftSentence("CCM documented a call on Friday.", ["RAW-002"])
            ],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cannot replace an explicit follow-up", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsASelectorThatIsCitedButItsValueIsOmitted()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer attended the meeting.", "Rough note", CaseNoteFactUsage.Narrative),
            new(
                "VISIT-PARTICIPATION",
                "The consumer participated with support.",
                "Participation selection",
                CaseNoteFactUsage.Narrative,
                RequiredTerms: ["participated", "support"]),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("The consumer attended and participated.", ["RAW-001", "VISIT-PARTICIPATION"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("did not retain 'support'", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsUnsupportedConclusionsAndNegation()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called CCM.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("The consumer called CCM and was not successful.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unsupported negation 'not'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("unsupported content word 'successful'", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingDoesNotTreatShortWordsAsAutomaticallySafe()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called CCM.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("The consumer was sad.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unsupported content word 'sad'", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsAnInventedChronologicalRelationship()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called CCM.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("After the consumer called CCM.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unsupported content word 'After'", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRejectsReversedParticipantAttribution()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Alice called Bob.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("CCM documented that Bob called Alice.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("changed attribution or meaning", StringComparison.Ordinal));
    }

    [Fact]
    public void GroundingRequiresAUserSuppliedQuotationToRemainAQuotation()
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Mia stated, \"Transportation did not arrive.\"", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence("CCM documented that Mia stated transportation did not arrive.", ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("preserve the supplied quotation", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("consumer called CCM.", "does not begin professionally")]
    [InlineData("The consumer called CCM", "not a complete punctuated sentence")]
    [InlineData("The consumer called\nCCM.", "unexpected line break")]
    public void GroundingRejectsUnprofessionalSentenceStructure(string sentence, string expectedError)
    {
        var facts = new List<CaseNoteDraftFact>
        {
            new("RAW-001", "Consumer called CCM.", "Rough note", CaseNoteFactUsage.Narrative),
            new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
        };
        var plan = new CaseNoteDraftPlan(
            [new CaseNoteDraftSentence(sentence, ["RAW-001"])],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var result = CaseNoteDraftRules.Validate(facts, plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidGroundedPlanRendersTheRequiredEnvelope()
    {
        var facts = Facts();
        var plan = new CaseNoteDraftPlan(
            [
                new CaseNoteDraftSentence("CCM documented that the consumer called CCM for 15 minutes.", ["RAW-001"]),
                new CaseNoteDraftSentence("Transportation was discussed.", ["RAW-002"])
            ],
            new CaseNoteDraftSentence(CaseNoteDraftRules.NoFollowUpText, [CaseNoteDraftRules.NoFollowUpFactId]));

        var validation = CaseNoteDraftRules.Validate(facts, plan);
        var rendered = CaseNoteDraftRules.Render("Case Manager", plan);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.StartsWith("Community Case Manager (CCM) Case Manager", rendered, StringComparison.Ordinal);
        Assert.EndsWith("Follow-up: No follow-up was documented.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedStyleExamplePassesTheCompleteCompetenceGate()
    {
        var snapshot = CaseNoteFactCompiler.Build(
            7,
            """
            Phone call from Andrew and Rob. Rob stated transportation did not arrive.
            CCM called ModivCare about the standing order. Hanna asked for the schedule by email.
            Follow-up: CCM will confirm transportation Friday.
            """,
            NoteType.Contact,
            null,
            "Joshua White",
            "Andrew",
            null);
        var plan = new CaseNoteDraftPlan(
            [
                new CaseNoteDraftSentence(
                    "CCM documented a phone call from Consumer (Andrew) and Rob.",
                    ["RAW-001"]),
                new CaseNoteDraftSentence(
                    "Rob stated that transportation did not arrive.",
                    ["RAW-002"]),
                new CaseNoteDraftSentence(
                    "CCM called ModivCare about the standing order.",
                    ["RAW-003"]),
                new CaseNoteDraftSentence(
                    "Hanna asked for the schedule by email.",
                    ["RAW-004"])
            ],
            new CaseNoteDraftSentence(
                "CCM will confirm transportation Friday.",
                ["RAW-005"]));

        var validation = CaseNoteDraftRules.Validate(snapshot.Facts, plan, ["Joshua White", "Andrew"]);
        var rendered = CaseNoteDraftRules.Render("Joshua White", plan);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal(
            snapshot.Facts.Where(fact => fact.Required).Select(fact => fact.Id).Order(),
            validation.UsedFactIds.Where(id => id != CaseNoteDraftRules.NoFollowUpFactId).Order());
        Assert.StartsWith(
            "Community Case Manager (CCM) Joshua White documented a phone call",
            rendered,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "Follow-up: CCM will confirm transportation Friday.",
            rendered,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicFallbackIsGroundedForMixedVisitFactCategories()
    {
        var snapshot = CaseNoteFactCompiler.Build(
            7,
            "Consumer selected the morning schedule.",
            NoteType.Visit,
            null,
            "Joshua White",
            "Taylor",
            new VisitDocumentation
            {
                ConsumerPresent = true,
                Setting = VisitSetting.Community,
                Appearance = VisitAppearance.NeatAndAppropriatelyDressed,
                Participation = VisitParticipation.ParticipatedWithSupport,
                SafetyObservation = VisitSafetyObservation.NoConcernsObserved,
                AskedQuestions = true,
                MadeChoices = true,
                GoalsReviewed = true,
                ServicesDiscussed = true,
                ObservationDetails = "Consumer pointed to Tuesday.",
                Attendees =
                [
                    new VisitAttendeeSnapshot { FullName = "Robin Smith", Role = "Shared Living Provider" }
                ]
            });

        var plan = FoundryLocalCaseNoteFormatter.BuildSafeBaselinePlan(snapshot.Facts);
        var validation = CaseNoteDraftRules.Validate(snapshot.Facts, plan, ["Joshua White", "Taylor"]);
        var rendered = CaseNoteDraftRules.Render("Joshua White", plan);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Contains("morning schedule", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Robin Smith", rendered, StringComparison.Ordinal);
        Assert.EndsWith("Follow-up: No follow-up was documented.", rendered, StringComparison.Ordinal);
    }

    private static IReadOnlyList<CaseNoteDraftFact> Facts() =>
    [
        new("RAW-001", "Consumer called CCM for 15 minutes.", "Rough note", CaseNoteFactUsage.Narrative),
        new("RAW-002", "Transportation was discussed.", "Rough note", CaseNoteFactUsage.Narrative),
        new(CaseNoteDraftRules.NoFollowUpFactId, CaseNoteDraftRules.NoFollowUpText, "Safe fallback", CaseNoteFactUsage.FollowUp, false)
    ];

    private static IReadOnlyList<CaseNoteDraftFact> CompileVisit(VisitDocumentation visit) =>
        CaseNoteFactCompiler.Build(
            7,
            "Brief check-in.",
            NoteType.Visit,
            null,
            "Case Manager",
            "Alex",
            visit).Facts;

    private static string BuildFingerprint(VisitDocumentation visit) =>
        CaseNoteFactCompiler.Build(
            7,
            "Brief check-in.",
            NoteType.Visit,
            null,
            "Case Manager",
            "Alex",
            visit).Fingerprint;
}
