# Handoff — Team chat over WSS

**For:** whoever implements this next (written for a fresh agent with no prior context).
**Source of truth:** [`TEAM_CHAT_DESIGN.md`](TEAM_CHAT_DESIGN.md) on `master`. Read the whole
design document before writing code — this brief is a map to it, not a replacement for it. Read
`CLAUDE.md` at the repo root first; it states the project's non-negotiable rules (rule ownership
in `Sati.Contracts.V1`, no direct EF in ViewModels, tenant isolation on every route, no PHI in
logs, fail-first security tests) and this design was written to comply with it throughout.

**Status:** design only. Nothing has been implemented. No migration exists. No route exists.

## What this is

In-app real-time messaging so case managers can help each other live instead of by email. Rooms
are scoped to teams and programs rather than one all-staff firehose. PHI is permitted — same-agency
workforce coordinating care is a permitted use, so there is no filtering and no sweeping — and chat
sits inside the same compliance envelope as every other PHI surface: authenticated, tenant-scoped,
audited on read, TLS in transit, encrypted at rest, retained under policy.

## The one decision everything else depends on

**The WebSocket pushes. It never accepts commands.** Messages are written with an ordinary
`POST /api/v1/chat/rooms/{id}/messages`, which flows through `ValidatedActorFilter`,
`SingleAttemptWriteFilter`, the rate limiter, and the route manifest exactly like every other
write in `Sati.Api`. The socket carries server→client frames plus three contentless client→server
frames (`resume`, `auth`, `pong`). Anything else closes the connection.

If you find yourself adding a frame type that changes server state, stop. That is the design being
inverted, and it means rebuilding the whole authorization pipeline inside a frame dispatcher.
Section 2 of the design document explains why, and what was rejected.

## Landing order (design document section 14)

Nine steps, each independently shippable. Work through them in order.

1. Entities, migration, `ChatAccess` in `Sati.Contracts.V1`, unit tests for the predicates.
2. The ten HTTP routes except `read` and `stream`. Update `ApiSurface.Routes` and
   `API_AUTHORIZATION.md` in the same change.
3. `POST /read`, the read marker, the coalesced `chat.room-read` audit. Update `AUDIT_EVENTS.md`.
4. Redaction.
5. Client chat over HTTP only — service, view models, panel, polling. **This is a complete,
   shippable feature at 30-second latency.** Ship it here if you need to stop.
6. `GET /chat/stream`, the connection registry, the authorization lease.
7. `ChatStreamConnection` on the WPF client.
8. The pre-send nudge. Optional and explicitly so.
9. Presence.

Do not build step 6 before step 5 works. The stream is a latency optimisation over routes that
must function without it — that is also what makes the proxy-blocked fallback free.

## The three things most likely to be got wrong

**1. The socket's authorization lease (design section 6.3).** An HTTP request is authorized once
and ends; a socket authorized once can live all day. A user whose permissions were revoked at
10:00 must not still be receiving PHI at 16:00. The connection needs three independent expiries:
the token's `exp`, the `sati_auth_time` + `MaxSessionMinutes` session cap that `POST /auth/renew`
already enforces, and a 30-second revalidation against the database. A renewed token arrives as an
`auth` frame and is validated through the same `TokenValidationParameters` the bearer handler uses;
a token for a different user closes the socket rather than re-targeting it.

**2. Read-audit granularity (design section 8).** One audit event per message displayed would put
over a thousand rows a morning into `AuditEvent` and turn the activity index into a second copy of
the conversation. The design coalesces: one `chat.room-read` event per five minutes, carrying a
contiguous `(from, to]` message-id range, so every message read still falls inside exactly one
audited range. Keep that contiguity property — it is the whole argument for why coalescing is
defensible rather than lossy.

**3. Suppressing the read marker behind the privacy screen (design section 12).** Messages that
arrived while the inactivity overlay was up were not read. Recording a PHI-access event for a read
that did not happen is a worse audit defect than recording nothing.

## Rule ownership — do not violate this

Per `CLAUDE.md`, the membership predicates go in `Sati.Contracts.V1.ChatAccess` and are referenced
by `Sati.Api`, never restated in an endpoint. `ChatNudge` goes there too, even though only the
client calls it, so a future web client cannot grow a second copy. A second hand-written copy of
either is a defect, not a convenience.

`ChatRoomScope` and `ChatMembership` are loaded from the database by the caller and never built
from request content — the same contract `CaseloadTransferRules` documents at length. Read that
file's comments before writing `ChatAccess`; it is the model to follow.

## Tests — read `CLAUDE.md`'s rule before writing any

Every security, tenancy, and concurrency test must be **confirmed failing against the unfixed
code** before you keep it. A cross-tenant test that passes because the route does not exist yet
has checked nothing. Write the route, then the test, then break the route to prove the test
notices.

Design document section 13 lists 37 specific tests. The eleven negative-authorization ones are
non-negotiable. Test 6 in particular — a membership row whose own `AgencyId` disagrees with the
room's must still be refused — exists to keep a redundant check load-bearing; if it passes without
that check present, it is testing nothing.

Cross-tenant refusals return 404, not 403. A 403 confirms the room id exists.

## Deployment constraints — flag these before step 6

- **Azure App Service has WebSockets off by default.** `webSocketsEnabled` must be turned on or
  the upgrade fails at the platform and looks like a generic connection error.
- **v1 requires a single App Service instance.** In-process fan-out does not cross instances. The
  client's 30-second reconcile means a second instance degrades latency rather than losing
  messages, but it also widens the removed-member exposure window in section 6.3. Record the
  constraint in `OPERATIONS.md`, not only in the design.

## Environment note

Demo's SQL firewall is closed to workstations. Step 1 adds four tables, so it needs a temporary
firewall rule that only Josh can add. Flag this before trying to apply the migration to Demo.

## Explicitly unresolved — do not guess, ask or flag instead

Section 16 of the design document lists O-1 through O-8. The ones most likely to block:

- **O-2** — how the all-agency room's membership stays in step with the user list. Three options,
  none chosen. Implied membership with no rows is simplest but breaks the design's own principle
  that membership must be answerable retrospectively. Do not pick one by default in code.
- **O-5** — chat's retention interval. `OPERATIONS.md` needs a named class and there is no
  obvious right answer; it is a policy and counsel question.
- **O-6** — the legal-hold gap. `ILegalHoldRegistry` answers questions about a consumer, and an
  untagged chat message about that consumer is unreachable by it. The design names the gap and
  deliberately does not close it. **Do not describe chat as covered by legal hold.**
- **O-3** — field-level encryption. The design gives chat bodies TDE only, matching note
  narratives. Do not envelope-encrypt chat alone; that is a platform-wide decision about narrative
  PHI and it starts with notes.
- **O-7** — whether the consumer tag stays optional or becomes prompted.

If these have not been resolved with Josh by the time the relevant step is reached, stop and ask
rather than picking an answer.

## Things to say accurately, and things not to say

- The nudge is **not** a security control. `CLAUDE.md` forbids UI visibility as security, and the
  nudge is UI. Posting PHI in the all-staff room is permitted; the nudge is minimum-necessary
  hygiene. Do not describe it in the UI, release notes, or documentation as preventing PHI
  exposure.
- Chat is **not** the clinical record, and the compose box says so. If chat traffic about clinical
  decisions starts to grow, the answer is a "promote to note" action, not a louder warning.
- Redaction retains the body. It is an amendment, not a delete, following `CLAUDE.md`'s rule 5.
- Chat is API-only. Local Production gets `ChatUnavailableService`, which throws. Do not build an
  EF chat service for a single-workstation install with nobody to chat with.

## Regulatory note

`REGULATORY_CONCERNS.md` needs a new entry when step 5 lands: chat is retained and discoverable,
its place in the designated record set is unsettled, and workforce conversation about a consumer
may be producible on a records request. Section 15 of the design document lists which documents
need updating as each step lands. Update them as you go, not in one pass at the end — that is how
they have stayed accurate through this project's history.
