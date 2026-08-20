using System.Security.Cryptography;
using System.Text;

namespace Sati.Contracts.V1;

/// <summary>
/// The exact set of routes this build of the API serves, and a short revision
/// fingerprint over that set.
///
/// This exists because a release number could not do the job. On 2026-08-19 the
/// hosted Demo API and the desktop client both reported release 1.2.17 while the
/// server was missing five routes the client had already started calling: the
/// version is bumped when a release is cut, not when a route is added, so comparing
/// it reported "in sync" and caught nothing. Every 404 from those routes surfaced in
/// the UI as "the record was not found or is outside your caseload", pointing case
/// managers at caseload problems that did not exist.
///
/// The fingerprint is derived from the route set itself, so it changes the moment
/// the API's surface changes and cannot drift out of step with a human remembering
/// to increment something. `ApiSurfaceTests` compares this list against the API's
/// live endpoint table and fails if they differ, which is what keeps the list honest:
/// adding a route without updating this file breaks the build rather than shipping a
/// client that quietly disagrees with its server.
///
/// Only the fingerprint crosses the wire. The route list is not published by any
/// endpoint — an unauthenticated caller has no business being handed a map of the
/// API's surface.
/// </summary>
public static class ApiSurface
{
    /// <summary>
    /// Every route, as "METHOD /pattern", ordinal-sorted. Generated from the API's
    /// own endpoint table; keep it that way rather than editing by hand.
    /// </summary>
    public static IReadOnlyList<string> Routes { get; } =
    [
        "* /health/ready",
        "DELETE /api/v1/at-requests/{id:int}",
        "DELETE /api/v1/contacts/{contactId:int}",
        "DELETE /api/v1/exempt-dates/{id:int}",
        "DELETE /api/v1/notes/{id:int}",
        "DELETE /api/v1/providers/{id:int}",
        "GET /api/v1/admin/activity",
        "GET /api/v1/admin/incidents",
        "GET /api/v1/admin/operations",
        "GET /api/v1/admin/overview",
        "GET /api/v1/admin/people",
        "GET /api/v1/at-requests",
        "GET /api/v1/at-requests/{id:int}",
        "GET /api/v1/at-requests/{id:int}/snapshot",
        "GET /api/v1/audit-events",
        "GET /api/v1/billing/candidates",
        "GET /api/v1/billing/claim-lines/draft",
        "GET /api/v1/billing/configuration",
        "GET /api/v1/billing/periods",
        "GET /api/v1/caseload",
        "GET /api/v1/exempt-dates/{year:int}",
        "GET /api/v1/incentives/history",
        "GET /api/v1/incentives/{year:int}/{month:int}",
        "GET /api/v1/me",
        "GET /api/v1/notes/day",
        "GET /api/v1/notes/monthly",
        "GET /api/v1/notes/year/{year:int}",
        "GET /api/v1/people/{personId:int}/appointments/latest",
        "GET /api/v1/people/{personId:int}/at-requests",
        "GET /api/v1/people/{personId:int}/contacts",
        "GET /api/v1/people/{personId:int}/history",
        "GET /api/v1/people/{personId:int}/history.pdf",
        "GET /api/v1/people/{personId:int}/journal",
        "GET /api/v1/people/{personId:int}/notes",
        "GET /api/v1/people/{personId:int}/pcp-source",
        "GET /api/v1/people/{personId:int}/reviews",
        "GET /api/v1/people/{personId:int}/ssn",
        "GET /api/v1/platform/incidents",
        "GET /api/v1/providers",
        "GET /api/v1/reports/consumer-billing-loss",
        "GET /api/v1/reviews",
        "GET /api/v1/scratchpad/history",
        "GET /api/v1/scratchpad/today",
        "GET /api/v1/scratchpad/tomorrow",
        "GET /api/v1/settings",
        "GET /api/v1/supervisor/notes",
        "GET /api/v1/supervisor/supervisees",
        "GET /api/v1/users/switchable",
        "GET /health/live",
        "GET /health/version",
        "POST /api/v1/admin/audit-export.csv",
        "POST /api/v1/admin/demo/seed-ssns",
        "POST /api/v1/assessments/{assessmentId:int}/submit",
        "POST /api/v1/at-requests",
        "POST /api/v1/at-requests/{id:int}/publish",
        "POST /api/v1/at-requests/{id:int}/reopen",
        "POST /api/v1/auth/login",
        "POST /api/v1/billing/claim-lines",
        "POST /api/v1/billing/periods/{periodId:int}/edi",
        "POST /api/v1/billing/periods/{periodId:int}/submit",
        "POST /api/v1/billing/periods/{year:int}/{month:int}",
        "POST /api/v1/exempt-dates",
        "POST /api/v1/forms/delete",
        "POST /api/v1/incentives/eligible-days",
        "POST /api/v1/incentives/remaining-days",
        "POST /api/v1/incidents",
        "POST /api/v1/notes",
        "POST /api/v1/notes/abandon-overdue",
        "POST /api/v1/people",
        "POST /api/v1/people/{personId:int}/agency-release.pdf",
        "POST /api/v1/people/{personId:int}/ai-context",
        "POST /api/v1/people/{personId:int}/assessments/draft",
        "POST /api/v1/people/{personId:int}/contacts",
        "POST /api/v1/people/{personId:int}/forms.pdf",
        "POST /api/v1/people/{personId:int}/journal/entries",
        "POST /api/v1/providers",
        "POST /api/v1/reviews/ensure-current",
        "POST /api/v1/scratchpad/{scratchpadId:int}/comments",
        "POST /api/v1/supervisor/notes/{noteId:int}/approve",
        "POST /api/v1/supervisor/notes/{noteId:int}/approve-override",
        "POST /api/v1/supervisor/notes/{noteId:int}/return",
        "POST /api/v1/users",
        "PUT /api/v1/admin/incidents/{incidentId:long}/status",
        "PUT /api/v1/assessments/{assessmentId:int}/document",
        "PUT /api/v1/at-requests/{id:int}",
        "PUT /api/v1/billing/configuration",
        "PUT /api/v1/forms/{id:int}",
        "PUT /api/v1/incentives/{id:int}",
        "PUT /api/v1/notes/{id:int}",
        "PUT /api/v1/people/{personId:int}",
        "PUT /api/v1/people/{personId:int}/contacts/{contactId:int}",
        "PUT /api/v1/people/{personId:int}/journal",
        "PUT /api/v1/people/{personId:int}/ssn",
        "PUT /api/v1/providers/{id:int}",
        "PUT /api/v1/reviews/{reviewItemId:int}/appointment",
        "PUT /api/v1/reviews/{reviewItemId:int}/stage",
        "PUT /api/v1/scratchpad",
        "PUT /api/v1/settings",
        "PUT /api/v1/users/me/password",
        "PUT /api/v1/users/{userId:int}",
        "PUT /api/v1/users/{userId:int}/password",
    ];

    /// <summary>
    /// Short fingerprint over <see cref="Routes"/>. Twelve hex characters is ample —
    /// this distinguishes builds, it does not defend against anyone crafting a
    /// collision, and a shorter value keeps the health response readable.
    /// </summary>
    public static string Revision { get; } = Fingerprint(Routes);

    /// <summary>
    /// The fingerprint of an arbitrary route set, so a caller can compute what a
    /// given surface would report.
    /// </summary>
    public static string Fingerprint(IEnumerable<string> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var canonical = string.Join("\n", routes.OrderBy(route => route, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12];
    }
}
