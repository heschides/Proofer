# Handoff — reviewed team chat implementation

The original Claude handoff remains at commit
`e7d80ca0de1e6d71b8a2e2b9f5caad0f7e15a895` on `team-chat-design`.
Its assertion that it was already on `master` was inaccurate when reviewed.

Read `TEAM_CHAT_DESIGN.md`, `TEAM_CHAT_REVIEW.md`, `TEAM_CHAT_GUIDE.md` and
`TEAM_CHAT_VALIDATION.md` alongside the repository's architecture, decisions and authorization
inventory. The revised design supersedes the original's conflicting details.

Key changes: explicit membership and consumer access; no automatic agency room; no historical
access on joining; server release evidence instead of a deferred client-read audit; durable
ordered changes including redactions; notifications carry no content; immutable records; no
automatic purge; no real-data activation. Local Production remains unavailable.

The user authorized building and testing the feature, not a release, cloud migration, or change
to any security setting. Preserve unrelated local edits. Security and concurrency tests must
demonstrably detect removal of the safeguard they test. Record operational limitations accurately;
automated tests do not approve legal or regulatory compliance.
