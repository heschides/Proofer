using Microsoft.Extensions.Options;
using Sati.Models;
using Sati.Services.LocalAi;
using Xunit;
using Xunit.Abstractions;

namespace Sati.Tests;

/// <summary>
/// Opt-in device/model release gate. Ordinary CI proves the deterministic security and grounding
/// contract without acquiring model weights. Setting SATI_RUN_LOCAL_AI_MODEL_EVAL=1 explicitly
/// authorizes Foundry Local initialization and any first-use multi-gigabyte model download.
/// </summary>
public sealed class LocalAiModelCompetenceTests(ITestOutputHelper output)
{
    [LocalAiModelFact]
    [Trait("Category", "LocalAiModelEvaluation")]
    public async Task ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs()
    {
        using var formatter = new FoundryLocalCaseNoteFormatter(Options.Create(new LocalAiOptions
        {
            Enabled = true,
            ModelAlias = "phi-4-mini",
            MaxInputWords = 500,
            MaxOutputTokens = 900,
            RulesFile = "AI_CASE_NOTE_RULES.md"
        }));

        var scenarios = new[]
        {
            CaseNoteFactCompiler.Build(
                91_001,
                """
                Phone call from Andrew and Rob. Rob stated transportation did not arrive.
                CCM called ModivCare about the standing order. Hanna asked for the schedule by email.
                Follow-up: CCM will confirm transportation Friday.
                """,
                NoteType.Contact,
                null,
                "Joshua White",
                "Andrew",
                null),
            CaseNoteFactCompiler.Build(
                91_002,
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
                        new VisitAttendeeSnapshot
                        {
                            FullName = "Robin Smith",
                            Role = "Shared Living Provider"
                        }
                    ]
                }),
            CaseNoteFactCompiler.Build(
                91_003,
                """
                Guardian Mia stated, "Transportation did not arrive."
                CCM called the provider at 2:30 PM.
                Follow-up: CCM will call Mia Friday.
                """,
                NoteType.Contact,
                null,
                "Joshua White",
                "Morgan",
                null)
        };

        var requestedScenario = Environment.GetEnvironmentVariable("SATI_LOCAL_AI_EVAL_SCENARIO");
        var selectedScenarios = int.TryParse(requestedScenario, out var personId)
            ? scenarios.Where(scenario => scenario.PersonId == personId).ToArray()
            : scenarios;
        Assert.NotEmpty(selectedScenarios);

        foreach (var scenario in selectedScenarios)
        {
            CaseNoteFormattingResult result;
            try
            {
                result = await formatter.FormatAsync(new CaseNoteFormattingRequest(
                    scenario.PersonId,
                    scenario.RawNarrative,
                    scenario.NoteType,
                    scenario.FormType,
                    scenario.CaseManagerFullName,
                    scenario.ConsumerFirstName,
                    scenario.Fingerprint,
                    scenario.Facts));
            }
            catch (CaseNoteDraftRejectedException exception)
            {
                Assert.Fail(
                    $"Scenario {scenario.PersonId} was rejected:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, exception.Errors));
                throw;
            }

            var requiredIds = scenario.Facts
                .Where(fact => fact.Required)
                .Select(fact => fact.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(requiredIds.IsSubsetOf(result.UsedFactIds));
            Assert.Equal(scenario.Fingerprint, result.SourceFingerprint);
            Assert.True(
                result.Warnings.Count == 0,
                $"Scenario {scenario.PersonId} used the deterministic fallback:{Environment.NewLine}" +
                string.Join(Environment.NewLine, result.Warnings));
            Assert.StartsWith(
                $"Community Case Manager (CCM) {scenario.CaseManagerFullName} ",
                result.DraftNarrative,
                StringComparison.Ordinal);
            Assert.Contains("Follow-up:", result.DraftNarrative, StringComparison.Ordinal);

            output.WriteLine($"Scenario {scenario.PersonId}:");
            output.WriteLine(result.DraftNarrative);
            output.WriteLine(string.Empty);
        }

    }
}

internal sealed class LocalAiModelFactAttribute : FactAttribute
{
    public LocalAiModelFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SATI_RUN_LOCAL_AI_MODEL_EVAL"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set SATI_RUN_LOCAL_AI_MODEL_EVAL=1 to authorize the on-device Foundry Local model evaluation.";
        }
    }
}
