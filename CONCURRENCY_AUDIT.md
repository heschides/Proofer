# Concurrency Audit

Date: 2026-08-14

## Purpose

This audit looked for places where two operations can overlap and produce an
incorrect result. That includes rapid navigation, repeated clicks, background
timers, account switching, window closing, and network responses arriving in a
different order than they were requested.

A **race condition** occurs when the result depends on which of two overlapping
operations happens to finish first. An **async operation** is work that can wait
for another resource, such as the Demo application programming interface (API),
without freezing the user interface.

## Material findings resolved

| Area | Risk | Resolution |
|---|---|---|
| Scratchpad saving | The timer, account switch, and window close could save the same scratchpad simultaneously and conflict on its revision number. | A single save gate now serializes those writes in request order. |
| User switching | Repeated switch requests or old-session initialization could overlap with the new session. | Account switching is single-entry and role navigation is awaited before the new session is published. |
| Calendar month navigation | A slower response for an old month could overwrite a newer month, and an automatic correction was previously saved in the background. | Only the newest request may publish tiles; corrective saves are awaited; tile changes are disabled while loading. |
| Client note selection | Notes from a slow response for the previously selected client could appear under the newly selected client. | Each load has an identity and only the newest matching-client response may update the list. |
| Billing views | Repeated navigation could start duplicate configuration and queue loads. | Each billing view collapses overlapping loads into one active request and handles load failure locally. |
| Form-note side effects | Form completion and form-opening work could continue after the note workflow had already refreshed. | The form side effects now return a `Task` and are awaited before note refresh. |
| Abandoned-note timer | Reinitialization could leave multiple hourly timers running, and a timer failure could escape through the Windows Presentation Foundation (WPF) event loop. | The prior timer is stopped, the callback cannot overlap itself, and background failure is observed without crashing the application. |
| Settings startup | Settings load begins from a constructor and therefore has no caller that can await it. A cloud failure could become an unobserved task error. | The load observes its own failure and reports it through the settings status text. |

## Verification

Deterministic tests reproduce the important timing hazards by deliberately
holding one operation open while a second operation starts. The tests verify
serialized scratchpad writes, collapsed billing loads, newest-only calendar
publication, and request invalidation. The normal desktop and API test suites
remain the regression safety net.

## Residual risk

- WPF requires some application and control event handlers to use `async void`.
  Those boundary handlers must continue catching their own exceptions or call
  methods that do so.
- New screens that load data in response to selection or navigation should use
  cancellation or a newest-request check before changing shared user-interface
  state.
- Server-side optimistic concurrency remains required. A user-interface gate
  prevents accidental overlap on one device; it cannot prevent two different
  devices from editing the same record.
- This is a focused code and deterministic-test audit, not mathematical proof.
  Network throttling and multi-device pilot testing should remain part of release
  qualification.

## Standard for future work

1. Await business operations that affect the next workflow step.
2. Serialize writes to the same record.
3. Cancel or invalidate obsolete reads.
4. Make repeated close, switch, load, and submit actions harmless.
5. Catch errors at framework-owned `async void` boundaries.
6. Add a deterministic overlap test whenever a timing defect is repaired.
