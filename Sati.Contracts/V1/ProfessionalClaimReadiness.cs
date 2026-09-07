namespace Sati.Contracts.V1;

/// <summary>
/// The exact immutable and line-level values that must be capable of producing an 837P row.
/// Both local and API billing paths translate their persistence models into this shape.
/// </summary>
public sealed record ProfessionalClaimLineFacts(
    int LineId,
    DateTime DateOfService,
    string ProcedureCode,
    string? ProcedureModifier,
    decimal? Units,
    decimal ChargeAmount,
    string ClientMaineCareId,
    string RenderingProviderNpi,
    string DiagnosisCode,
    int PlaceOfService,
    string? ClaimSnapshotJson);

public sealed record ProfessionalClaimLineReadiness(
    int LineId,
    string ClientName,
    IReadOnlyList<string> Errors)
{
    public bool IsReady => Errors.Count == 0;
}

public sealed record ProfessionalClaimPeriodReadiness(
    IReadOnlyList<ProfessionalClaimLineReadiness> Lines,
    IReadOnlyList<string> PeriodErrors)
{
    public bool IsReady => PeriodErrors.Count == 0 && Lines.All(line => line.IsReady);

    public string ExplainFailure()
    {
        var messages = PeriodErrors
            .Concat(Lines.SelectMany(line => line.Errors))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return messages.Count == 0
            ? "The billing period is ready."
            : string.Join("; ", messages);
    }
}

/// <summary>
/// Single rule owner for the frozen claim data consumed by 837P generation. Candidate-note
/// checks happen before a claim exists; this check happens immediately after construction and
/// again at preview, submission, and generation so persisted legacy defects fail visibly.
/// </summary>
public static class ProfessionalClaimReadiness
{
    public static ProfessionalClaimPeriodReadiness EvaluatePeriod(
        int year,
        int month,
        IEnumerable<ProfessionalClaimLineFacts> facts)
    {
        var rows = facts.ToList();
        if (rows.Count == 0)
            return new([], ["The billing period has no claim lines."]);

        var evaluated = rows.Select(row => EvaluateLine(row, year, month)).ToList();
        var readableSnapshots = rows
            .Select(row => TryReadSnapshot(row.ClaimSnapshotJson))
            .Where(snapshot => snapshot is not null)
            .Cast<ProfessionalClaimSnapshot>()
            .ToList();

        if (readableSnapshots
            .Select(ProviderPayerIdentity)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any())
        {
            const string mixedMessage =
                "This period mixes different provider, submitter, or payer snapshots; rebuild its claim lines together.";
            evaluated = evaluated
                .Select(line => line with { Errors = line.Errors.Append(mixedMessage).ToList() })
                .ToList();
        }

        return new(evaluated, []);
    }

    private static ProfessionalClaimLineReadiness EvaluateLine(
        ProfessionalClaimLineFacts line,
        int year,
        int month)
    {
        var errors = new List<string>();
        ProfessionalClaimSnapshot? snapshot = null;
        try
        {
            snapshot = ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(exception.Message);
        }

        var clientName = snapshot is null
            ? "Unknown client"
            : $"{snapshot.SubscriberFirstName} {snapshot.SubscriberLastName}".Trim();

        if (line.DateOfService.Year != year || line.DateOfService.Month != month)
            errors.Add("Service date is outside this billing month.");
        if (!BillingRules.IsValidProcedureCode(line.ProcedureCode))
            errors.Add("Procedure code is missing or invalid.");
        if (!BillingRules.IsValidModifier(line.ProcedureModifier))
            errors.Add("Procedure modifier is invalid.");
        if (!BillingRules.IsValidDiagnosisCode(line.DiagnosisCode))
            errors.Add("Diagnosis code is missing or invalid.");
        if (line.PlaceOfService is < 1 or > 99)
            errors.Add("Place of service must be between 1 and 99.");
        if (line.Units is null or <= 0)
            errors.Add("Units must be greater than zero.");
        if (line.ChargeAmount <= 0)
            errors.Add("Charge must be greater than $0.");

        if (snapshot is not null)
        {
            if (!HasValidProviderSnapshot(snapshot))
                errors.Add("Frozen provider, submitter, or payer details are incomplete or invalid.");
            if (!HasValidSubscriberSnapshot(snapshot))
                errors.Add("Frozen client identity or billing address is incomplete or invalid.");
            if (!string.Equals(line.ClientMaineCareId, snapshot.SubscriberMemberId, StringComparison.Ordinal) ||
                !string.Equals(line.RenderingProviderNpi, snapshot.BillingProviderNpi, StringComparison.Ordinal))
                errors.Add("Claim identifiers do not match the frozen billing snapshot.");
        }

        return new(line.LineId, clientName, errors.Distinct(StringComparer.Ordinal).ToList());
    }

    private static ProfessionalClaimSnapshot? TryReadSnapshot(string? json)
    {
        try
        {
            return ProfessionalClaimSnapshotCodec.Deserialize(json);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasValidProviderSnapshot(ProfessionalClaimSnapshot snapshot) =>
        BillingRules.IsSafeX12Element(snapshot.BillingProviderName, 60) &&
        BillingRules.IsValidNpi(snapshot.BillingProviderNpi) &&
        BillingRules.IsSafeX12Element(snapshot.BillingProviderTaxId, 50) &&
        BillingRules.IsSafeX12Element(snapshot.BillingProviderStreet, 55) &&
        BillingRules.IsSafeX12Element(snapshot.BillingProviderCity, 30) &&
        BillingRules.IsSafeX12Element(snapshot.BillingProviderState, 2) &&
        BillingRules.IsSafeX12Element(snapshot.BillingProviderZip, 15) &&
        BillingRules.IsSafeX12Element(snapshot.SubmitterId, 15) &&
        BillingRules.IsSafeX12Element(snapshot.PayerName, 60) &&
        BillingRules.IsSafeX12Element(snapshot.PayerId, 80) &&
        BillingRules.IsSafeX12Element(snapshot.SubmitterContactName, 60) &&
        !string.IsNullOrWhiteSpace(snapshot.SubmitterContactPhone) &&
        snapshot.SubmitterContactPhone.Length is >= 10 and <= 15 &&
        snapshot.SubmitterContactPhone.All(char.IsDigit);

    private static bool HasValidSubscriberSnapshot(ProfessionalClaimSnapshot snapshot) =>
        BillingRules.IsSafeX12Element(snapshot.SubscriberFirstName, 35) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberLastName, 60) &&
        snapshot.SubscriberBirthDate >= new DateTime(1900, 1, 1) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberMemberId, 80) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberStreet, 55) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberCity, 30) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberState, 2) &&
        BillingRules.IsSafeX12Element(snapshot.SubscriberZip, 15) &&
        snapshot.SubscriberGenderCode is "M" or "F" or "U";

    private static string ProviderPayerIdentity(ProfessionalClaimSnapshot snapshot) =>
        string.Join('\u001f', snapshot.AgencyId, snapshot.BillingProviderNpi,
            snapshot.SubmitterId, snapshot.PayerId);
}
