namespace Sati.Contracts.V1;

/// <summary>
/// Where a medical directory entry sits in the affiliation hierarchy. Null on entries
/// that are not healthcare providers.
/// <para>
/// The three tiers are the same split the federal identifier system already makes —
/// individual clinicians hold Type 1 NPIs and organizations hold Type 2 — so this is a
/// real distinction rather than a convenience for the form.
/// </para>
/// </summary>
public enum MedicalProviderKind
{
    Individual,
    Practice,
    Network
}

/// <summary>
/// One directory entry reduced to what an affiliation decision needs. Callers hand in
/// their own agency's rows; nothing here reaches a database, so the desktop and the API
/// evaluate identical rules against identical inputs.
/// </summary>
public readonly record struct ProviderAffiliationNode(
    int Id,
    string Name,
    int? ParentProviderId,
    MedicalProviderKind? Kind);

/// <summary>
/// The single owner of provider affiliation: which tier may belong to which, what makes a
/// proposed parent illegal, and how an ancestor chain resolves.
/// <para>
/// Affiliation is one parent link rather than separate practice and network columns. Two
/// typed columns cannot express a hospitalist who belongs to a network with no practice
/// between, which would force a network column onto individuals as well — and at that
/// point an individual's network can disagree with their practice's network. One parent
/// has no such state. See DECISIONS.md, "Provider affiliation is one parent link, not
/// three typed tiers".
/// </para>
/// </summary>
public static class ProviderAffiliation
{
    /// <summary>
    /// How many links a chain may contain. This bounds every walk below, so a cycle
    /// introduced by concurrent edits or a hand-edited row cannot hang a reader even
    /// though <see cref="ValidateParent"/> refuses to create one.
    /// </summary>
    public const int MaxDepth = 10;

    /// <summary>
    /// The tier rule. Network to Network is deliberate: it is what lets three tier names
    /// survive four-level reality, where a network owns a multi-practice group that in
    /// turn owns practices. Individual to Individual is refused — a nurse practitioner
    /// under a supervising physician is a supervision relationship, not an affiliation,
    /// and folding the two together would corrupt every ancestor walk.
    /// </summary>
    public static bool CanParent(MedicalProviderKind child, MedicalProviderKind parent) => child switch
    {
        MedicalProviderKind.Individual => parent is MedicalProviderKind.Practice or MedicalProviderKind.Network,
        MedicalProviderKind.Practice => parent is MedicalProviderKind.Network,
        MedicalProviderKind.Network => parent is MedicalProviderKind.Network,
        _ => false
    };

    /// <summary>
    /// Whether the tier designation itself is coherent for this entry. Returns null when
    /// it is, or the reason to show the person editing it.
    /// </summary>
    public static string? ValidateKind(bool isHealthcare, MedicalProviderKind? kind)
    {
        if (isHealthcare && kind is null)
            return "Choose whether this medical provider is an individual, a practice, or a network.";

        if (!isHealthcare && kind is not null)
            return "Only medical providers are designated as an individual, a practice, or a network.";

        return null;
    }

    /// <summary>
    /// Whether <paramref name="proposedParentId"/> is a legal parent for this entry.
    /// Returns null when it is, or the reason to show the person editing it.
    /// </summary>
    /// <param name="childId">The entry being saved, or 0 when it is new.</param>
    /// <param name="childKind">The tier the entry will have after this save.</param>
    /// <param name="agencyDirectory">
    /// Every directory row in the acting agency. Scope is the caller's job, which is what
    /// makes a parent from another agency fail as "not found" rather than silently linking
    /// across a tenant boundary.
    /// </param>
    public static string? ValidateParent(
        int childId,
        MedicalProviderKind? childKind,
        int? proposedParentId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        ArgumentNullException.ThrowIfNull(agencyDirectory);

        if (proposedParentId is null)
            return null;

        // A non-medical entry may not carry a parent yet. The column is deliberately not
        // gated to healthcare in the schema — waiver providers have the same shape, an
        // agency owning programs owning staff — but no tier rule exists to check them
        // against, and an unvalidated hierarchy is worse than no hierarchy.
        if (childKind is null)
            return "Only medical providers can be affiliated with a parent organization.";

        if (childId != 0 && proposedParentId == childId)
            return "A provider cannot be affiliated with itself.";

        // Looked up rather than searched, so an id belonging to another agency — or a 0 from
        // a hand-written client — fails as "not in this directory" instead of matching the
        // default struct and producing a misleading tier message.
        var byId = agencyDirectory.ToDictionary(node => node.Id);
        if (!byId.TryGetValue(proposedParentId.Value, out var parent))
            return "That parent organization is not in this agency's provider directory.";

        if (parent.Kind is null)
            return $"\"{parent.Name}\" is not a medical provider, so it cannot be a parent organization.";

        if (!CanParent(childKind.Value, parent.Kind.Value))
            return TierRuleMessage(childKind.Value, parent.Name, parent.Kind.Value);

        // Walking up from the proposed parent is the cycle test: if this entry already sits
        // above it, the link would close a loop. This also catches the indirect case that a
        // self-check alone would miss.
        var depth = 1;
        var cursor = parent;
        while (true)
        {
            if (childId != 0 && cursor.Id == childId)
                return $"\"{parent.Name}\" already sits beneath this entry, so this would create a loop.";

            if (cursor.ParentProviderId is not { } nextId || !byId.TryGetValue(nextId, out var next))
                break;

            depth++;
            if (depth > MaxDepth)
                return $"Affiliation chains are limited to {MaxDepth} levels.";

            cursor = next;
        }

        return null;
    }

    /// <summary>
    /// The filter the parent picker uses, so the form offers only what a save would accept
    /// rather than reporting the tier rule after the fact.
    /// </summary>
    public static bool IsSelectableParent(
        int childId,
        MedicalProviderKind? childKind,
        int candidateParentId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory) =>
        ValidateParent(childId, childKind, candidateParentId, agencyDirectory) is null;

    /// <summary>
    /// The chain above an entry, nearest ancestor first. Bounded by <see cref="MaxDepth"/>
    /// and terminated on a repeat, so a cycle already present in the data yields a short
    /// answer instead of spinning.
    /// </summary>
    public static IReadOnlyList<ProviderAffiliationNode> ResolveAncestors(
        int providerId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        ArgumentNullException.ThrowIfNull(agencyDirectory);

        var byId = agencyDirectory.ToDictionary(node => node.Id);
        var ancestors = new List<ProviderAffiliationNode>();
        var visited = new HashSet<int> { providerId };

        if (!byId.TryGetValue(providerId, out var cursor))
            return ancestors;

        while (cursor.ParentProviderId is { } parentId &&
               byId.TryGetValue(parentId, out var parent) &&
               visited.Add(parentId) &&
               ancestors.Count < MaxDepth)
        {
            ancestors.Add(parent);
            cursor = parent;
        }

        return ancestors;
    }

    /// <summary>
    /// The nearest ancestor of a given tier — how a consumer profile answers "which
    /// practice" and "which network" for a selected clinician without storing either.
    /// </summary>
    public static ProviderAffiliationNode? NearestAncestorOfKind(
        int providerId,
        MedicalProviderKind kind,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        foreach (var ancestor in ResolveAncestors(providerId, agencyDirectory))
        {
            if (ancestor.Kind == kind)
                return ancestor;
        }

        return null;
    }

    /// <summary>
    /// The affiliation shown beside an entry, e.g. "Coastal Women's Healthcare · MaineHealth".
    /// Empty when the entry stands alone, which is a legitimate state rather than missing data.
    /// </summary>
    public static string DescribeAffiliation(
        int providerId,
        IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory) =>
        string.Join(" · ", ResolveAncestors(providerId, agencyDirectory).Select(node => node.Name));

    /// <summary>
    /// A provider and its resolved chain, frozen at a moment in time.
    /// <para>
    /// Live profile data derives the chain on every read so a directory correction reaches every
    /// consumer. A <em>document</em> must do the opposite: an assessment approved in March has to
    /// keep saying what it said in March, even after the physician changes practices. This is the
    /// shape that gets written into a document and never recomputed.
    /// </para>
    /// </summary>
    public readonly record struct ProviderSnapshot(
        int ProviderId,
        string ProviderName,
        string PracticeName,
        string NetworkName)
    {
        public bool HasAffiliation => PracticeName.Length > 0 || NetworkName.Length > 0;

        /// <summary>
        /// One line for a document: "Dr. Reed — Coastal Women's Healthcare · MaineHealth".
        /// The affiliation is omitted rather than padded when the provider stands alone.
        /// </summary>
        public string Describe()
        {
            var affiliation = string.Join(
                " · ", new[] { PracticeName, NetworkName }.Where(part => part.Length > 0));
            return affiliation.Length == 0 ? ProviderName : $"{ProviderName} — {affiliation}";
        }
    }

    /// <summary>
    /// Freezes a provider and its chain for writing into a document. An id that is not in the
    /// directory yields an empty snapshot rather than a partial one: a document should not record
    /// an affiliation it could not actually resolve.
    /// </summary>
    public static ProviderSnapshot Snapshot(
        int providerId, IReadOnlyCollection<ProviderAffiliationNode> agencyDirectory)
    {
        ArgumentNullException.ThrowIfNull(agencyDirectory);

        var provider = agencyDirectory.FirstOrDefault(node => node.Id == providerId);
        if (provider.Id != providerId || providerId == 0)
            return new ProviderSnapshot(0, string.Empty, string.Empty, string.Empty);

        return new ProviderSnapshot(
            providerId,
            provider.Name,
            NearestAncestorOfKind(providerId, MedicalProviderKind.Practice, agencyDirectory)?.Name
                ?? string.Empty,
            NearestAncestorOfKind(providerId, MedicalProviderKind.Network, agencyDirectory)?.Name
                ?? string.Empty);
    }

    /// <summary>
    /// Why a directory entry with affiliated entries beneath it cannot be deleted. Removing
    /// it would either orphan the subtree or, under SET NULL, promote every child to top
    /// level with nothing in the interface revealing that the hierarchy had split.
    /// </summary>
    public static string AffiliatedChildrenMessage(string parentName, IReadOnlyList<string> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var named = string.Join(", ", children.Take(5));
        var remainder = children.Count > 5 ? $", and {children.Count - 5} more" : string.Empty;
        return $"\"{parentName}\" cannot be deleted while {children.Count} " +
               $"{(children.Count == 1 ? "entry is" : "entries are")} affiliated with it ({named}{remainder}). " +
               "Reassign or remove those entries first, so the directory does not split into two unconnected halves.";
    }

    private static string TierRuleMessage(MedicalProviderKind childKind, string parentName, MedicalProviderKind parentKind)
    {
        var allowed = childKind switch
        {
            MedicalProviderKind.Individual => "a practice or a network",
            MedicalProviderKind.Practice => "a network",
            _ => "another network"
        };

        var child = childKind switch
        {
            MedicalProviderKind.Individual => "An individual",
            MedicalProviderKind.Practice => "A practice",
            _ => "A network"
        };

        var parent = parentKind switch
        {
            MedicalProviderKind.Individual => "an individual",
            MedicalProviderKind.Practice => "a practice",
            _ => "a network"
        };

        return $"{child} can only be affiliated with {allowed}, and \"{parentName}\" is {parent}.";
    }
}
