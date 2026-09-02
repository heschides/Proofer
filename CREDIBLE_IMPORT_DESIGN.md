# Credible Client Export Import — Design

*Drafted 2026-09-01. All six steps of the sequencing below are built and tested. Outstanding
work is listed there and in the agenda entry at the end.*

Consume a Credible client-data export and create Sati consumers from it: one at a time from the
Consumers page, and in bulk from a folder during agency onboarding.

Expected scale is 300–400 consumers per agency.

## What the export actually is

Credible's `client_printview.aspx?client_id={id}` is an ASP.NET page that posts back to itself.
The operator ticks section checkboxes, presses **Print View**, and the rendered sections appear on
the same page.

That page can be captured two ways, and the choice is not cosmetic:

- **Save as HTML** (`Ctrl+S`) — the lossless artifact. **This is the supported input.**
- **Print to PDF** (`Ctrl+P`) — a lossy render in which field-to-value pairing is destroyed. **Not
  supported.** See the evidence below.

The HTML output is a server-rendered table with semantic classes declared in the page's own
stylesheet:

| Class | Meaning |
|---|---|
| `.lc`, `.lc2` | label cell |
| `.vc`, `.vc2` | value cell (`.vc2` is `white-space:pre-wrap` — multi-line free text) |
| `.shc` | top-level section banner (CONSUMER INFO, CONSUMER EPISODE INFO) |
| `.hc` | sub-section banner — Consumer Address, Consumer Demograpics, Medical. **Not** a column header: all 44 in the real export are `colspan="4"` |
| `.shHeader` | the page title row (client name, id, DOB) — not a section |
| `P.page` | `page-break-after: always` — section delimiter |

Label and value are **adjacent cells in the DOM**. The column-interleaving problem that PDF
coordinate reconstruction would force us to solve does not exist here. A `.lc` followed by its
`.vc` is an unambiguous pair.

### Why print-to-PDF is refused, not merely discouraged

Tested 2026-09-01 against a 10-page print view of Credible's own `CREDIBLE TEST` demo client
(id 21864), extracted with `pdftotext -layout`. The text layer is present and complete — this is
not an OCR problem. The **pairing** is destroyed:

```text
Zip                clancy.donnelly@credibleinc.com     <- an email in the ZIP field
Consumer Email     Etna                                <- a town in the email field
City               3016529500                          <- a phone number in the city field
Admission Date     1876                                <- not a date
```

Those are the visible failures. The dangerous ones are invisible. Checked against the rendered
page, the values are shifted **up by one row** relative to their labels:

| Label | Extracted | Actually |
|---|---|---|
| Saddleback ID | `12345678A` | *(blank)* |
| MaineCare ID | `000001800` | `12345678A` |
| SSN | `YES` | `000001800` |

Every one of those reads as plausible in isolation. A MaineCare ID field silently holding an SSN
passes every format check Sati could apply, survives the field-level review screen because the
reviewer has no reason to doubt it, and lands in billing. That is the failure this whole design
exists to prevent, and no amount of parser effort makes it detectable from the artifact alone.

**Microsoft Print to PDF is worse still.** Tested 2026-09-01 on the same page: `Producer
(Microsoft: Print To PDF)`, 27 pages, **zero font objects**, JPEG image streams, and `pdftotext`
extracts nothing but page breaks. It rasterizes the document outright. That is an OCR problem
layered on top of the pairing problem, and OCR of a MaineCare ID or a date of birth is a claim
denial waiting to happen.

The print view is not a two-column layout. It renders as at least five parallel streams — left
label, left value, a centered section-header column, right label, right value — with varying row
heights, so values migrate vertically away from their labels. About 20% of extracted lines carry
three or more column breaks, and the section banners themselves get merged with neighbouring
values (`ACS   CONSUMER EPISODE INFO`, `ROI   L`). Values from the demographics block surfaced
attached to guardian and address labels several rows away.

Better tooling would improve this. PdfPig with real bounding boxes could cluster columns by X and
pair within a Y band, which is more than `pdftotext` attempts. But the vertical drift means Y-band
pairing is unreliable too, and the failure mode is **silent mis-pairing** — the wrong PHI written
to the right-looking field, which is the incident-class error this design exists to prevent. A
parser that is usually right about which value belongs to which label is worse than no parser.

So the reader detects a PDF and refuses it with a message naming the fix ("save the print view as
HTML, not PDF"). That is a cheap, honest feature. Supporting PDF properly is a separate project
with a bad risk profile and no upside, given the lossless artifact is one keystroke away.

### Sections available in the export

From the print-options checkboxes, with their default state:

| Section | Control | Default | Sati home |
|---|---|---|---|
| Client Profile | `cbClients` | on | `Person` demographics |
| Client Extended Profile | `cbClientsExt` | off | `Person` demographics |
| Client Episodes | `cbClientEpisode` | on | possibly `EffectiveDate` — see below |
| Visit Headers | `cbClientVisit` | on, 24 months | none — out of scope |
| Treatment Plans | `cbTxPlans` | off | none today (PCP is future scope) |
| Medications | `cbMeds` | on | none |
| Diagnosis | `cbClientAxis` | on | `Person.DiagnosisCode` |
| Insurance | `cbClientInsurance` | on | `Person.MaineCareId` |
| Authorizations | `cbAuthorizations` | on | none today (future scope) |
| Assignments | `cbAssignments` | off | none — Sati ownership is decided by import, not by Credible |
| Notes | `cbNotes` | on | out of scope for v1 |
| Contacts | `cbContacts` | on | `PersonContact` |
| Family | `cbClientDependent` | on | `PersonContact`, possibly `GuardianName` |
| Warnings | `cbClientWarning` | on | none |
| Allergies | `cbClientAllergy` | on | none |
| Medical Profile | `cbMedicalProfile` | on | possibly `PrimaryCareProvider` |
| External Providers | `cbExternalProviders` | on | `PersonProvider` / provider directory |
| Tx Plus | `cbTxPlus` | off | none |
| Credible Plan | `cbCrediblePlans` | off | none |
| E-Labs | `cbELabs` | conditional | none |

v1 consumes **Client Profile, Client Extended Profile, Diagnosis, Insurance, Contacts, Family**.
Everything else is parsed past and ignored. Sections with no Sati home are not a gap to close by
inventing storage for them.

### Confirmed table structure

The rendered page is a **four-column table**: `Label | Value | Label | Value`, with section
banners as full-width rows spanning all four columns. Rows are read left pair then right pair.

Verified 2026-09-01 against a real saved print view (128KB, "Webpage, Complete"): 678 `.lc` and
651 `.vc` cells, 482 `.lc2` and 454 `.vc2`, 42 `.shc` section banners, 44 tables, 2478 `<td>`.
The markup around a known field:

```html
<td class="lc"><b>&nbsp;MaineCare ID</b></td>
<td class="vc">12345678A&nbsp;</td>
<td class="lc"><b>&nbsp;Sandata ID</b></td>
<td class="vc">&nbsp;</td>
```

Exactly the adjacency the design assumed, and `MaineCare ID → 12345678A` matches the rendered
page — the same field the browser's PDF text layer reported as `000001800`, which is the SSN.

Three mechanical details the reader must handle:

- Labels are wrapped in `<b>` and prefixed with a non-breaking space; values are suffixed with
  one. Trim U+00A0 as whitespace at both ends, not just ASCII space.
- An empty value cell contains `&nbsp;` alone, so "present but blank" and "absent" are
  distinguishable — which is what makes the `cbHideBlank` check meaningful.
- `.lc2`/`.vc2` appear in comparable numbers to `.lc`/`.vc`, so the profile must treat both pairs
  as label/value, not just the first.

### Client Profile field map

Confirmed against the rendered page. This is the v1 mapping for the demographics sections:

| Credible label | Sati target | Note |
|---|---|---|
| First Name | `Person.FirstName` | |
| Last Name | `Person.LastName` | |
| DOB | `Person.BirthDate` | `MM/DD/YYYY` |
| Gender | `Person.Gender` | map to `Gender` enum; unrecognized → `Unknown`, reported |
| Consumer ID | `Person.CredibleClientId` | matches the `client_id` in the URL |
| MaineCare ID | `Person.MaineCareId` | |
| SSN | `SsnPanel` → `PUT /people/{id}/ssn` | **never** in `SavePersonRequest` |
| address1, address2 | `Person.Address` | joined for display |
| address1 | `Person.BillingStreet` | structured claim address |
| City | `Person.BillingCity` | |
| State | `Person.BillingState` | |
| Zip | `Person.BillingZip` | |
| Home Phone | `Person.PhoneNumber` | |
| Consumer Email | `Person.Email` | |
| Consumer is Own Guardian? | `Person.HasGuardian` | **inverted** — `YES` means no guardian |
| Guardian First/Last Name | `Person.GuardianName` | joined |
| Primary Diagnosis | `Person.DiagnosisCode` | `(F84.0) Autistic disorder` — split the parenthesized code from the description; store the code |
| Age | — | derived from DOB; ignored |
| Race or Ethnicity, Preferred Language, Religion, Financial Resource, Language Spoken at Home, Gender Identity | — | no Sati home; not invented for |
| date_updated, Signature Source, QI Reviewed, Is Restricted, Reminder Days Ahead, First Bill Service Date | — | Credible-internal; not imported |

Two of these are traps worth naming. **Consumer is Own Guardian? is inverted** relative to
`Person.HasGuardian`, so a straight copy sets the flag backwards on every record. And **Primary
Diagnosis is a composite** — `Person.DiagnosisCode` wants `F84.0`, not the whole string.

**The export carries the SSN in full**, unmasked, as ordinary table text. That is the strongest
practical argument for the never-upload decision: these files are among the most sensitive
artifacts an agency will ever hand around, and the design's answer is that they never move.

### Section banners observed in a real print view

Confirmed present in the 2026-09-01 test export, in document order. These are the strings the
layout profile matches on:

`CONSUMER INFO`, `Consumer Address`, `Consumer Contact Info`, `Emergency Contact`,
`Consumer Guardian #1`, `Consumer Guardian #2`, `Consumer Demograpics`, `Medical`,
`Employment/Day Program`, `Education`, `Other Information`, `Administrative Only`, `Referred By`,
`CONSUMER EPISODE INFO` (repeats per episode — 31 in the test client), `Diagnosis`, `Insurance`,
`WARNINGS`, `CONTACTS`, `External Providers`, `Dependent`.

Note `Consumer Demograpics` — the typo is Credible's, in their UI. The profile matches the string
that is actually there, misspelling included. This is a good argument for the profile being data
rather than code: nobody would have written that constant correctly from memory, and a future
Credible release that fixes the typo becomes a profile edit rather than a bug report.

### Export procedure the operator must follow

1. Open the client's print view.
2. Select the sections needed. `cbHideBlank` ("Hide Empty Profile Fields") **must be off** — with
   it on, empty fields drop their label rows entirely, so the field inventory differs per client
   and "label not found" stops being a meaningful signal. Off, the field set is stable across a
   batch, and a missing label means the export shape changed, which Sati reports rather than
   absorbs.
3. Press **Print View**.
4. **Save the print view's own document as "Webpage, Complete".** Not `Ctrl+P`, not "save as
   PDF", and **not `Ctrl+S` on the Credible application window**.

   "Webpage, Complete" is confirmed working. It also writes a sibling `_files` directory of
   assets, which the reader ignores — nothing outside the `.htm` is read, and nothing in it is
   fetched. "HTML Only" is expected to work equally well since the markup is what matters, but
   has not been verified; prefer the format that has.

Step 4 has three distinct ways to go wrong, all of which produce a file that looks complete:

| Mistake | What you get | Detected by |
|---|---|---|
| `Ctrl+P` → Save as PDF (browser) | Text present, label-to-value pairing destroyed | `%PDF-` magic bytes |
| `Ctrl+P` → Microsoft Print to PDF | Rasterized, zero font objects, no text at all | `%PDF-` magic bytes |
| `Ctrl+S` on the Credible app window | The `<frameset>` shell — ~14KB, no tables, no client data | `<frameset>` with no label/value cells |

The third is the subtle one. Credible is a frames-based application: the app window is a
`<frameset>` holding `banner`, `main`, and `frmsigpadext`, and saving it captures the frame
definition rather than the print view inside it.

Two ways to get the real document:

- Open the print view URL directly in its own tab —
  `.../client/client_printview.aspx?client_id={id}` — so no frameset is involved, then save.
- Or right-click inside the rendered print view and use the browser's "This Frame → Save Frame
  As…".

The reader refuses all three with a message naming the specific mistake, rather than importing a
thin or scrambled record. A refusal that says "this looks like the Credible application window,
not a print view" is worth more than any amount of parser tolerance.

Sati validates that the export contains the sections it needs and reports which were absent, rather
than silently importing a thin record.

## Governing decision: the export never leaves the workstation

Parsing is deterministic and operates on a document already sitting on the agency's machine. It
does not need a server.

```text
Credible export (already local)
  -> local HTML parse
  -> local mapping to a draft
  -> human review, field by field
  -> ordinary SavePersonRequest over the existing TLS + JWT API
```

The API surface grows by zero PHI-carrying bytes. Nothing is uploaded, stored, encrypted at rest,
retained, legal-held, or deleted, because nothing arrives. An import becomes indistinguishable from
typing, so `PersonSaveRules`, `TenantAccess`, `PersonLifecycle`, `PersonVersion` and the audit
trail already cover it, with no new bypass to review.

The rejected alternative — upload exports and parse them in `Sati.Api` — buys nothing functionally
and costs blob storage, key management, a retention and deletion story, expanded BAA scope, and
PHI in server memory.

## HTML-specific hazards

These did not exist in the PDF design and are the main new risk.

**Never render the export.** The page loads scripts and stylesheets from
`assets.cbh3.crediblebh.com`, and carries an Akamai/Boomerang RUM beacon pointed at
`s.go-mpulse.net`. Displaying it in a `WebBrowser`/`WebView` control — even for a preview — would
phone out to Credible and Akamai from a machine holding an open client record, and execute vendor
script against it. The export is parsed as inert data only: no script execution, no resource
fetching, no navigation.

**`__VIEWSTATE` is opaque and may carry PHI.** ASP.NET ViewState is base64 serialized server state
and, when not encrypted, can contain field values. It is never logged, never stored, never
displayed, and never parsed. It is treated exactly like the rest of the document body — read past
and dropped.

**Parser choice follows from this.** Use **AngleSharp** — spec-compliant HTML5 parsing, CSS
selector support, MIT, pure managed, and it does not fetch resources or run script unless
explicitly configured to. Configure it with no `IRequester` and no script provider so the safe
behavior is structural rather than a setting someone can flip later. HtmlAgilityPack is a viable
lighter alternative but lacks the selector engine the layout profile wants.

## Layering

### `Sati.Contracts.V1.CredibleProfileMapping`

The rule owner. Pure, no I/O:

```csharp
CredibleProfileDraft Map(ClientExportDocument document, CredibleLayoutProfile profile);
```

Testable with no HTML present. It lives in Contracts so that if a server-side path ever exists, a
field cannot be mapped two different ways.

`ClientExportDocument` is the parse result, not a DOM: named sections, each holding ordered
label/value pairs, plus the export's `client_id`. Keeping the DOM out of Contracts keeps AngleSharp
out of the API's dependency graph and keeps the mapper trivially testable from a literal.

`CredibleProfileDraft` carries per-field provenance: `(SatiField, ExtractedValue, SourceSection,
SourceLabel)`. The review UI needs to show where each value came from. There is no confidence
score — a label/value cell pair is exact. A field is mapped or it is unmapped.

### `IClientExportReader` — desktop side

AngleSharp behind an interface, producing `ClientExportDocument`. Accepts `.htm` and `.html`. A
"Web Page, Complete" save also produces a sibling `_files` directory; it is ignored, not required.
MHTML (`.mht`) is out of scope for v1.

The reader refuses, rather than half-parses, three specific wrong artifacts, each with a message
naming its own fix:

- **A PDF**, detected by `%PDF-` magic bytes rather than extension, so a PDF renamed `.html` is
  still caught.
- **The application frameset**, detected as a document containing `<frameset>` and no label/value
  cells.
- **A print view with no section banners**, which is the options page saved before **Print View**
  was pressed.

All three are procedure errors caught at the door. The bulk dry run reports them per file
alongside genuine parse failures, so an operator who got the export wrong learns it once for the
whole folder rather than 400 times.

### `CredibleLayoutProfile` — declarative, not compiled

Credible print views are configurable per agency, and the page is a vendor artifact that changes
with their release cycle. The profile is a declarative map — section header text, label text, and
the CSS selectors that identify label and value cells — stored as JSON on `Settings` (the pattern
`HealthcareSystemName`'s option list already uses), versioned, with a built-in default. A new
agency variant, or a Credible UI update, becomes a data change.

**The mapper never guesses.** An unfound label yields a blank field reported as unmapped. No
positional fallback. Missing PHI is a nuisance; PHI written to the wrong field is an incident.

Worth asking each agency during onboarding whether Credible exposes a data export or API to them.
A print view is a presentation artifact and will drift; a supported export would be a materially
more stable integration. The profile design is what keeps the drift survivable in the meantime.

## `client_id` is the dedupe key

The export's form action carries `client_id={id}` — Credible's own stable identifier for the
record. That is far better than fuzzy name/DOB matching.

`Person` already has a precedent for external identifiers in `EvergreenId`. Add a matching
`CredibleClientId` beside it. Matching order:

1. `CredibleClientId` exact — the reliable path, and what makes re-import exactly idempotent.
2. `MaineCareId` exact.
3. Normalized `(LastName, FirstName, BirthDate)`.

A match is skipped and reported, never silently merged — merging into an existing clinical record
on a fuzzy name match is unrecoverable.

## Single-consumer import

A "New from Credible export…" button beside the entry-panel toggle in `Views/ClientsView.xaml`.

1. File picker, parse, map.
2. Review screen: one row per field — Sati field, extracted value, source section and label, accept
   checkbox. Unmapped fields and absent sections visibly flagged.
3. Accept **populates `NewClientViewModel`** exactly as if typed.
4. The existing `Submit()` remains the only writer.

That last point is the safety property: one create path, fed by import rather than paralleled by
it. It matters most in local Production, where no server sits between the ViewModel and the
database. An import path that wrote directly would be entirely unguarded there.

**SSN routes separately.** The demographic save must not carry the number. A parsed SSN is held in
the review model and applied after creation through `SsnPanel` -> `PUT /people/{id}/ssn`, which
already does envelope encryption and shape validation.

**No effective date on import**, including from Client Episodes. See below.

## Bulk folder import

Same reader, same mapper, no upload. The supervisor's own workstation does the work.

1. Point at a folder, enumerate `*.htm` and `*.html`.
2. Parse each in memory, one at a time.
3. **Dry-run report first**: parsed N of M, K match existing consumers, P failed with reasons,
   and which sections were absent across the batch. Nothing written.
4. Commit sequentially, with progress and cancel, reporting per-record outcomes.

Idempotency: `CredibleClientId` makes re-running a folder exact. Additionally record a SHA-256 of
each document's bytes per run to detect an unchanged re-export. **Store the hash, not the
filename.** `Smith_John.htm` is PHI, and filenames reach logs far more readily than field values.

### No bulk-create endpoint

400 sequential ordinary creates is about a minute over HTTP, faster locally. Each record gets the
same validation, audit event and `PersonVersion` through the one write path. A bulk endpoint would
be a second way to create a consumer.

## Ownership: import lands on the importer

**Decision:** imported consumers land on the importing user's own caseload. A supervisor onboarding
a team's caseloads holds them, then distributes. The handoff is a wanted review point. The
export's own Assignments section is ignored — Credible's staff assignments are not Sati's, and
importing them would create ownership without an authorization check.

Consequence: `POST /people` already sets `UserId = actor.UserId`, and
`PersonService.AddPersonAsync` already rejects `person.UserId != actor.Id`. The import path needs
no contract change and no new authorization surface.

Distribution, however, does not exist today, and must ship with or before import — otherwise the
first bulk run strands 400 consumers with no exit.

### What distribution requires

`Person.UserId` is `{ get; private set; }` with exactly two internal writers, `CreatePerson` and
`Rehydrate`. Note reassignment is not a precedent: it moves a note between consumers on the same
case manager's own caseload and scopes the target with `person.UserId == actor.UserId`. It exists
to prevent cross-caseload movement.

- `Person.TransferTo(int userId)` — a third narrow writer, named, so ownership change is an
  operation rather than a property assignment. Preserves what `private set` was protecting.
- `IPersonService.TransferOwnershipAsync(personId, targetUserId, expectedRevision)`, implemented
  in both `PersonService` (EF) and `CloudPersonService` (HTTP).
- `PUT /people/{personId}/owner`, gated on `TenantAccess.CanAccessUserAsync` for **both** current
  and target owner, plus same-agency on both ends.
- `Sati.Contracts.V1.CaseloadTransferRules` — the permission owner. A pure function over
  already-loaded facts (actor, current owner, target user's agency and permissions) returning allow
  or a named denial. `Sati.Api` calls it after loading from `db`; `PersonService` calls it after
  loading from `SatiContext`. Neither writes the predicate. Same shape as `BillingComplianceGate`.
- `Revision` bump and `ExpectedRevision` on the request, so distributing while a case manager edits
  the profile does not clobber. In a 50-consumer distribution some will 409; the batch reports
  per-record outcomes.
- `person.reassigned` audit action, metadata `{previousUserId, newUserId}` and nothing else.
- Supervisor UI with multi-select. Distributing 400 one at a time is not a feature.

The local implementation re-reads the target user from the database and checks agency and
permissions itself, rather than trusting an id the ViewModel passed. Tests target `PersonService`
specifically, not only the API.

## No effective date on import

`GetAllPeopleAsync` loads the caseload with `.Include(Notes).Include(Forms)` and runs
`EnsureCurrentCycleForms` across every person on every load. Production is 26 clients. A supervisor
holding 400 mid-onboarding is roughly 15x that, on the exact path `AGENDA.md` blames for
memory-grant inflation and `RESOURCE_SEMAPHORE` stalls — the reason `Journal` and `Bio` are
excluded from it.

If imported consumers carried effective dates, the first caseload load after a bulk import would
perform the largest form-generation write that path has ever done.
`IX_Forms_PersonId_Type_DueDate` makes that a lost race rather than a triplication, but it should
not be discovered incidentally.

**Import creates demographics with no effective date**, even where the Client Episodes section
supplies an admission date. No forms generate. The effective date is set at distribution, by
someone with actual knowledge of the case. Import produces a shell; distribution makes it a live,
compliance-tracked case. The staging caseload stays cheap and transient.

A Credible episode start is also not necessarily the date Sati's compliance cycle should run from —
that is a program judgement, not a data migration.

If a 400-row staging caseload still drags the matrix, that is a separate paging fix and not a
reason to change this shape.

## Audit and log hygiene

New actions for `AUDIT_EVENTS.md`:

- `person.imported` — per consumer, metadata `{source:"credible-export", batchId, mappedFieldCount}`.
- `caseload-import.completed` — batch summary, counts only.
- `person.reassigned` — see above.

All three emit identically through `AuditTrail` (API) and `LocalAuditTrail` (desktop), or the trail
differs by environment.

`AUDIT_EVENTS.md` should add **filenames** to its forbidden-metadata list. It currently names
names; here they are the same thing.

Parse failures go through `IIncidentReporter` as a failure class plus section name. Mapper
exceptions carry field and section, **never the offending value** — the most common way PHI reaches
a log is an exception helpfully quoting what it could not parse.

On memory: managed strings cannot be reliably zeroed, so the mitigation is lifetime, not scrubbing.
Parse one document, map, drop. Do not accumulate parsed documents across a 400-file batch. SSN
follows what `LocalSsnStore` already does.

## Encrypted intake — deferred

Two things get conflated under "encrypted intake":

**Transport and storage of source documents.** Solved by scope reduction. The documents do not move.

**An agency hands Josh a folder for onboarding assistance.** This is a regulatory decision before
it is an engineering one: PHI entering Josh's custody requires a BAA and everything downstream of
that in `REGULATORY_CONCERNS.md`. Deferred outright.

If built later, the machinery exists: an intake bundle with AES-256-GCM per document, data keys
wrapped through `IKeyWrapper`, a recipient-key wrapper alongside `KeyVaultKeyWrapper` and
`DpapiKeyWrapper`, and `FieldBinding`-style AAD binding each document to `(agencyId,
documentIndex)` so a document cannot be lifted between bundles. `EnvelopeProtection` was designed
for this shape.

Note that agency-run bulk import delivers the same outcome with no custody transfer at all.

## Out of scope for v1

Server-side ingestion, encrypted bundle handoff, merge-into-existing, notes and service history,
medications, allergies, warnings, treatment plans, authorizations, episodes. Demographics,
diagnosis, insurance identifiers, and contacts.

## Testing

Fixture exports are **synthetic** — hand-authored HTML matching the real class structure, with
fabricated demographics. Never a real client's export, in the repo or the scratchpad. Because the
mapper takes `ClientExportDocument` rather than markup, most tests need no HTML at all.

- Known fixture maps to the expected draft.
- Missing label yields unmapped, not guessed.
- Absent section is reported, not silently treated as an empty section.
- `cbHideBlank`-style export (labels dropped) is detected and reported.
- A PDF is refused by magic bytes, with the message naming the HTML fix — including a PDF given a
  `.html` extension.
- Dedupe: `CredibleClientId`, `MaineCareId`, and name/DOB paths.
- Reader performs no network activity and executes no script when handed a document containing
  external references and inline script. This is the test that keeps the safe behavior from being
  configured away later.
- Transfer refused across agencies, and refused peer-to-peer by a non-supervisor — confirmed
  failing against the unfixed code before being kept.
- Transfer tests run against `PersonService` as well as the API.

## Sequencing

1. ~~`CaseloadTransferRules`, `Person.TransferTo`, `PUT /people/{id}/owner`, both service
   implementations, audit, tests.~~ **Done 2026-09-01.** 13 API tests and 9 desktop tests; each
   guard confirmed load-bearing by mutation. Two findings from that pass are worth keeping:
   the supervisor-link reach rule had no test until mutation testing exposed the hole, and a
   `TargetCannotHoldCaseload` branch turned out to be fully subsumed by the reach check — no
   test could tell them apart — so it was removed rather than left as a rule stated twice.
2. ~~Supervisor distribution UI with multi-select.~~ **Done 2026-09-01.** `CaseloadDistributionViewModel`
   as a supervisor-dashboard sub-view; per-record outcomes rather than a batch result, because
   a consumer edited elsewhere must fail on its own and be seen to. Target list calls
   `CaseloadTransferRules.CanReachCaseloadOf` rather than restating it. 9 view-model tests and
   3 render tests; the render tests exist because this view shipped a `DynamicResource` naming
   a brush the themes do not define, which fails silently.
3. ~~`ClientExportDocument` and `CredibleProfileMapping` in Contracts; `CredibleLayoutProfile`;
   synthetic fixtures.~~ **Done 2026-09-01.** 29 tests, six guards confirmed load-bearing by
   mutation: the guardian inversion, the diagnosis-code extraction, the gender fallback, the
   culture-independent date parse, the non-breaking-space trim, and first-occurrence handling
   for repeated sections. The mapper reports four distinct kinds of absence — `Blank`,
   `LabelMissing`, `SectionMissing`, `Unreadable` — because collapsing them is what makes a
   truncated export indistinguishable from a sparse client.
4. ~~`IClientExportReader` over AngleSharp, with the no-network/no-script test.~~ **Done 2026-09-01.**
   15 tests; the three refusals and the banner/label/value rules each confirmed load-bearing by
   mutation. Verified end to end against the real 128KB export: 86 sections, 1107 fields, zero
   missing sections, and every one of the 17 mapped fields matching the rendered page.
5. ~~Single-consumer import button and review screen.~~ **Done 2026-09-01.** Plus
   `Person.CredibleClientId` and its migration (`20260901232228_AddPersonCredibleClientId`),
   which the dedupe key needs. 25 tests. `ApplyImportedDraft` fills the form and writes nothing;
   `Submit` stays the only writer, which is what keeps one create path in local Production where
   nothing sits between the view model and SQL Server.
6. ~~Bulk folder: dry run, then commit.~~ **Done 2026-09-01.** 14 view-model tests plus 9 API
   tests for the dedupe lookup, whose three guards are confirmed load-bearing by mutation.
   `POST /people/credible-matches` is agency-scoped and returns no name and no person id — only
   the ids that matched, and the owner's display name where the caller could already see that
   caseload. A POST rather than a query string because these identify real people and a query
   string is the part of a request that reliably reaches access logs.

Steps 1–2 stand on their own merits — staff turnover and caseload rebalancing need them regardless
— so they are not wasted if the export work stalls.

---

## Draft entry for `DECISIONS.md`

> **Credible export import takes HTML only, and parses on the workstation — 2026-09-01**
>
> Credible's client print view is captured as saved HTML. Printing it to PDF is refused by magic
> bytes: tested against a 10-page export of Credible's `CREDIBLE TEST` demo client, the PDF text
> layer is complete but the label-to-value pairing is destroyed by a five-stream column layout with
> vertical drift, and the failure mode is silent mis-pairing — the wrong value under the right
> label. The HTML carries `.lc`/`.vc` cell adjacency and has no such ambiguity. Exports are parsed
> locally with AngleSharp
> as inert data — never rendered, because the page loads vendor scripts and an Akamai RUM beacon,
> and its `__VIEWSTATE` may carry PHI — and submitted as ordinary `SavePersonRequest` payloads.
> Nothing is uploaded, stored, or parsed server-side. Import populates the existing create form
> rather than writing directly, so local Production and cloud Demo enforce identically and there is
> one create path. Credible's `client_id` is stored as `Person.CredibleClientId` and is the dedupe
> and idempotency key. Imported consumers land on the importing user's caseload and carry no
> effective date; a supervisor distributes them, and the effective date is set at distribution,
> which is what starts compliance-form generation. Credible's own Assignments section is ignored.
> Encrypted intake bundles and any PHI custody transfer are deferred pending
> `REGULATORY_CONCERNS.md` review.

## Draft entry for `AGENDA.md`

> ## Credible export import — 2026-09-01
>
> All six steps built and tested 2026-09-01. Full write-up in `CREDIBLE_IMPORT_DESIGN.md`.
>
> - [x] `CaseloadTransferRules` in Contracts; `Person.TransferTo`; `PUT /people/{id}/owner`;
>       `person.reassigned` audit in both trails; `ExpectedRevision` and typed 409. Every guard
>       confirmed load-bearing by mutation, against `PersonService` as well as the API.
> - [x] Supervisor distribution UI, multi-select, per-record outcomes.
> - [x] `Person.CredibleClientId` beside `EvergreenId`, with migration. **Not yet applied to any
>       database** — SatiDemo needs a temporary firewall rule first.
> - [x] `ClientExportDocument` and `CredibleProfileMapping` in Contracts; `CredibleLayoutProfile`
>       with a verified default. Storing an agency override as JSON on `Settings` is still open.
> - [x] `IClientExportReader` over AngleSharp (1.7.2), a bare `HtmlParser` with no browsing
>       context, so there is no requester and no script engine. Refuses PDFs by magic bytes, the
>       application frameset, and the options page.
> - [ ] Write the operator-facing export procedure into the onboarding material: sections to tick,
>       Hide Empty Profile Fields off, and save as HTML rather than print to PDF.
> - [x] Single-consumer import button and field-level review screen.
> - [ ] Write an imported SSN. The review panel shows the number the export carries and refuses
>       to accept it, because the value is encrypted against a consumer id that does not exist
>       until the record is saved. Nothing carries it in the meantime — an always-null field is
>       a guard that does nothing. Wiring it means applying it through the SSN route immediately
>       after `Submit` succeeds, with an honest story for the case where the consumer is created
>       and the SSN write then fails.
> - [ ] Bulk folder import: dry-run report, then sequential commit. Document hashes, not filenames.
> - [ ] Add `person.imported` and `caseload-import.completed` to `AUDIT_EVENTS.md`, and add
>       filenames to its forbidden-metadata list. **Not done** — an imported consumer currently
>       records `person.created` like any other, which is accurate but does not distinguish an
>       import from hand entry.
> - [ ] `API_AUTHORIZATION.md` row for the new owner route.
> - [ ] Ask agencies during onboarding whether Credible exposes a supported data export or API. A
>       print view is a vendor presentation artifact and will drift.
