namespace Sati.Contracts.V1;

public enum AttestationActorKind
{
    CaseManager,
    Supervisor,
    System
}

public enum PrerequisiteKind
{
    None,
    DocumentArtifact,
    ComprehensiveAssessment,
    SafetyPlan,
    PrivacyPracticesAcknowledgment
}

public sealed record ArtifactFact(
    int ArtifactId,
    int PersonId,
    string Kind,
    DateTime CycleStart,
    bool IsDraft,
    bool IsExternal = false);

public sealed record NoteFact(
    int NoteId,
    int PersonId,
    string FormType,
    DateTime EventDate,
    string Status);

public sealed record FormFact(
    int FormId,
    int PersonId,
    string FormType,
    DateTime DueDate,
    DateTime? CompletedDate);

public sealed record UnmetPrerequisite(PrerequisiteKind Kind, string Message);

public sealed record AttestationDecision(
    bool Accepted,
    string? DateError,
    IReadOnlyList<UnmetPrerequisite> UnmetPrerequisites,
    bool SupervisorOverrideAccepted = false);

public sealed record PendingAttestation(
    int FormId,
    int PersonId,
    string FormType,
    DateTime CycleStart,
    DateTime CycleEnd,
    DateTime DueDate,
    int EvidenceNoteId,
    DateTime EvidenceDate);

/// <summary>
/// Single owner of the rules that decide whether a compliance-form attestation is
/// legal and which form-note evidence is waiting for a human attestation.
/// </summary>
public static class FormAttestationRules
{
    public const string BeforeCycleMessage =
        "A form completion date cannot be before the compliance cycle began.";
    public const string NoPrerequisitesStateJson = "{\"prerequisiteArtifactIds\":[]}";

    public static AttestationDecision Evaluate(
        string formType,
        DateTime completedOn,
        DateTime cycleStart,
        DateTime today,
        AttestationActorKind actor,
        IReadOnlyCollection<ArtifactFact> artifactsForCycle,
        IReadOnlyCollection<FormFact>? formsForCycle = null,
        string? supervisorOverrideReason = null)
    {
        var dateError = FormCompletionRules.Validate(completedOn, today);
        if (dateError is null && completedOn.Date < cycleStart.Date)
            dateError = BeforeCycleMessage;

        var unmet = EvaluatePrerequisites(
            formType,
            cycleStart,
            artifactsForCycle,
            formsForCycle ?? []);
        var supervisorOverrideAccepted =
            unmet.Count > 0 &&
            actor == AttestationActorKind.Supervisor &&
            !string.IsNullOrWhiteSpace(supervisorOverrideReason);
        var prerequisitesAccepted =
            unmet.Count == 0 || actor == AttestationActorKind.System || supervisorOverrideAccepted;

        return new AttestationDecision(
            dateError is null && prerequisitesAccepted,
            dateError,
            unmet,
            supervisorOverrideAccepted);
    }

    public static IReadOnlyList<PendingAttestation> PendingAttestations(
        IReadOnlyCollection<NoteFact> notes,
        IReadOnlyCollection<FormFact> forms,
        DateTime? effectiveDate,
        DateTime today)
    {
        if (effectiveDate is null)
            return [];

        var eligibleNotes = notes
            .Where(note => note.EventDate.Date <= today.Date && IsEvidenceStatus(note.Status))
            .OrderByDescending(note => note.EventDate)
            .ThenByDescending(note => note.NoteId);

        var pending = new List<PendingAttestation>();
        foreach (var note in eligibleNotes)
        {
            var form = forms
                .Where(candidate =>
                    candidate.PersonId == note.PersonId &&
                    string.Equals(candidate.FormType, note.FormType, StringComparison.OrdinalIgnoreCase) &&
                    candidate.CompletedDate is null)
                .Select(candidate => new
                {
                    Form = candidate,
                    Cycle = ResolveCycle(effectiveDate.Value, candidate.DueDate)
                })
                .Where(candidate => candidate.Cycle is not null &&
                    candidate.Cycle.Value.CycleStart.Date <= note.EventDate.Date &&
                    note.EventDate.Date < candidate.Cycle.Value.CycleEnd.Date)
                .OrderByDescending(candidate => candidate.Form.DueDate)
                .FirstOrDefault();

            if (form is null || pending.Any(item => item.FormId == form.Form.FormId))
                continue;

            pending.Add(new PendingAttestation(
                form.Form.FormId,
                form.Form.PersonId,
                form.Form.FormType,
                form.Cycle!.Value.CycleStart,
                form.Cycle.Value.CycleEnd,
                form.Form.DueDate.Date,
                note.NoteId,
                note.EventDate.Date));
        }

        return pending;
    }

    public static PrerequisiteKind PrerequisiteFor(string formType) => formType switch
    {
        "Reclassification" => PrerequisiteKind.ComprehensiveAssessment,
        "SafetyPlan" => PrerequisiteKind.SafetyPlan,
        "PrivacyPractices" => PrerequisiteKind.PrivacyPracticesAcknowledgment,
        "Release_Agency" or "Release_DHHS" or "Release_Medical" => PrerequisiteKind.DocumentArtifact,
        _ => PrerequisiteKind.None
    };

    public static string PrerequisiteStateJson(
        AttestationDecision decision,
        IEnumerable<int> artifactIds,
        string? supervisorOverrideReason = null)
    {
        var ids = artifactIds.Distinct().Order().ToArray();
        if (ids.Length == 0 && !decision.SupervisorOverrideAccepted)
            return NoPrerequisitesStateJson;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            prerequisiteArtifactIds = ids,
            supervisorOverride = decision.SupervisorOverrideAccepted,
            supervisorOverrideReason = decision.SupervisorOverrideAccepted
                ? supervisorOverrideReason?.Trim()
                : null
        });
    }

    public static (DateTime CycleStart, DateTime CycleEnd)? ResolveCycle(
        DateTime effectiveDate,
        DateTime formDueDate)
    {
        var approximateYear = formDueDate.Year - effectiveDate.Year;
        for (var offset = approximateYear - 1; offset <= approximateYear + 1; offset++)
        {
            var cycleStart = effectiveDate.AddYears(offset).Date;
            var cycleEnd = effectiveDate.AddYears(offset + 1).Date;
            if (formDueDate.Date > cycleStart && formDueDate.Date <= cycleEnd)
                return (cycleStart, cycleEnd);
        }

        return null;
    }

    private static bool IsEvidenceStatus(string status) =>
        status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Logged", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<UnmetPrerequisite> EvaluatePrerequisites(
        string formType,
        DateTime cycleStart,
        IReadOnlyCollection<ArtifactFact> artifactsForCycle,
        IReadOnlyCollection<FormFact> formsForCycle)
    {
        var prerequisite = PrerequisiteFor(formType);
        if (prerequisite == PrerequisiteKind.None)
            return [];

        if (prerequisite == PrerequisiteKind.ComprehensiveAssessment)
        {
            var cycleEnd = cycleStart.AddYears(1);
            var completed = formsForCycle.Any(form =>
                form.FormType.Equals("ComprehensiveAssessment", StringComparison.OrdinalIgnoreCase) &&
                form.DueDate.Date > cycleStart.Date &&
                form.DueDate.Date <= cycleEnd.Date &&
                form.CompletedDate is not null);
            return completed
                ? []
                : [new(prerequisite, "A Comprehensive Assessment for this compliance cycle must be attested first.")];
        }

        var catalogEntry = AnnualDocumentCatalog.ForFormType(formType);
        if (catalogEntry is null)
            return [new(prerequisite, "The required supporting document is not available.")];

        var matching = artifactsForCycle.Where(artifact =>
            artifact.CycleStart.Date == cycleStart.Date &&
            artifact.Kind.Equals(catalogEntry.Kind.ToString(), StringComparison.OrdinalIgnoreCase));

        var satisfied = prerequisite switch
        {
            PrerequisiteKind.DocumentArtifact => matching.Any(artifact => !artifact.IsDraft),
            // A generated safety plan is evidence only after its supervisor approval.
            // The rendering endpoint records unapproved plans as Draft artifacts.
            PrerequisiteKind.SafetyPlan => matching.Any(artifact => !artifact.IsDraft),
            // Privacy acknowledgement remains external until its dedicated acknowledgement step.
            PrerequisiteKind.PrivacyPracticesAcknowledgment =>
                matching.Any(artifact => !artifact.IsDraft && artifact.IsExternal),
            _ => false
        };

        return satisfied
            ? []
            : [new(prerequisite, $"{catalogEntry.DisplayName} must be prepared or recorded as external before attestation.")];
    }
}
