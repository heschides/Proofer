# Annual document templates

Implemented locally in step 5 on 2026-09-03; not migrated or deployed.

## Provisional Privacy Practices default

Josh authorized generic wording until the actual agency notice is available. Version 1 is seeded
as a Sati default and visibly says that agency privacy/legal review is required. It is not an
approved notice and should not be used operationally until reviewed. The actual agency version
should supply its legal effective date, named privacy contact, external complaint process, and
the uses, rights, restrictions, and procedures that apply to that agency. Review should start with
the [HHS model notices](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/model-notices-privacy-practices/index.html)
and qualified Maine/program advice rather than treating this generic draft as a compliance finding.

Generating a Privacy Practices PDF records document metadata. It does not record receipt,
acknowledgment, consumer authorization, or form completion. The acknowledgment workflow is step 7.

## Source format

Source is plain text, not HTML. One nonblank ordinary line becomes one paragraph.

| Syntax | Result |
|---|---|
| `# Heading` | Main heading |
| `## Heading` | Section heading |
| `- Item` | Bullet |
| `\| Cell one \| Cell two \|` | Table row; 2-8 columns, equal cell count per row |
| `\|---\|---\|` | Optional table separator, not printed |
| `[[PAGE_BREAK]]` | New page |
| `{{agency.name}}` | One-pass token substitution |

The first table row is a header. Blank lines separate source blocks. Unknown or malformed tokens,
invalid table widths, empty bodies, and source over 100,000 characters are rejected before publish.
Missing values render blank and are reported as token names on the artifact; values never enter
the blank-field list. Replacement values are not parsed again as template syntax.

Common tokens: `agency.name`, `agency.address`, `agency.phone`, `consumer.full_name`,
`consumer.birth_date`, `cycle.start`, `cycle.end`, `case_manager.name`, `case_manager.role`.
The future Medical Records Request kind additionally permits `provider.name`, `provider.address`,
`provider.phone`, and `provider.fax`; that generator/recipient workflow is not implemented here.

## Versions and access

`GET /api/v1/agencies/{agencyId}/templates/{kind}` returns the agency's and Sati-default versions.
`POST` to the same route accepts `{ "body": "..." }` and appends a new agency version.
Both require Administration permission and an exact match to the actor's agency. The server
assigns the author, timestamp, and next version; callers cannot publish a global default.

The latest non-retired agency version takes precedence over the latest non-retired Sati default.
Existing versions cannot be edited or deleted through tracked persistence. Publishing new wording
does not alter earlier artifacts' `TemplateOwner`, `TemplateKey`, or `TemplateVersion`.
`RetiredAtUtc` is reserved for a future controlled retirement workflow; there is no retirement
endpoint in this slice. A database unique index prevents two versions with the same agency/kind/number.

`POST /api/v1/people/{personId}/documents/PrivacyPractices` accepts an empty object or optional
`cycleStart`. Identity and template selection are server-derived, and the response is the PDF.
`IDocumentTemplateService` provides equivalent local and HTTP-backed operations. A WPF template
editor is not included; the later packet/profile workflow will consume this service.

## Persistence and verification

`20260903183358_AddDocumentTemplates` creates the table and seeds the provisional default.
No runtime database was migrated. Publishing records `document-template.published` without body
text; generation records template provenance without merged consumer content. Tests cover agency
and permission gates, token rejection, local generation, version preservation, and immutable rows.
The authorization and immutable-row tests were confirmed failing with their protections removed,
then passing after restoration. A synthetic one-page preview was rendered and visually inspected.
