namespace Sati.Contracts.V1;

/// <summary>What matching a legacy free-text provider name against the directory produced.</summary>
public enum LegacyMatchOutcome
{
    /// <summary>Nothing was ever typed. Not a gap — most consumers legitimately have none.</summary>
    NoLegacyValue,

    /// <summary>Something was typed and nothing in the directory carries that name.</summary>
    NoMatch,

    /// <summary>
    /// More than one directory entry carries that name. Deliberately not resolved: picking one
    /// would silently attach a consumer to whichever row happened to sort first.
    /// </summary>
    Ambiguous,

    /// <summary>Exactly one directory entry carries that name.</summary>
    Matched
}

public readonly record struct LegacyProviderMatch(
    LegacyMatchOutcome Outcome,
    int ProviderId,
    string ProviderName,
    int CandidateCount)
{
    public bool CanLink => Outcome == LegacyMatchOutcome.Matched;
}

/// <summary>
/// Matching the free-text provider fields that predate the directory —
/// <c>Person.PrimaryCareProvider</c> and <c>Person.HealthcareSystemName</c> — against directory
/// entries, so a case manager can link them one at a time.
/// <para>
/// The matching is <b>exact after trimming, case-insensitive, and nothing else</b>. No edit
/// distance, no token overlap, no "starts with". Those would let "Dr. Reed" attach to "Dr. Reedy",
/// and a wrong provider on a consumer's medical record is worse than an unlinked one — an unlinked
/// value is visibly unfinished, a wrong link looks finished.
/// </para>
/// <para>
/// Nothing here writes. It proposes, and a person confirms. A bulk automatic backfill over live
/// consumer records is exactly the operation that should not happen without review, and the legacy
/// string is never deleted either way: it is the only record of what someone actually typed.
/// </para>
/// </summary>
public static class LegacyProviderLinking
{
    /// <summary>
    /// Finds the directory entry a legacy name refers to, optionally restricted to one tier —
    /// a healthcare-system name should only ever match a network, never a clinician who happens
    /// to share the name.
    /// </summary>
    public static LegacyProviderMatch Match(
        string? legacyName,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory,
        MedicalProviderKind? requiredKind = null)
    {
        ArgumentNullException.ThrowIfNull(agencyDirectory);

        var name = legacyName?.Trim();
        if (string.IsNullOrEmpty(name))
            return new LegacyProviderMatch(LegacyMatchOutcome.NoLegacyValue, 0, string.Empty, 0);

        var candidates = agencyDirectory
            .Where(node => requiredKind is null || node.Kind == requiredKind)
            .Where(node => string.Equals(node.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.Count switch
        {
            0 => new LegacyProviderMatch(LegacyMatchOutcome.NoMatch, 0, name, 0),
            1 => new LegacyProviderMatch(
                LegacyMatchOutcome.Matched, candidates[0].Id, candidates[0].Name, 1),
            _ => new LegacyProviderMatch(LegacyMatchOutcome.Ambiguous, 0, name, candidates.Count)
        };
    }

    /// <summary>
    /// What to tell the case manager about an unlinked legacy primary care provider. Each outcome
    /// names the next action, because "not linked" on its own is a state, not a task.
    /// </summary>
    public static string PrimaryCareGuidance(LegacyProviderMatch match) => match.Outcome switch
    {
        LegacyMatchOutcome.Matched =>
            $"This profile still records \"{match.ProviderName}\" as free text. " +
            "Link it to the directory entry of the same name to pick up their practice and network.",
        LegacyMatchOutcome.NoMatch =>
            $"This profile records \"{match.ProviderName}\" as free text, and no directory entry " +
            "carries that name. Add them to the provider directory, then link them here.",
        LegacyMatchOutcome.Ambiguous =>
            $"This profile records \"{match.ProviderName}\" as free text, and {match.CandidateCount} " +
            "directory entries carry that name. Merge the duplicates in the directory first — " +
            "linking to one of them would be a guess.",
        _ => string.Empty
    };

    /// <summary>
    /// Whether the healthcare system typed on the profile still agrees with the network the
    /// linked provider actually resolves to. A disagreement is worth surfacing rather than
    /// silently preferring the derived value: one of the two is out of date and only a person
    /// knows which.
    /// </summary>
    public static string HealthcareSystemGuidance(string? legacySystemName, string? derivedNetworkName)
    {
        var legacy = legacySystemName?.Trim();
        if (string.IsNullOrEmpty(legacy))
            return string.Empty;

        var derived = derivedNetworkName?.Trim();
        if (string.IsNullOrEmpty(derived))
            return $"This profile records \"{legacy}\" as its healthcare system. Once a provider " +
                   "is linked, the network comes from the directory instead.";

        return string.Equals(legacy, derived, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"This profile records \"{legacy}\" as its healthcare system, but the linked provider " +
              $"belongs to \"{derived}\". Check which is current — the directory is what the rest " +
              "of Sati now uses.";
    }
}
