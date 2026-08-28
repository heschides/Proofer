namespace Sati.Contracts.V1;

/// <summary>
/// Curation of the shared agency provider directory: who may change what, when a new entry looks
/// like one that already exists, and what a merge of two entries is allowed to do.
/// <para>
/// The directory has always been agency-wide — <c>Provider.AgencyId</c> scopes every row to the
/// agency, not to the person who typed it. What it lacked was the housekeeping a shared pool
/// needs, which is what this owns.
/// </para>
/// </summary>
public static class ProviderDirectoryRules
{
    public const int ContactNameMaxLength = 150;
    public const int ContactRoleMaxLength = 100;
    public const int ContactPhoneMaxLength = 30;
    public const int ContactExtensionMaxLength = 10;
    public const int ContactEmailMaxLength = 254;
    public const int ContactSortOrderMax = 999;
    /// <summary>
    /// Anyone working a caseload can add and correct directory entries. The directory is only
    /// useful if the person on the phone with a new specialist can record them straight away, and
    /// a wrong entry is fixable.
    /// </summary>
    public static bool CanCreateOrEdit(string? role) =>
        role is "CaseManager" or "Supervisor" or "Director" or "Admin";

    /// <summary>
    /// Deleting and merging are Admin-only. Both destroy a row that other case managers'
    /// consumers, documents, and affiliations point at, and neither is undoable by the person who
    /// did it.
    /// </summary>
    public static bool CanDeleteOrMerge(string? role) => role is "Admin";

    public const string DeleteRequiresAdminMessage =
        "Only an agency Admin can remove a provider from the directory. Other case managers' " +
        "consumers may be linked to this entry.";

    public const string MergeRequiresAdminMessage =
        "Only an agency Admin can merge two directory entries.";

    /// <summary>
    /// Two names are "the same entry" for duplicate-warning purposes when they match after
    /// trimming, collapsing internal runs of whitespace, and ignoring case.
    /// <para>
    /// Deliberately not fuzzy. This decides what a human is warned about, so a false positive
    /// costs a moment's reading — but a matcher that flags every similar name trains people to
    /// dismiss the warning, which costs everything the warning was for.
    /// </para>
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return string.Join(' ', name.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static bool IsSameName(string? left, string? right) =>
        NormalizeName(left).Length > 0 &&
        string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How many existing entries already carry this name, excluding the one being edited.
    /// </summary>
    public static int CountSameName(
        string? name,
        int editingProviderId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        ArgumentNullException.ThrowIfNull(agencyDirectory);

        return agencyDirectory.Count(node =>
            node.Id != editingProviderId && IsSameName(name, node.Name));
    }

    /// <summary>
    /// The warning shown while a name is being typed, or empty when there is nothing to say.
    /// It <b>warns and does not block</b>: two genuinely different organizations can share a name,
    /// and refusing the save would make the directory unable to describe reality.
    /// </summary>
    public static string SameNameWarning(
        string? name,
        int editingProviderId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        var count = CountSameName(name, editingProviderId, agencyDirectory);
        if (count == 0)
            return string.Empty;

        var normalized = NormalizeName(name);
        var subject = count == 1 ? "An entry" : $"{count} entries";
        var verb = count == 1 ? "is" : "are";

        return $"{subject} in this agency's directory {verb} already named \"{normalized}\". " +
               "Check whether this is the same organization before adding another — a duplicate " +
               "splits the affiliation tree and the consumers linked to it.";
    }

    public static Dictionary<string, string[]> ValidateContact(SaveProviderContactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > ContactNameMaxLength)
            errors["name"] = [$"A contact needs a name of at most {ContactNameMaxLength} characters."];
        if (TrimmedLength(request.Role) > ContactRoleMaxLength)
            errors["role"] = [$"The role must not exceed {ContactRoleMaxLength} characters."];
        if (TrimmedLength(request.Phone) > ContactPhoneMaxLength)
            errors["phone"] = [$"The phone number must not exceed {ContactPhoneMaxLength} characters."];
        if (TrimmedLength(request.Extension) > ContactExtensionMaxLength)
            errors["extension"] = [$"The extension must not exceed {ContactExtensionMaxLength} characters."];
        var email = request.Email?.Trim();
        if (email?.Length > ContactEmailMaxLength)
            errors["email"] = [$"The email address must not exceed {ContactEmailMaxLength} characters."];
        else if (!string.IsNullOrEmpty(email) &&
                 !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            errors["email"] = ["Enter a valid email address, or leave it blank."];
        if (request.SortOrder < 0 || request.SortOrder > ContactSortOrderMax)
            errors["sortOrder"] = [$"The display order must be between 0 and {ContactSortOrderMax}."];
        return errors;
    }

    private static int TrimmedLength(string? value) => value?.Trim().Length ?? 0;

    // ── Merge ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether one directory entry may be merged into another. Returns null when it may, or the
    /// reason it may not.
    /// </summary>
    /// <param name="surviving">The entry that remains.</param>
    /// <param name="merged">The entry that is absorbed and then removed.</param>
    public static string? ValidateMerge(ProviderAffiliationNode surviving, ProviderAffiliationNode merged)
    {
        if (surviving.Id == merged.Id)
            return "Choose two different directory entries to merge.";

        if (surviving.Kind != merged.Kind)
            return "Two entries can only be merged when they are the same kind of provider — " +
                   "an individual with an individual, a practice with a practice, a network with " +
                   "a network.";

        // Merging a parent into its own descendant would leave the survivor as its own ancestor.
        // The caller resolves the chain; this states the rule.
        return null;
    }

    public const string MergeWouldCreateLoopMessage =
        "That merge would leave the surviving entry inside its own affiliation chain. " +
        "Move the affiliation first, then merge.";

    public static string MergeConsumerLinkConflictMessage(int count) =>
        $"The merge cannot continue because {count} {(count == 1 ? "consumer has" : "consumers have")} " +
        "current links to both entries. End or correct one of each consumer's duplicate links first.";

    /// <summary>
    /// Refused when both entries carry the same kind of durable identifier and the two disagree.
    /// Two different National Provider Identifiers is positive evidence these are two different
    /// organizations, which is the one thing a merge must not paper over.
    /// </summary>
    public static string ConflictingIdentifierMessage(string which) =>
        $"These entries have different {which} values, which means they are not the same " +
        "organization. Correct the identifier on whichever entry is wrong before merging.";

    /// <summary>
    /// What a completed merge did, so the Admin sees the consequences rather than a bare success.
    /// </summary>
    public static string MergeSummary(
        string survivingName,
        string mergedName,
        int affiliatedMoved,
        int consumerLinksMoved,
        int contactsMoved) =>
        $"\"{mergedName}\" was merged into \"{survivingName}\". " +
        $"Moved {affiliatedMoved} affiliated {(affiliatedMoved == 1 ? "entry" : "entries")}, " +
        $"{consumerLinksMoved} consumer {(consumerLinksMoved == 1 ? "link" : "links")}, and " +
        $"{contactsMoved} {(contactsMoved == 1 ? "contact" : "contacts")}. " +
        "Documents that already named the merged entry keep what they recorded.";
}
