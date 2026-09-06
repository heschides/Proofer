# Team chat — implementation design after review

Revised 2026-09-05 from Claude's `team-chat-design` commit
`e7d80ca0de1e6d71b8a2e2b9f5caad0f7e15a895`. The original remains in Git history.
This document replaces its unsafe defaults. [Review findings](TEAM_CHAT_REVIEW.md) explain each
change; [the plain-language guide](TEAM_CHAT_GUIDE.md) explains operational and legal boundaries.

## Product scope

Explicit rooms for staff coordination, a WPF chat workspace, unread room counts, managed membership,
archiving, retained redaction, prompt updates and recovery after disconnection. No auto-joined
agency room or private-message feature. General coordination is subject to a no-client-details
policy. Consumer discussions have a room-level consumer reference and the existing consumer access
check in addition to membership. The reference cannot be changed to retarget an existing room.

Initial operation is synthetic-data-only and disabled by default. Local Production has no chat
service. No configuration switch alone makes real-client chat approved or compliant.

## Permission ownership

`Sati.Contracts.V1.ChatAccess` decides eligible permissions and agency/user/room membership bindings.
Callers construct its inputs from persisted state. `None`, billing-only, unknown permissions,
and platform-support identities receive no chat access. Existing `TenantAccess` supplies the
consumer/caseload restriction; room membership never overrides it.

Admins create rooms and manage their explicitly named members. Read authority still requires
membership and any consumer-specific access. Supervisor members and Admin members may hide a
mistaken message with a reason. Members may leave. Archived rooms permit authorized history reads
and redaction but no further posts. Membership closure remains recorded; a later rejoin is a new
interval with a new visibility boundary. Ordinary joining never grants earlier history.
Room and message-page responses identify the current membership episode, so a removal followed
by rejoining between refreshes still invalidates old client content and pending work.

## Persistence and ordering

Five tables: `ChatRooms`, `ChatRoomMembers`, `ChatMessages`, `ChatChanges`, `ChatReadMarkers`.
Persistence models and API twins map to the same physical schema. Every table is agency-stamped;
relationships are restrictive rather than deletion cascades.

`ChatRoom.Revision` is an optimistic concurrency token and the source of the room's ordered change
sequence. Every room mutation advances the revision. A post/redaction appends a change at that
revision in the same transaction. Gaps due to membership/metadata operations are permitted; reuse
or out-of-order commit of a revision is not. Conflicts return a clear refresh/retry outcome.

Messages and changes are immutable. A redaction appends a change identifying the original message,
actor, timestamp and protected reason; it does not edit or erase the original. Normal reads return
a placeholder instead of a redacted body. Neither application nor migration introduces a purge.
Consumer-linked rooms restrict consumer deletion. General/misfiled messages still need discovery
and hold procedures; a foreign key is not a complete legal-hold system.

Client retry identity is `(room, author, ClientMessageId)`. Exact retries return the same saved
message; reuse for different content is refused. Client merge identity is the server message ID.
The client retains an uncertain message's body and key until it can check/retry that same send.

## Content delivery and evidence

All commands and all message-body reads use ordinary authenticated API routes, with current actor,
membership and consumer checks. All response data is non-cacheable.

Before returning a nonempty message page, commit bounded append-only `chat.messages-released` audit
events describing the exact message IDs supplied, actor and room. A page is divided into chunks
that fit the existing audit storage limit; all chunks commit together. Store no body, room title,
consumer name or redaction reason in general metadata. This proves what the server made available,
not that a person read it. If evidence cannot be saved, do not release the batch.

Posting returns the sender's own submitted message; its immutable message/change rows supply the
write evidence. The release events above describe message-page retrieval, including older history.
Seen markers only support unread display; there is no five-minute deferred audit. Room metadata,
membership changes, redaction and administration have their own minimized activity records.

The incremental read returns ordered changes, the next sequence, the captured room revision and a
`HasMore` indicator. The client pages all changes and applies current message/tombstone forms.
It never relies on a timestamp overlap or a database identity being commit order.

## Notifications

The WebSocket sends only `{"type":"changed"}`. It never supplies content or room/user/person
identifiers. The client refetches through the normal authorized and audited route. No client
application frame is accepted. The connection has bounded resources, cancellation and token/session
expiry; token renewal stays owned by `CloudApiClient`, followed by reconnect.
Passive chat reads and socket opening do not initiate renewal; existing user-activity handling
owns keeping an active session alive.

In-process notifications improve latency. Periodic notification/reconciliation and ordinary HTTP
polling recover missed changes, restarts and deployments with more than one instance. No security
claim depends on the connection registry immediately knowing a membership was revoked, because
the registry never releases PHI. Hosting and multiple-instance behavior still require rehearsal.

## Desktop behavior

The current shell hosts chat inside the privacy-screen tree, preserving the current Overview.
Keep keyboard navigation, text unread cues, theme contrast, accessible names, and compose focus.
Do not produce desktop message previews or write chat caches/drafts to disk.
Show the authorized consumer's name and record identifier in the room context, independent of
its user-chosen title. Room detail editing retains the version originally shown and provides
an explicit reload; background refresh must not silently replace that edit's concurrency check.

Fetch content only for an active, unobscured chat workspace. A room/identity change cancels and
invalidates old loads before they can publish results. Removal, refusal, expired session, switch
user and closing clear the relevant message/draft state and stop the old connection. An unread
marker is a user-interface fact, not a substitute for the server's release evidence.
Outside chat, its navigation count is the last refreshed count, not a live delivery guarantee.

## Test and deployment sequence

1. Shared rules and both persistence models, schema migration and append-only guards.
2. Authenticated room/member/message/change/read/redaction routes and disabled-by-default gate.
3. Negative authorization, concurrent-write, retry, evidence and recovery tests with mutation proof.
4. Full WPF workflow and polling; stale-request, privacy and session-boundary tests.
5. Contentless notification transport and fallback verification.
6. Migration review and synthetic SQL Server rehearsal, then controlled Demo deployment if authorized.
7. Separate agency readiness work in the guide before any real-client activation.

The current implementation route inventory and test evidence are maintained in
`API_AUTHORIZATION.md` and `TEAM_CHAT_VALIDATION.md`. No application code migrates cloud databases.

## Deliberately unresolved for real-client use

Named privacy/security owners and legal applicability; sensitive-record/consent restrictions;
retention interval and hold scope including backups; controlled recovery/export of retained hidden
messages and misfiled conversations; account disablement and immediate session invalidation;
approved API-backed Production deployment; agency training and incident handling; restoration,
monitoring and assistive-technology acceptance. These are tracked in `AGENDA.md`, not silently
assumed complete because chat passes automated tests.
