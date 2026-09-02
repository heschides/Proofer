namespace Sati.Contracts.V1;

/// <summary>
/// One user as far as caseload authorization is concerned. Deliberately not the persistence
/// <c>User</c>: this carries the four facts the decision needs and nothing that must stay on
/// the server, so the same predicate can run inside the API and inside the transitional
/// desktop-local services without either of them holding a password hash.
/// </summary>
/// <param name="UserId">The user.</param>
/// <param name="AgencyId">Tenant. Reach never crosses it.</param>
/// <param name="Permissions">Validated permissions, read from the database — never from a request.</param>
/// <param name="SupervisorId">Who supervises this user, for the non-agency-wide case.</param>
public readonly record struct CaseloadParticipant(
    int UserId,
    int AgencyId,
    UserPermissions Permissions,
    int? SupervisorId);

/// <summary>Why a transfer was refused. <see cref="None"/> means it is permitted.</summary>
public enum CaseloadTransferDenial
{
    None = 0,

    /// <summary>The actor holds no supervisory permission, so they may not move a consumer at all.</summary>
    ActorLacksSupervision,

    /// <summary>The consumer's current owner is outside the actor's supervisory reach.</summary>
    CurrentOwnerOutOfReach,

    /// <summary>
    /// The proposed new owner is outside the actor's supervisory reach — which covers being in
    /// another agency, not reporting to this actor, and not holding case management at all.
    /// </summary>
    TargetOutOfReach,

    /// <summary>The consumer already belongs to the proposed owner.</summary>
    AlreadyOwned
}

/// <summary>
/// The single owner of who may move a consumer from one caseload to another.
///
/// <para>
/// This exists because the decision has to be made in two places that share no code path:
/// <c>Sati.Api</c> serving the Demo client over HTTP, and the transitional desktop-local
/// <c>PersonService</c> writing to local Production through EF. Neither environment may be the
/// lenient one, and a rule written twice becomes two rules the first time one of them is
/// edited. Both load the participants from their own database and hand them to
/// <see cref="Evaluate"/>; neither restates the predicate.
/// </para>
///
/// <para>
/// The immediate caller is the Credible import flow, where a supervisor onboards a team's
/// caseloads onto their own account and then distributes them. But the same operation is what
/// staff turnover and caseload rebalancing need, and it is deliberately not import-specific.
/// See <c>CREDIBLE_IMPORT_DESIGN.md</c>.
/// </para>
///
/// <para>
/// Note what this does <b>not</b> decide: whether the actor is who they claim to be. The API
/// re-confirms identity in <c>ValidatedActorFilter</c> and the desktop reads it from the signed-in
/// session before either one gets here. Handing this function a forged actor produces a confident
/// wrong answer, which is why neither caller builds one from request content.
/// </para>
/// </summary>
public static class CaseloadTransferRules
{
    /// <summary>
    /// Whether <paramref name="actor"/>'s supervisory reach extends over
    /// <paramref name="participant"/>'s caseload.
    ///
    /// <para>
    /// This is the predicate behind <c>TenantAccess.CanAccessUserAsync</c>, lifted here so the
    /// desktop can ask the same question. Reach requires all of: the actor supervises at all,
    /// the participant is in the actor's own agency, the participant can actually hold a
    /// caseload, and either the actor's reach is agency-wide or the participant reports to them
    /// directly.
    /// </para>
    /// </summary>
    public static bool CanReachCaseloadOf(AgencyActor actor, CaseloadParticipant participant) =>
        UserPermissionRules.HasSupervisorPermissions(actor.Permissions) &&
        participant.AgencyId == actor.AgencyId &&
        UserPermissionRules.HasCaseManagerPermissions(participant.Permissions) &&
        (UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions) ||
         participant.SupervisorId == actor.UserId);

    /// <summary>
    /// Whether the actor may reach a caseload that is either their own or a supervisee's.
    ///
    /// <para>
    /// The self case is separate on purpose. A supervisor who imported a batch of consumers holds
    /// them personally, and must be able to hand them out without also being their own supervisee;
    /// requiring <see cref="CanReachCaseloadOf"/> for that would make the ordinary import flow
    /// depend on a self-referential <c>SupervisorId</c> nobody sets.
    /// </para>
    /// </summary>
    public static bool CanReachOwnOrSupervisedCaseload(AgencyActor actor, CaseloadParticipant participant) =>
        participant.UserId == actor.UserId
            ? UserPermissionRules.HasCaseManagerPermissions(actor.Permissions) &&
              participant.AgencyId == actor.AgencyId
            : CanReachCaseloadOf(actor, participant);

    /// <summary>
    /// Decides one transfer. Both participants must already have been loaded from the database
    /// by the caller — passing values that came from the request is the one way to misuse this.
    /// </summary>
    /// <param name="actor">The signed-in user attempting the move.</param>
    /// <param name="currentOwner">Who holds the consumer now.</param>
    /// <param name="target">Who would hold the consumer after the move.</param>
    public static CaseloadTransferDenial Evaluate(
        AgencyActor actor,
        CaseloadParticipant currentOwner,
        CaseloadParticipant target)
    {
        // Checked before reach so a plain case manager gets "you do not supervise" rather than
        // the vaguer out-of-reach answer, which would read as though the consumer were the
        // problem.
        if (!UserPermissionRules.HasSupervisorPermissions(actor.Permissions))
            return CaseloadTransferDenial.ActorLacksSupervision;

        if (currentOwner.UserId == target.UserId)
            return CaseloadTransferDenial.AlreadyOwned;

        if (!CanReachOwnOrSupervisedCaseload(actor, currentOwner))
            return CaseloadTransferDenial.CurrentOwnerOutOfReach;

        // Reach already requires the participant to be in the actor's agency and to hold case
        // management, so it is the whole of "may this person receive a consumer". An earlier
        // draft checked those two facts again here as defence in depth; mutation testing showed
        // no test could tell the two branches apart, which is the signature of a rule stated
        // twice rather than a second rule. If target validity ever needs to diverge from reach,
        // it gets its own predicate and its own test — not a silent duplicate of this one.
        if (!CanReachOwnOrSupervisedCaseload(actor, target))
            return CaseloadTransferDenial.TargetOutOfReach;

        return CaseloadTransferDenial.None;
    }

    public static bool IsAllowed(
        AgencyActor actor,
        CaseloadParticipant currentOwner,
        CaseloadParticipant target) =>
        Evaluate(actor, currentOwner, target) == CaseloadTransferDenial.None;

    /// <summary>
    /// Caseload-facing wording for a refusal.
    ///
    /// <para>
    /// Every out-of-reach and missing-target case deliberately reads the same as the others at
    /// the API boundary, because the alternative tells an unauthorized caller which user ids
    /// exist in which agency. The distinct <see cref="CaseloadTransferDenial"/> values are for
    /// the audit trail and for tests, not for the person on the other end of a refused request.
    /// </para>
    /// </summary>
    public static string Describe(CaseloadTransferDenial denial) => denial switch
    {
        CaseloadTransferDenial.None => string.Empty,
        CaseloadTransferDenial.ActorLacksSupervision =>
            "Only a supervisor can move a consumer to another caseload.",
        CaseloadTransferDenial.AlreadyOwned =>
            "This consumer is already on that caseload.",
        _ => "That consumer or case manager is not on your team."
    };
}
