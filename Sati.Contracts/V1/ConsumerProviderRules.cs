namespace Sati.Contracts.V1;

/// <summary>
/// The single owner of what a consumer's provider list will accept: field validity, the
/// at-most-one-primary-care rule, the one-current-link-per-provider rule, and the order
/// the list reads in.
/// <para>
/// There is deliberately <b>no product limit</b> on how many providers a consumer may
/// have. An eight-row cap was considered and rejected: it is a document constraint wearing
/// the costume of a data rule, and a medically complex consumer with eleven specialists
/// would have the eleventh recorded nowhere. Where a form has a fixed number of rows, the
/// form takes that many in the case manager's explicit order.
/// <see cref="MaxProvidersPerConsumer"/> exists only as a runaway guard and no real
/// workflow should ever reach it.
/// </para>
/// <para>
/// Tidiness comes from state instead: a link that has ended keeps its row and drops out of
/// the current list. See DECISIONS.md, "A consumer's provider list stores the link, never
/// the resolved chain".
/// </para>
/// </summary>
public static class ConsumerProviderRules
{
    /// <summary>
    /// A guard against runaway entry, not a clinical judgement about how many providers a
    /// person may have. Reaching it means something went wrong with data entry.
    /// </summary>
    public const int MaxProvidersPerConsumer = 50;

    public const int MaxRoleLength = 80;
    public const int MaxSortOrder = 999;

    /// <summary>
    /// Whether a link is part of the consumer's current care. Named here rather than
    /// written as <c>EndDate is null</c> in five places, so "current" cannot come to mean
    /// two different things.
    /// </summary>
    public static bool IsCurrent(DateTime? endDate) => endDate is null;

    public static Dictionary<string, string[]> Validate(SaveConsumerProviderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>();

        if (request.ProviderId <= 0)
            errors["providerId"] = ["Choose a provider from the agency directory."];

        var role = request.Role?.Trim();
        if (role is { Length: > MaxRoleLength })
            errors["role"] = [$"The role must not exceed {MaxRoleLength} characters."];

        if (request.StartDate is { } start && request.EndDate is { } end && end.Date < start.Date)
            errors["endDate"] = ["The end date cannot fall before the start date."];

        if (request.SortOrder < 0 || request.SortOrder > MaxSortOrder)
            errors["sortOrder"] = [$"The display order must be between 0 and {MaxSortOrder}."];

        return errors;
    }

    /// <summary>
    /// Why a second current primary care provider is refused. The previous one is named,
    /// because "there is already one" without saying which is a message that cannot be
    /// acted on.
    /// </summary>
    public static string PrimaryCareConflictMessage(string existingProviderName) =>
        $"\"{existingProviderName}\" is already recorded as this consumer's primary care provider. " +
        "End that relationship, or clear its primary-care mark, before recording another.";

    /// <summary>
    /// Why the same provider cannot be linked twice at once. A provider the consumer
    /// returned to after a gap is a second row with its own dates, which is correct — two
    /// simultaneous rows for one relationship is a data-entry mistake.
    /// </summary>
    public static string DuplicateCurrentLinkMessage(string providerName) =>
        $"\"{providerName}\" is already on this consumer's current provider list. " +
        "Edit that entry rather than adding a second one for the same provider.";

    public static string ProviderOutsideAgencyMessage() =>
        "That provider is not in this agency's provider directory.";

    /// <summary>
    /// Why a directory entry that appears on somebody's record cannot be deleted. Ended
    /// links count too: the row still references the entry, and the history is the reason
    /// the row was kept.
    /// <para>
    /// Deliberately a count and never names. A directory screen is not the place to
    /// disclose which consumers see which clinician, and an Admin curating the directory
    /// has no need to know.
    /// </para>
    /// </summary>
    public static string ProviderOnConsumerRecordsMessage(string providerName, int consumerRecords) =>
        $"\"{providerName}\" cannot be deleted because it appears on {consumerRecords} consumer " +
        $"{(consumerRecords == 1 ? "record" : "records")}. Remove it from those records first, " +
        "or leave the directory entry in place so their history stays readable.";

    public static string TooManyProvidersMessage() =>
        $"A consumer may have at most {MaxProvidersPerConsumer} providers recorded. " +
        "This is a safety limit rather than a clinical one — reaching it means something " +
        "has gone wrong with data entry.";

    /// <summary>
    /// The order the list reads in: the primary care provider first, then the case
    /// manager's own ordering, then by name. Shared so the API's response and the
    /// desktop's rendering cannot disagree about what "first" means.
    /// </summary>
    public static IEnumerable<T> OrderForDisplay<T>(
        IEnumerable<T> links,
        Func<T, bool> isPrimaryCare,
        Func<T, int> sortOrder,
        Func<T, string> displayName) =>
        links
            .OrderByDescending(isPrimaryCare)
            .ThenBy(sortOrder)
            .ThenBy(displayName, StringComparer.CurrentCultureIgnoreCase);
}
