# Team chat — real-time in-app messaging over WSS

*Designed 2026-09-05 against release 1.2.47. Status: design only. No code has been written.*

Read `CLAUDE.md`, `ARCHITECTURE.md`, and `API_AUTHORIZATION.md` before implementing this.
This document is the source of truth for the feature; `HANDOFF_TEAM_CHAT.md` is a map to it.

---

## 1. What this is and why

Case managers currently help each other by email. Email leaves the compliance envelope the
moment it leaves Sati: it is not tenant-scoped by anything Sati controls, its reads are not
audited, its retention is the mail provider's, and a message about a consumer ends up in a
mailbox no agency records process can reach. Moving that traffic inside Sati is a compliance
improvement, not only a convenience.

The secondary goal is stated plainly because it shapes the design: staff should have a reason
to keep Sati open. That makes latency, unread state, and reconnection quality product
requirements, not polish.

### PHI posture

PHI is permitted in chat. Same-agency workforce members coordinating care is a permitted use
and disclosure for treatment and health care operations. Consequently:

- There is **no** content filtering, no PHI detection gate, no sweep, no quarantine.
- There is **no** claim that chat is "PHI-free" anywhere in the UI, docs, or release notes.
  A control that pretends the data is not there makes the audit story worse, not better.
- Chat is inside the compliance envelope and carries the same obligations as every other
  PHI-bearing surface: authenticated, tenant-scoped, access-controlled, audited on read,
  encrypted in transit and at rest, retained under policy, reachable by legal hold.

### What chat is not

Chat is **not** the clinical record. A decision that affects services belongs in a service
note. This has to be stated in the UI, because the alternative is case managers documenting
care in a chat room and the note record going quiet. See section 12.

---

## 2. Governing constraint — the socket is a delivery channel, not a command channel

This is the single most important architectural decision here, and every other decision
follows from it.

**Writes go over ordinary HTTP. The WebSocket only pushes.**

```text
POST /api/v1/chat/rooms/{id}/messages   ──►  ValidatedActorFilter
                                             SingleAttemptWriteFilter
                                             membership gate
                                             EF insert
                                             in-process fan-out
                                                     │
GET  /api/v1/chat/stream (WSS)          ◄────────────┘  server→client frames only
```

Why: `Sati.Api` already spends its authorization, replay-safety, rate-limiting, route-manifest,
and audit machinery on the HTTP pipeline. A socket that accepts commands would need every one of
those mechanisms rebuilt inside a frame dispatcher, and the second copy is where the gap opens.
A push-only socket has a security surface that fits in a paragraph.

Client→server frames are restricted to three that carry no content: `resume`, `auth`, `pong`.
Any other inbound frame closes the connection. This is enforced by a whitelist, not a blocklist.

**Rejected:** SignalR. It would introduce a second authentication and session-lifetime mechanism
alongside `CloudApiClient`'s token handling, and its reconnect/backplane value is not yet earned
at this scale. Revisit only when the scale-out step in section 6.4 is actually needed.

**Rejected:** writing messages over the socket for latency. The POST returns the created message
and the sender renders it optimistically. The saved round trip is not worth a parallel write path
outside `SingleAttemptWriteFilter`.

---

## 3. Room model

There is no `Team` or `Program` entity in Sati today. `User.SupervisorId` and `AgencyId` are the
only grouping facts available, and `CaseloadTransferRules` derives supervisory reach from them.

**Decision: rooms have explicit, append-only membership rows. Membership is not derived.**

Rationale: minimum necessary is an access-control claim, and an access-control claim has to be
answerable retrospectively. "Who could read this room on 12 March" must be a query, not a
reconstruction from whatever `SupervisorId` happened to be that day. A derived membership also
changes silently when an unrelated field is edited, which is exactly the failure mode
`FormAttestation` was introduced to end for form completion.

Supervisor spans are still useful — they seed a new room's initial membership as a convenience
in the create UI. The seed writes real rows; it is not a live binding.

### Room kinds

| Kind | Purpose | Membership | Created by |
|---|---|---|---|
| `Team` | The working unit: a supervisor's span, a program, a regional office, a project. | Explicit rows. | Supervision or administration permission. |
| `Agency` | One per agency. Announcements and "does anyone know…". | Every active user in the agency, maintained automatically. | Provisioned, not user-created. |

Exactly one `Agency` room per agency, created on first use and kept in step with the user list.
`Team` rooms are unlimited in principle and capped in practice (section 9).

**Deferred, deliberately: direct 1:1 messages.** They are a materially larger surface —
discovery, supervision, retention of a conversation with no room to belong to, and the
"private channel for clinical decisions" failure mode. They get their own design or they do not
ship. Do not add them as "just a two-person Team room"; a Team room is visible in a room list
and administrable, and a DM is neither.

### Archival, not deletion

A room is archived, never deleted. Archived rooms are read-only and remain readable by their
membership. Removing a member sets `RemovedAtUtc`; it does not delete the row. Both properties
exist so the access history survives the org chart.

---

## 4. Schema

New file `Sati.Persistence/Models/Chat.cs`, with server twins in `Sati.Api/Data/ApiDbContext.cs`
following the existing `Server*` convention.

```text
ChatRoom
  Id                int, identity
  AgencyId          int, required            -- tenant
  Kind              int, required            -- Team = 1, Agency = 2
  Name              nvarchar(80), required
  Description       nvarchar(240), null
  CreatedAtUtc      datetime2, required
  CreatedByUserId   int, required
  ArchivedAtUtc     datetime2, null
  ArchivedByUserId  int, null
  index (AgencyId, Kind)
  filtered unique index (AgencyId) where Kind = 2      -- one Agency room per tenant

ChatRoomMember
  Id                int, identity
  RoomId            int, required
  UserId            int, required
  AgencyId          int, required            -- denormalized; must equal room's and user's
  Role              int, required            -- Member = 1, Moderator = 2
  AddedAtUtc        datetime2, required
  AddedByUserId     int, required
  RemovedAtUtc      datetime2, null
  RemovedByUserId   int, null
  filtered unique index (RoomId, UserId) where RemovedAtUtc is null
  index (UserId, RemovedAtUtc)

ChatMessage
  Id                bigint, identity         -- also the client's cursor
  RoomId            int, required
  AgencyId          int, required
  AuthorUserId      int, required
  AuthorDisplayName nvarchar(120), required  -- snapshotted, as ScratchpadComment does
  ClientMessageId   uniqueidentifier, required
  PostedAtUtc       datetime2, required
  Body              nvarchar(4000), required
  PersonId          int, null                -- optional consumer context, section 4.1
  RedactedAtUtc     datetime2, null
  RedactedByUserId  int, null
  RedactionReason   nvarchar(240), null
  unique index (RoomId, AuthorUserId, ClientMessageId)   -- idempotency
  index (RoomId, Id)
  index (RoomId, PostedAtUtc)
  filtered index (AgencyId, PersonId) where PersonId is not null

ChatReadMarker
  Id                    int, identity
  RoomId                int, required
  UserId                int, required
  AgencyId              int, required
  LastReadMessageId     bigint, required
  LastReadAtUtc         datetime2, required
  LastAuditedMessageId  bigint, required     -- what the last chat.room-read event covered
  LastAuditedAtUtc      datetime2, null
  unique index (RoomId, UserId)
```

`AuthorDisplayName` is snapshotted for the same reason `ScratchpadComment` snapshots it: a later
profile edit must not rewrite who said what.

Every table carries `AgencyId` directly. It is redundant against the join path in every case, and
it is there anyway, because a tenant filter that depends on a join is one bad `Include` away from
being absent.

### 4.1 The optional consumer reference

A message may carry `PersonId`. It is opt-in, chosen by the author from a picker, and it is a
pointer — it grants nothing and restricts nothing. Its value is threefold:

1. It makes "chat that mentions this consumer" a query rather than a text search. Without it,
   a legal hold on a consumer cannot reach chat at all (section 11).
2. It sharpens the nudge in section 10, which can then say "you already tagged Client X; this is
   the all-staff room."
3. It gives a future records-request workflow something to enumerate.

**Gate:** the `PersonId` must be in the actor's agency. It deliberately does **not** require own
caseload — a case manager asking a teammate about a consumer they do not hold is the central use
case, and requiring ownership would push exactly that conversation back to email. Room membership
governs who reads it; the tag governs nothing.

---

## 5. Routes

All under the existing `/api/v1` group, so `RequireAuthorization()`, `ValidatedActorFilter`, and
`SingleAttemptWriteFilter` apply unchanged. New file `Sati.Api/Endpoints/ChatEndpoints.cs`,
registered from `MapSatiApi` as `MapChat(api)`.

| Route | Access rule |
|---|---|
| `GET /chat/rooms` | Active membership only. Returns rooms with unread counts. Never enumerates rooms the actor does not belong to. |
| `POST /chat/rooms` | Supervision or administration permission; `AgencyId` from the actor, never the request. `Kind` may only be `Team`. |
| `PUT /chat/rooms/{roomId:int}` | Room moderator, or supervision/administration in the room's agency. Name and description only. |
| `POST /chat/rooms/{roomId:int}/archive` | Same as update. Refuses an already-archived room. |
| `GET /chat/rooms/{roomId:int}/members` | Active membership in the room. |
| `POST /chat/rooms/{roomId:int}/members` | Room moderator, or supervision/administration. Target user must be in the actor's agency and hold case-management, supervision, or administration permission. |
| `DELETE /chat/rooms/{roomId:int}/members/{userId:int}` | Same as add. Sets `RemovedAtUtc`; never deletes. A member may always remove themself. |
| `GET /chat/rooms/{roomId:int}/messages` | Active membership. `?after={id}` for backfill, `?since={utc}` for reconcile, `take` capped at 200. |
| `POST /chat/rooms/{roomId:int}/messages` | Active membership, room not archived. |
| `POST /chat/rooms/{roomId:int}/read` | Active membership. Advances the read marker and emits the coalesced read audit (section 8). |
| `POST /chat/messages/{messageId:long}/redact` | Supervision or administration permission in the message's agency. Requires a 10–240 character reason. |
| `GET /chat/stream` | Active membership in at least one room. WebSocket upgrade. |

Twelve routes. All twelve go into `ApiSurface.Routes` and into `API_AUTHORIZATION.md` in the same
change — `ApiSurfaceTests` fails the build otherwise, which is the intended behaviour.

`GET /chat/stream` appears in the live endpoint table as a GET and must be declared as one.

### 5.1 The membership gate, stated once

Every route above resolves access through one helper, in `Sati.Contracts.V1`:

```csharp
ChatAccess.CanReadRoom(AgencyActor actor, ChatRoomScope room, ChatMembership? membership)
ChatAccess.CanPostToRoom(AgencyActor actor, ChatRoomScope room, ChatMembership? membership)
ChatAccess.CanAdministerRoom(AgencyActor actor, ChatRoomScope room, ChatMembership? membership)
ChatAccess.CanRedact(AgencyActor actor, ChatRoomScope room)
```

`ChatRoomScope` carries `RoomId`, `AgencyId`, `Kind`, `IsArchived`. `ChatMembership` carries
`UserId`, `AgencyId`, `Role`, `IsActive`. Both are loaded from the database by the caller and
never built from request content — the same contract `CaseloadTransferRules` documents.

Reading requires all of: the actor's agency equals the room's agency, the membership row exists,
is active, and its own `AgencyId` equals both. The third check is redundant against a correct
insert and is there because a tenant boundary that depends on inserts being correct is not a
boundary.

Posting additionally requires the room not be archived.

---

## 6. The stream

### 6.1 Handshake

`GET /api/v1/chat/stream` with `Connection: Upgrade`. The bearer token travels in the
`Authorization` header — `ClientWebSocket.Options.SetRequestHeader` supports this, so no token
ever appears in a URL or query string.

Because the endpoint sits in the `/api/v1` group, `ValidatedActorFilter` has already confirmed
the identity, agency, role, and current persisted permissions against the database before the
upgrade is accepted. If the request is not a WebSocket request, return 400.

### 6.2 Frames

Server to client, one JSON object per frame:

| Type | Payload |
|---|---|
| `hello` | Server UTC, heartbeat seconds, `ApiSurface.Revision`, the actor's room ids. |
| `message` | One `ChatMessageDto`. |
| `redaction` | Message id, room id, redacted timestamp. |
| `membership` | Room id and whether the actor gained or lost it. The client refetches the room list. |
| `presence` | Room id and the user ids currently connected to it. |
| `ping` | Empty. |

Client to server:

| Type | Payload | Purpose |
|---|---|---|
| `resume` | `[{ roomId, afterMessageId }]` | Sent once after `hello`. The server replies with any missed messages, capped at 200 per room; beyond the cap it sends a `truncated` marker and the client backfills over HTTP. |
| `auth` | `{ accessToken }` | A renewed bearer token. See 6.3. |
| `pong` | Empty. | |

Anything else closes the connection with 1008. Frames are size-capped at 8 KB inbound.

### 6.3 The authorization lease — the part most likely to be got wrong

An HTTP request is authorized once and ends. A socket authorized once can live for hours. Without
deliberate handling, a user whose permissions were revoked at 10:00 keeps receiving PHI until they
close the application.

Each connection holds a lease with three independent expiries:

1. **Token expiry.** `exp` from the presented JWT. When it passes with no fresh `auth` frame, the
   server closes with 1008 and a reason the client maps to "reconnect". The client sends `auth`
   whenever `CloudApiClient` renews, which it already does five minutes before expiry.
2. **Session cap.** `sati_auth_time` plus `Authentication:MaxSessionMinutes`, exactly as
   `POST /auth/renew` enforces it. A socket must never outlive the HTTP session that a
   re-authentication would have been required to extend. An `auth` frame carrying a token whose
   `sati_auth_time` is older than the cap is refused and the socket closes.
3. **Revalidation.** Every 30 seconds the server re-reads the user row and the actor's active
   memberships. A vanished user, a changed agency, an unsupported permission set, or a lost
   membership takes effect on that tick.

A renewed `auth` frame is validated through the same `TokenValidationParameters` the JWT bearer
handler uses — issuer, audience, signing key, lifetime — and its `sub` and `agency_id` must match
the connection's. A token for a different user does not re-target an existing socket; it closes it.

**Membership changes are also pushed synchronously.** The member add/remove routes raise an
in-process event; on a single instance the fan-out set is corrected within the same request, and
a removed member is dropped from the room immediately and sent a `membership` frame. The 30-second
revalidation is the backstop, not the mechanism.

Residual exposure: on a multi-instance deployment, a removed member on another instance can
receive up to 30 seconds of messages. This is one of the two reasons v1 is single-instance
(section 6.4). Do not soften the revalidation interval to reduce database load without revisiting
this paragraph.

### 6.4 Delivery, ordering, and scale

**Delivery.** In-process fan-out. A connection registry maps room id to live connections; the
POST handler pushes to the members' connections after the insert commits. Never before the
commit — a delivered message that then fails to persist is worse than a slow one.

**Ordering.** `ChatMessage.Id` is the cursor. Clients dedupe on `ClientMessageId` and sort on
`Id`. There is no separate sequence column and no per-room counter, because a per-room counter
needs a lock and buys nothing: clients need "after X", not contiguity.

**Correctness under scale-out.** In-process fan-out does not cross instances. Rather than add a
backplane now, the client runs a **slow reconcile**: every 30 seconds each open room issues
`GET /chat/rooms/{id}/messages?since={utc-5min}` and merges by `ClientMessageId`. This is the
same route backfill already needs, so it costs one route and no infrastructure.

The consequence is that correctness does not depend on instance count. A second instance degrades
cross-instance latency from instant to at most 30 seconds; it does not lose messages.

The reconcile queries by **time window**, not `Id > watermark`, on purpose. SQL Server identity
values are assigned at insert and committed out of order, so a watermark advanced past a
concurrently-committing row would skip it permanently. A five-minute overlapping window plus
`ClientMessageId` dedupe makes commit ordering irrelevant. Backfill on connect may safely use
`after={id}`, because a reconnect follows a gap in which all prior writes have committed.

**v1 runs on a single App Service instance.** Record it in `OPERATIONS.md` as a deployment
constraint with its reason, alongside the WebSocket enablement below. The scale-out step — Azure
SignalR Service, or a per-instance database tail poll with a lag window — is named in `AGENDA.md`
and is not built now.

**Azure App Service requires `webSocketsEnabled` to be turned on explicitly.** It is off by
default. Without it the upgrade fails at the platform, not in Sati's code, and the failure looks
like a generic connection error.

### 6.5 Fallback

If the upgrade fails for any reason — an agency proxy that blocks WSS, the App Service setting
above, a corporate TLS interceptor — the client falls back to polling
`GET /chat/rooms/{id}/messages?since=` at a slower cadence and shows a quiet "reconnecting"
state. Chat degrades; it does not disappear. This is not a fallback bolted on later: the reconcile
in 6.4 already is the polling path, so the fallback is the same code with a shorter interval.

### 6.6 Reconnection

Exponential backoff with jitter, capped at 30 seconds. On reconnect the client sends `resume`
with its per-room high-water marks. It does not clear its local view first; a reconnect that
blanks the conversation reads as data loss.

`CloudApiClient.HasSessionEnded` is terminal for the socket too. When the session ends, the
socket closes and does not retry until a fresh sign-in.

---

## 7. Encryption

**In transit:** WSS only. `app.UseHttpsRedirection()` already covers HTTP; the client must refuse
a `ws://` endpoint outright rather than relying on the redirect. Assert the scheme in
`ChatStreamConnection`'s constructor.

**At rest:** Azure SQL Transparent Data Encryption, which is what protects every other narrative
column in Sati today.

Message bodies are deliberately **not** given the `EnvelopeProtector` treatment that SSNs get.
The reason is consistency, not convenience: service note narratives, journals, assessments, and
scratchpads are all TDE-only plaintext columns. Encrypting chat while the clinical record beside
it sits unencrypted would produce a claim the platform cannot honour, and it would put server-side
retention and legal-hold tooling out of reach for chat specifically.

If field-level encryption is wanted, it is a platform-wide decision about narrative PHI, taken
once, applied to notes first. See open question O-3.

---

## 8. Audit

Reads are audited as PHI access. Done naively — one event per message displayed — a forty-message
morning across thirty staff writes 1,200 rows before lunch, and the audit trail stops being the
activity index `AUDIT_EVENTS.md` describes and becomes a second copy of the conversation.

### 8.1 Read audit, coalesced but complete

`POST /chat/rooms/{id}/read` carries the highest message id the user actually saw.

- The read marker advances, never backwards.
- If the new id exceeds `LastAuditedMessageId` **and** `LastAuditedAtUtc` is null or older than
  five minutes, write one `chat.room-read` event covering the range
  `(LastAuditedMessageId, newId]` with metadata `{ roomId, fromMessageId, toMessageId, count }`,
  then advance `LastAuditedMessageId` and `LastAuditedAtUtc`.
- Otherwise advance the marker only. The next event's range absorbs it.

The property that makes this defensible: ranges are contiguous from `LastAuditedMessageId`, so
**every message a user read falls inside exactly one audited range**. Coalescing reduces the event
count without reducing coverage. An investigator asking "did this user read the message about
Client X" gets an answer, at five-minute resolution rather than per-message.

This mirrors `person-history.viewed`, which records that a history was viewed without copying the
history.

### 8.2 What is not audited, and why

There is **no** audit event per posted message. The `ChatMessage` row is itself append-only,
attributable, timestamped, and tenant-stamped — it is already the write record. Mirroring it into
`AuditEvent` would double the write volume of the most write-heavy surface in the product to
produce a strictly less informative copy. This reasoning goes into `AUDIT_EVENTS.md` explicitly,
because a reviewer will otherwise read the absence as an oversight.

### 8.3 New audit actions

| Action | Resource | Metadata |
|---|---|---|
| `chat.room-created` | Room id | Kind, member count seeded. |
| `chat.room-archived` | Room id | Nothing beyond the envelope. |
| `chat.member-added` | Room id | Added user id, role. |
| `chat.member-removed` | Room id | Removed user id, whether self-removal. |
| `chat.room-read` | Room id | `fromMessageId`, `toMessageId`, `count`. |
| `chat.message-redacted` | Message id | Room id, author user id. The reason stays on the message row. |

Membership changes are audited because they *are* the access-control changes. The read event is
the disclosure record. Nothing in any metadata field carries message text, consumer names, or the
redaction reason — same rule as every existing action.

### 8.4 Logging

Message bodies never enter logs, exception messages, `MetadataJson`, or incident records.
`ApiIncidentRecorder` already fingerprints exceptions by type and call site and captures no
request body, so it is safe as written; do not add body capture for chat diagnostics. A
correlation id plus a room id is the whole of what a chat failure may log.

---

## 9. Redaction, not deletion

Someone will post the wrong consumer's details into the wrong room. The answer is not a delete
button.

`POST /chat/messages/{id}/redact` (supervision or administration, same agency) stamps
`RedactedAtUtc`, `RedactedByUserId`, and a required 10–240 character reason. The body is
**retained** — this follows CLAUDE.md's rule that submitted records get amendments, not silent
overwrites, and it is what makes the incident investigable afterwards.

Clients render a redacted message as a tombstone with the redactor and time. `GET /messages`
returns the tombstone shape, not the body, to ordinary members. Whether an administrator may read
the retained body through any route is open question O-4; the default in this design is that no
route returns it, and recovery is a database operation with its own authorization.

A `redaction` frame pushes the tombstone to connected clients, so a mistake disappears from live
screens within the same second rather than at next launch.

### Limits

| Limit | Value | Reason |
|---|---|---|
| Message body | 1–4,000 characters after trim | Chat, not documentation. |
| Messages per user | 30 per minute, fixed window | A `"chat"` limiter beside the existing `"login"` one. |
| Rooms per user | 50 active memberships | Keeps the fan-out set and the room list bounded. |
| Members per room | 250 | Above this it is an announcement channel, not a team. |
| Backfill page | 200 | Matches the audit and review paging conventions. |

---

## 10. The pre-send nudge

Optional, client-side, advisory. It exists because minimum necessary is a habit, not a permission,
and habits benefit from a well-placed prompt.

**Scope: the `Agency` room only.** Team rooms are already scoped; nudging there would be noise
that trains people to dismiss the nudge.

Before sending in the all-staff room, the client checks the draft locally and, on a hit, offers a
non-blocking prompt: *"This looks like it names a client. Send to a team room instead?"* with a
picker of the author's Team rooms, a **Send anyway**, and a per-message **Don't ask again**.

Signals, all evaluated locally against data already in memory:

- A full name matching a consumer on the author's caseload.
- A distinctive surname match, minimum five characters, matching exactly one consumer.
- A MaineCare identifier pattern.
- An SSN pattern.
- A date of birth adjacent to a name match.
- A `PersonId` tag already attached to the message — the strongest signal, and the reason the tag
  in section 4.1 earns its place.

Rules the implementation must not break:

1. **The draft never leaves the machine.** No network call, no logging, no telemetry, no incident
   record. The heuristic is a pure function.
2. **It is not a control.** CLAUDE.md forbids UI visibility as security, and this is UI. Posting
   PHI in the all-staff room is permitted; the nudge is hygiene. The design must not be described
   anywhere as preventing PHI exposure.
3. **It never blocks.** One dismissal per message. No escalation, no second prompt, no supervisor
   notification.
4. **False positives are the failure mode to optimise against.** A nudge that fires on "I'll ask
   Mary in accounting" gets trained away within a week and then does not fire on the case that
   mattered.

The predicate lives in `Sati.Contracts.V1.ChatNudge` — pure, dependency-free, and unit-testable —
even though only the client calls it. A heuristic worth having is worth having tests, and putting
it in Contracts prevents a second copy appearing in a future web client.

---

## 11. Retention, legal hold, and discovery

Chat inherits `OPERATIONS.md`'s current posture: `RetentionEnforcementMode = PolicyOnly`. Nothing
deletes chat automatically, and nothing may until the retention workflow described there exists.
Add chat as a named retention class in that document with its own class and interval, decided by
Josh — it is not obviously the same interval as the audit trail or as clinical documentation.

**Legal hold has a real gap here and it must be named rather than assumed away.**
`ILegalHoldRegistry` answers questions about a consumer. A chat message with no `PersonId` cannot
be found by consumer, so a hold placed on a consumer does not reach conversation about them. The
optional tag in section 4.1 makes tagged messages reachable; untagged ones are reachable only by
text search across the agency's chat, which is a discovery exercise, not a control.

Do not represent chat as covered by legal hold. Record the gap in `OPERATIONS.md` and
`AGENDA.md`. Options for closing it later, in rough order of cost: make the tag mandatory when a
caseload name is detected, index message bodies for consumer-name search, or accept a documented
manual discovery process. This design does not decide which.

---

## 12. Client design (WPF)

### Placement

The chat panel lives in the shell side column, alongside Work Agenda and the Scratchpad, following
`DISPLAY_MODES_DESIGN.md`'s existing side-panel routing. In the Compact one-pane arrangement it
becomes a selectable workspace like the others. It is never a separate window: a window is the
right primitive for the daily agenda because that is a one-shot read, and the wrong one for chat
because chat is ambient.

An unread badge appears on the shell navigation. It counts rooms with unread messages, not
messages — a three-digit badge is noise.

### Non-negotiable interaction with the inactivity privacy screen

Three requirements, and the third is the subtle one:

1. Chat content must be covered by the overlay. It already is, because
   `ReleaseUiStructureTests.TheInactivityScreenCoversTheWholeWindowAndIsAdjustable` pins the
   overlay to the whole window. Do not add a chat surface outside that tree.
2. No notification, toast, or preview may surface message text while the privacy screen is up.
3. **The read-marker POST must be suppressed while the privacy screen is up.** Messages that
   arrived behind the overlay were not read, and recording a PHI-access event for a read that did
   not happen is a worse audit defect than recording nothing. Resume marking on dismissal.

### "This is not the record"

The compose box carries persistent, quiet helper text: *"Chat is not the client record. Document
services in a note."* Not a modal, not a per-message warning — a label. Section 1 explains why it
is there; it is the cheapest available guard against the documentation drift this feature could
otherwise cause.

### New types

| Path | Purpose |
|---|---|
| `Sati.Contracts/V1/ChatContracts.cs` | DTOs and frame types. |
| `Sati.Contracts/V1/ChatAccess.cs` | The membership predicates in 5.1. Single owner. |
| `Sati.Contracts/V1/ChatNudge.cs` | The section 10 heuristic. Pure. |
| `Sati.Persistence/Models/Chat.cs` | Entities. |
| `Sati.Api/Endpoints/ChatEndpoints.cs` | The twelve routes. |
| `Sati.Api/Infrastructure/ChatConnectionRegistry.cs` | Fan-out map, lease tracking, revalidation timer. |
| `Data/IChatService.cs` | Client-facing interface. |
| `Data/Cloud/CloudChatService.cs` | HTTP implementation. |
| `Data/Cloud/ChatStreamConnection.cs` | `ClientWebSocket` wrapper: connect, resume, backoff, `auth` frames. |
| `Data/ChatUnavailableService.cs` | Local Production. See below. |
| `ViewModels/Children/ChatViewModel.cs`, `ChatRoomViewModel.cs` | |
| `Views/ChatPanelView.xaml` | |

### Chat is API-only

Local Production talks to EF directly and runs on one workstation. There is no one to chat with,
and building an EF chat service would create a second implementation of the membership rule for a
scenario that cannot occur. Register `ChatUnavailableService` for local Production, following the
`CloudUnavailableServices` precedent, and hide the navigation entry. The service throws
`NotSupportedException` with a plain explanation if it is somehow called.

### Token coupling

`ChatStreamConnection` must not hold its own token. `CloudApiClient` owns the token and its
renewal; add an `AccessTokenRenewed` event to it, and have the stream send an `auth` frame in
response. This is a small, surgical change to a file that already manages the token under a lock —
raise the event outside the lock.

### Accessibility

Per CLAUDE.md: meaningful automation names on every message and room; keyboard navigation through
the room list and message history; a screen-reader live region announcing new messages in the
focused room only; unread state conveyed by text as well as by the badge colour; focus staying in
the compose box after send.

---

## 13. Tests

Per CLAUDE.md, every security, tenancy, and concurrency test must be **confirmed failing against
the unfixed code** before it is kept. A cross-tenant test that passes because the route does not
exist yet has checked nothing — write the route first, then the test, then break the route to
prove the test notices.

### Must fail first (negative authorization)

1. A user in agency A cannot read a room in agency B. 404, not 403 — a 403 confirms the room id.
2. A user in agency A cannot post to a room in agency B.
3. A non-member in the same agency cannot read a room's messages.
4. A non-member in the same agency cannot post.
5. A removed member (`RemovedAtUtc` set) cannot read or post.
6. A member cannot read a room whose `AgencyId` differs from their own membership row's, even
   with an active membership row present — the redundant check in 5.1 must be load-bearing.
7. `GET /chat/rooms` never returns a room the actor does not belong to.
8. A plain case manager cannot create a room, add a member, or redact.
9. A supervisor in agency A cannot add a user from agency B to their room.
10. The `PersonId` tag is refused when the consumer is in another agency.
11. `PlatformOperator` is refused every chat route by `ValidatedActorFilter`'s path allowlist.

### Stream lease

12. A socket whose token `exp` passes with no `auth` frame is closed.
13. An `auth` frame whose `sati_auth_time` exceeds `MaxSessionMinutes` is refused and closes.
14. An `auth` frame for a different user closes rather than re-targets.
15. A member removed while connected stops receiving that room's messages, and receives a
    `membership` frame.
16. A user whose permissions become unsupported is disconnected on the next revalidation tick.
17. An unrecognised inbound frame type closes with 1008.
18. An inbound frame over 8 KB closes.
19. A `ws://` endpoint is refused client-side.

### Delivery and idempotency

20. The same `ClientMessageId` posted twice yields one row and the same DTO both times.
21. Backfill by `after={id}` returns messages in ascending id order, capped at 200, with a
    truncation marker beyond it.
22. The reconcile window returns overlapping messages and the client dedupes to one.
23. A message is fanned out only after the insert commits — assert no delivery on a rolled-back
    write.

### Audit

24. Reading advances `LastAuditedMessageId` and writes exactly one `chat.room-read` covering the
    full range.
25. Two reads inside the coalesce window write one event whose range covers both.
26. Two reads either side of the window write two events whose ranges are contiguous and
    non-overlapping.
27. No `AuditEvent` row is written for a posted message.
28. No audit metadata anywhere contains message body text — assert against the serialized
    `MetadataJson` of every chat action.

### Redaction and retention

29. Redaction requires supervision or administration and a reason of the required length.
30. A redacted message returns a tombstone from `GET /messages` and pushes a `redaction` frame.
31. The body is retained in the row after redaction.

### Rules and nudge

32. `ChatAccess` predicates are tested directly, including the archived-room and cross-agency
    cases.
33. `ChatNudge` fires on a caseload full-name match, a MaineCare pattern, and an attached
    `PersonId`; does not fire on a common first name alone, on a four-character surname, or in a
    Team room.

### Client

34. The read-marker call is suppressed while the privacy screen is up and resumes on dismissal.
35. `ChatUnavailableService` throws rather than silently no-ops, and the navigation entry is
    absent in local Production.
36. `ReleaseUiStructureTests` gains a case pinning the chat panel inside the privacy-screen tree.

### Surface

37. `ApiSurfaceTests` passes with the twelve new routes declared, and the fingerprint changes.

---

## 14. Landing order

Each step is independently shippable and testable. Do not skip ahead to the stream before the
HTTP surface underneath it is complete and tested — the stream is an optimisation over routes
that must work without it.

1. Entities, migration, `ChatAccess` in Contracts, and its unit tests.
2. The ten HTTP routes except `read` and `stream`. Room list, membership, post, backfill.
   `ApiSurface`, `API_AUTHORIZATION.md`. Tests 1–11, 20–22, 32, 37.
3. `POST /read`, the read marker, and the coalesced `chat.room-read` audit. `AUDIT_EVENTS.md`.
   Tests 24–28.
4. Redaction. Tests 29–31.
5. Client HTTP-only chat: service, view models, panel, polling at the reconcile cadence. This is
   a working, shippable feature with 30-second latency. Tests 34–36.
6. `GET /chat/stream`, the connection registry, and the lease. Tests 12–19, 23.
7. `ChatStreamConnection` on the client, `AccessTokenRenewed`, backoff, resume, fallback to 5.
8. The nudge. Test 33.
9. Presence.

Steps 1–5 deliver the whole product value at higher latency. Steps 6–7 are the "keep Sati open"
half. Step 8 is optional and explicitly so.

---

## 15. Documents to update as each step lands

Update them with the step, not in one pass at the end.

| Document | What changes |
|---|---|
| `API_AUTHORIZATION.md` | Twelve routes with their access rules. Steps 2, 3, 4, 6. |
| `AUDIT_EVENTS.md` | Six new actions, plus the explicit reasoning for no per-message event. Step 3. |
| `ARCHITECTURE.md` | A `## Team chat` section: the push-only socket boundary, `ChatAccess` as rule owner, the API-only decision. Steps 2 and 6. |
| `DECISIONS.md` | The socket-is-delivery-only choice and its rejected alternatives; explicit membership over derived; coalesced read audit; TDE-only bodies. |
| `OPERATIONS.md` | Single-instance constraint, `webSocketsEnabled`, chat retention class, the legal-hold gap. Step 6. |
| `REGULATORY_CONCERNS.md` | Chat is not the clinical record; discoverability; the designated-record-set question. Step 5. |
| `AGENDA.md` | Scale-out backplane, direct messages, the legal-hold gap, retention enforcement. |
| `CLAUDE.md` | Add `ChatAccess` to the rule-owner list. |

---

## 16. Open questions — do not decide these in code

**O-1. Room administration.** This design lets supervision or administration create rooms and
manage membership, plus a per-room Moderator role. Is the Moderator role wanted at all, or is
supervision sufficient? Building both and using one is worse than building one.

**O-2. The Agency room's membership maintenance.** Every active user, kept in step automatically —
by what? A hook on user create/deactivate, a reconciliation on room open, or membership implied by
kind with no rows at all? Implied membership is simplest but breaks the section 3 principle that
membership is answerable retrospectively. Not decided.

**O-3. Field-level encryption for narrative PHI.** Section 7 makes chat consistent with notes
(TDE only). If Josh wants envelope encryption for chat bodies, it should be decided for note
narratives at the same time, and it is a platform-wide change with retention and tooling
consequences. Do not encrypt chat alone.

**O-4. Redacted body recovery.** This design returns no route that reads a redacted body. Should
an administrator be able to, with its own audit action? Or is a database operation the right
friction?

**O-5. Chat retention interval.** Section 11 requires a named class in `OPERATIONS.md`. Is chat
retained as long as clinical documentation, as long as the audit trail, or on its own shorter
clock? This is a policy and counsel question, not an engineering one.

**O-6. The legal-hold gap.** Section 11 names it and does not close it. Which of the three
options, or a fourth?

**O-7. Consumer tagging — optional or prompted?** Section 4.1 makes `PersonId` optional. Making
it prompted-when-detected would materially improve discovery and the nudge, at the cost of
friction on every message that mentions a name. Not decided.

**O-8. Presence granularity.** This design shows connected/not per room and deliberately omits
"last seen" and typing indicators, on the grounds that continuous staff activity visibility is a
labor-relations question rather than a feature. Confirm before adding either.

---

## 17. Risks

**R-1. Chat becomes the record.** The mitigation in section 12 is a label. If chat traffic about
clinical decisions grows, the honest response is a "promote to note" action, not a stronger
warning. Watch for it.

**R-2. The single-instance constraint is forgotten.** A scale-out that looks successful will
quietly degrade cross-instance chat to 30-second latency, and the removed-member window in 6.3 to
30 seconds. It must be in `OPERATIONS.md` and in the deployment checklist, not only here.

**R-3. The nudge trains people to dismiss.** Measured by nothing today. If it fires more than
occasionally in practice, tighten the heuristic or remove it. A dismissed nudge is worse than no
nudge because it looks like a control.

**R-4. Read-audit volume is still substantial.** One event per user per room per five minutes of
active reading. Thirty staff across six rooms is on the order of a few thousand events a day. That
is far better than per-message and still larger than anything currently in `AuditEvent`. Confirm
against `Sati:AuditRetentionDays` and the `admin/operations` counts before step 3 ships, and be
ready to widen the coalesce window.

**R-5. WebSocket connections must not hold a database context.** The connection registry must not
keep an `ApiDbContext` alive for the life of a socket. Revalidation creates a short-lived context
from `IDbContextFactory` per tick, as every other service in Sati does.
