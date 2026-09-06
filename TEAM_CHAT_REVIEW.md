# Team chat — design review and safer defaults

Reviewed 2026-09-05. Original: `team-chat-design`, commit
`e7d80ca0de1e6d71b8a2e2b9f5caad0f7e15a895`, `TEAM_CHAT_DESIGN.md` and
`HANDOFF_TEAM_CHAT.md`. This review supersedes the original where they disagree.
The user authorized both review and implementation, with safest practical defaults.

This is an engineering review, not an agency approval or legal opinion. See
[the plain-language guide](TEAM_CHAT_GUIDE.md) for responsibilities before real-client use.

## Findings and adopted corrections

| Original reference | Flaw and consequence | Adopted correction |
|---|---|---|
| Design sections 1, 3, 4.1, 10 | Same-agency employment is treated as sufficient grounds to receive PHI. An agency room plus a dismissible warning can disclose information to staff with no duty involving the consumer. | No automatic all-agency room. Explicit member rooms; general rooms are for operational coordination. A consumer discussion belongs to a consumer-scoped room and each reader must also pass the existing consumer-access rule. Free text is not claimed to be PHI-free or automatically classified. |
| Sections 5, 5.1 | Any supervisor can manage any room in their agency, including adding themselves to a room outside their supervisory reach. The proposed membership value also omits its room binding. | Admin controls room creation/membership. That authority alone does not authorize reading. Shared `ChatAccess` checks agency, room, user, active membership, and eligible permissions; consumer scope remains an additional live check. |
| Sections 3, 6.3, O-2 | The design assumes an active/disabled user lifecycle that the repository does not have. Known permission bits alone also admit `None` or billing-only access. | Explicit eligible permissions and live room membership; no automatic user-list synchronization. Removing all chat memberships suspends chat access. Platform-wide account disabling and immediate credential/session revocation remain a real-data prerequisite; password reset must not be described as session revocation. |
| Sections 3, 4 | New or re-added members can retrieve all old history, without an explicit historical disclosure decision. Membership is called append-only while removal mutates its closing fields. | Membership intervals are retained, with only one permitted closure. A join records its visibility boundary; new/re-added members see later messages only. No implicit historical-access grant. |
| Section 8.1; tests 24–26 | Reads inside the five-minute interval update only a marker. If the person stops reading, the final access never enters the audit history. Test 25 expects an immutable event to grow after it was written. | Before releasing a nonempty message batch, the server commits an append-only event identifying the exact messages supplied. Seen/unread markers remain presentation state. No deferred five-minute flush is relied on. |
| Sections 8.1, 12 | A maximum message number cannot establish which messages were displayed, and a client may omit the marker entirely. A privacy overlay does not undo information already sent to a workstation. | The record describes server release, not proof of human reading or a legal accounting of disclosures. Suppress content requests while chat is hidden/obscured; clear stale client state at session boundaries. |
| Section 6.4 | SQL identity allocation is not commit order. Reconnection does not prove all earlier transactions finished. A fixed five-minute overlap can miss late commits or more than one page of traffic. | A durable per-room change sequence is committed with the room revision and message/amendment. Conflicting writes cannot silently advance the sequence. Every recovery request pages that sequence to completion; time windows and identity watermarks are not correctness mechanisms. |
| Sections 6.4, 9 | Polling recently posted messages cannot discover a redaction of an older message when its immediate notification was missed. | Redactions are new entries in the same durable change feed. Responses resolve the current redacted form, including when an old post is replayed. |
| Schema and section 6.4 | The server scopes the retry key by room and author; the client deduplicates by the caller-controlled key alone. Two authors can choose the same key and suppress or replace a message locally. | Client merges by server-assigned message ID. Retry recognition uses room + author + client key and requires the same original body. An uncertain send preserves its key for retry. |
| Sections 4, 9 | Calling messages immutable does not enforce immutability. Redaction fields can be overwritten concurrently, and repeated correction can lose its first reason. | Original messages and change records are append-only. Redaction is a separate immutable change with actor, time, and protected reason. Database uniqueness permits one redaction per message; stale room revisions conflict instead of silently overwriting. |
| Section 6 | A socket carrying bodies creates another PHI release path whose authorization, auditing, queued writes, and revocation must all be correct. | The socket sends only a generic change notice, with no body, name, room, person, or user identifier. The ordinary authenticated, audited HTTP read supplies content. Notifications improve latency; they are never required for recovery. |
| Sections 6, 12 | Account switch, hidden screens, stale selections, queued sends, and failures can retain or publish an outgoing user's chat. | Cancel old requests and connections, invalidate load identities, clear messages and drafts, and require the new session to reload. No chat draft/message file or desktop message preview is created. |
| Sections 1, 11 | The introduction claims legal-hold reach while section 11 admits it is absent for untagged content. Tags and name detection cannot reliably find all relevant records. | No automatic chat purge. Consumer-linked rooms prevent consumer deletion from severing their evidence. General/incorrectly filed content and backups still require a documented discovery and preservation process before PHI use. Do not call the existing person-only hold registry complete chat hold enforcement. |
| Sections 1, 12 | “Chat is not the clinical record” suggests a product label can remove legal records obligations. | UI says chat does not replace required service notes and messages may be retained and included in records requests. Decisions about care must be documented in the proper workflow. |
| Sections 7, 11, O-3/O-5 | Matching another feature's encryption or retention choices is not a risk assessment or a retention rule. | Keep approved transport/storage protection, introduce no new external message processor, and require a documented platform risk assessment. Leave disposal disabled pending an approved retention/hold schedule. No invented universal six-year chat period. |
| Section 12 | Placement relies on an obsolete compact-workspace design, superseded by current local Overview changes. | Integrate into the current shell, under its existing privacy screen, while preserving the user's fixed Overview and Work Agenda changes. |

## Boundaries deliberately retained

Further implementation review identified and addressed these issues:

- A full message page's exact access evidence can exceed the existing audit field. Use bounded
  chunks committed together; never truncate evidence or release text after an audit failure.
- Long-lived notification requests must not hold a database context for their entire lifetime.
  Resolve and recheck access with short-lived contexts.
- Background refresh and socket reconnect must not count as user activity or automatically renew
  an idle session. Use the existing activity-based session process.
- Removing and re-adding a member between refreshes can otherwise preserve old cached history.
  Carry the membership episode on room and page responses and invalidate old text, drafts, pending
  sends and in-flight results when the episode changes.
- Neutral or duplicate room names do not establish which consumer is being discussed. Show the
  authorized consumer's name and record identifier separately from the room name.
- Refreshing a room's version while an administrator edits old details can silently overwrite
  another administrator's change. Preserve the editor's original version and require a deliberate
  reload after a conflict.
- Consumer deletion and database rollback must not remove retained chat evidence. Refuse linked
  consumer deletion before changing dependent records, and refuse rollback of populated chat.

- Chat uses the API; Local Production receives an unavailable service. No direct database client is introduced.
- Shared authorization predicates remain in `Sati.Contracts.V1`; neither WPF visibility nor a warning is security.
- Posted text never enters general diagnostic/audit metadata. Room names and redaction reasons are also potentially sensitive free text.
- No private direct-message feature, attachments, typing indicators, staff activity history, automatic clinical-note conversion, external message delivery, or AI processing is added.
- Current code is intended for explicitly enabled synthetic-data testing. Production activation, deployment, and application of a cloud migration are separate work.
- A successful test run is evidence of tested behavior, not proof of regulatory compliance.

## Verification required

Prove rejection of cross-agency access, non-members, removed members, invalid membership bindings,
ineligible permission sets, platform support identities, and consumer access lost after joining.
Prove new members do not inherit old history. Prove repeated/uncertain sends do not duplicate text,
conflicting writes do not lose changes, and old-message redaction is recovered without a live socket.
Prove records cannot be updated/deleted through the ordinary save boundary and linked consumer
deletion is refused. Prove every nonempty server body release has committed evidence and audit
failure prevents release. Prove privacy-screen, selection, session-change, and reconnect behavior.

Security/concurrency tests must detect an intentionally removed safeguard before being kept;
missing routes alone are not a valid negative test. SQL Server migration and concurrent-write
rehearsal remain distinct from SQLite integration tests. Real assistive-technology testing remains
distinct from XAML structure tests. The final test evidence is recorded in `TEAM_CHAT_VALIDATION.md`.
