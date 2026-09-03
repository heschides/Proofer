using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class SafetyPlanRulesTests
{
    [Fact]
    public void Complete_shared_structure_can_be_submitted()
    {
        var document = new SafetyPlanDocument(SafetyPlanRules.SchemaVersion,
            SafetyPlanRules.SectionIds.Select(id => new SafetyPlanSection(id, "Reviewed content.")).ToList());

        var errors = SafetyPlanRules.Validate(System.Text.Json.JsonSerializer.Serialize(document), requireComplete: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void Incomplete_plan_cannot_be_submitted()
    {
        var errors = SafetyPlanRules.Validate(SafetyPlanRules.EmptyDocumentJson(), requireComplete: true);

        Assert.Contains("document", errors.Keys);
    }

    [Fact]
    public void Only_non_draft_safety_plan_artifact_satisfies_attestation()
    {
        var date = new DateTime(2026, 1, 1);
        var draft = new ArtifactFact(1, 1, "SafetyPlan", date, true);
        var final = new ArtifactFact(2, 1, "SafetyPlan", date, false);

        Assert.False(FormAttestationRules.Evaluate("SafetyPlan", date, date, date, AttestationActorKind.CaseManager, [draft]).Accepted);
        Assert.True(FormAttestationRules.Evaluate("SafetyPlan", date, date, date, AttestationActorKind.CaseManager, [final]).Accepted);
    }
}
