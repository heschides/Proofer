# Carika

Carika is a deliberately limited Avalonia client for authenticated client-profile access and case-note entry.

It references `Sati.Contracts`, not `Sati.Api`, and reaches Azure data only through the authenticated HTTPS API. It has no EF Core, SQL client, LocalDB, Azure SQL connection string, or migration code. Access tokens and passwords are memory-only in this first slice. Optional local drafts are DPAPI-protected for the current Windows user and bound to the Sati user and client IDs.

Set `CARIKA_WHISPER_MODEL` to an approved, already-provisioned GGML Whisper model. Carika has no cloud transcription fallback or automatic model download. This slice imports a temporary WAV; it does not yet capture microphone audio or copy/retain the selected audio. The user must review the transcript before saving.

This is an architectural first slice, not a HIPAA-compliance claim. Device encryption, workstation policy, model provenance/licensing, runtime telemetry, crash-dump/swap behavior, secure deployment, retention, incident response, and formal risk assessment remain deployment gates.

Case-note creation exposes only case-manager choices from `Sati.Contracts`: Scheduled, Draft,
Submit for review, Cancelled, and Delayed statuses; Visit, Contact, Form, and Other note types; and
the applicable form selector when Form is chosen. Supervisor/system-owned statuses and the
journal-only Reminder type are intentionally unavailable. The selected values are sent through
`SaveNoteRequest` and remain subject to authoritative API validation and workflow rules.
