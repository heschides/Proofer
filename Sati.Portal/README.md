# Signature portal: controlled synthetic setup

This is a separate public ASP.NET Core host. It is disabled by default. The implemented policy
accepts only `Demo`/`SatiDemo` or injected isolated `Testing`/`SatiApiTests` environments. Production
is refused even if the feature flag is set. Staff mutations also require a creation-time test
consumer marker. Do not relabel existing consumers or bypass these gates to try real records.

Read `SIGNATURE_PORTAL_GUIDE.md` and `SIGNATURE_PORTAL_REVIEW.md` before setup. This document is for
the person configuring hosting; it does not approve a deployment, a migration, or real signatures.

## Separate identities and resources

Use an independently deployed portal with its own managed identity, host name, release process,
network controls and logs. Never reuse the staff API identity. The portal needs only:

- the reviewed `sati_signature_portal` SQL role from `scripts/Grant-SignaturePortal.sql`;
- read access to a dedicated **private** signature blob container;
- unwrap access to the dedicated signing-PIN Key Vault key, including retained key versions.

The staff API/worker needs the full staff application's authorized database rights, create/read
rights on that container, wrapping/unwrapping rights on both distinct keys, and (only when
notifications are explicitly enabled) its separate ACS Email send identity. The portal has no
mail sender, no outbox-key registration, no staff routes, and no migration runner. The grant script
does not create users or assign an identity; an authorized operator performs and verifies that
step separately. A compromised portal identity can still read signature metadata and retained
PDFs across the dedicated container. These permissions do not create per-agency SQL identities.

## Configuration

Supply configuration through the host's protected settings. The following names are required;
no working credentials or invitation tokens belong in source control.

| Setting | Staff API | Public portal |
|---|---|---|
| `Sati:ExpectedEnvironment` | Existing validated `Demo` identity | `Demo` |
| `Sati:ExpectedDatabaseName` | Existing validated `SatiDemo` identity | `SatiDemo` |
| `Signatures:Enabled` | Explicit `true` for synthetic rehearsal | Explicit `true` for the matching synthetic environment |
| `Signatures:WorkersEnabled` | Explicit `true` to prepare copies/process notifications | Not used; no workers are registered |
| `ConnectionStrings:SignaturePortal` | Not used | Encrypted Azure SQL connection using `Authentication=Active Directory Managed Identity`, matching database, no password, no trusted-server-certificate bypass |
| `Signatures:PortalBaseUri` | Exact HTTPS portal origin ending `/` | Same exact HTTPS origin |
| `Signatures:BlobContainerUri` | Dedicated private Azure container URI | Same container, read-only identity |
| `Signatures:PinKeyUri` | Dedicated versioned Azure Key Vault key URI | Same PIN key URI; unwrap-only identity |
| `Signatures:OutboxKeyUri` | A different dedicated versioned key URI | Leave unset; deny access |
| `Signatures:EmailEnabled` | Default `false`; deliberate synthetic email rehearsal only | Not used |
| `Signatures:EmailEndpoint`, `EmailSender` | Verified ACS resource/sender, only when email enabled | Not used |
| `Signatures:AllowedTestRecipients` | Exact approved test addresses; no wildcard or domain-wide permission | Not used |
| `AllowedHosts` | Existing API setting | Exact portal host; default only `localhost` |
| `Portal:TrustedProxyAddresses` | Not used | Explicit immediate proxy addresses if HTTPS terminates there; empty means trust no forwarded scheme or client address |

The environment values inside `Signatures` cannot override `Sati`'s validated environment.
The portal checks the actual database environment view at enabled startup. The only test bypass
requires an injected SQLite provider, the Testing host environment, and the exact Testing identity.
Do not expose a test host on the internet. Local developer secrets and a distributed desktop
connection to Azure SQL are not alternatives to the intended identities.

## Before enabling a synthetic rehearsal

1. Review/apply the controlled schema deployment with an authorized migration identity. Use
   `DATABASE_ENVIRONMENTS.md` to provision the environment identity first. Do not grant migration
   rights to the public portal. Do not open SQL firewall access automatically.
2. Review the SQL grant script against an isolated database and verify the actual deployed identity
   cannot read People, Notes, Users, general audit, chat or outbox data, alter signing identities,
   rewrite evidence, delete history, or write frozen/package records.
3. Confirm the container is private and prevent overwrite/delete through the deployed identities.
   Apply the approved retention/hold settings before real data. The application checks content
   hashes at freeze, read and package creation; a derived signed PDF has its own hash.
4. Retain both vault key histories. Test backup recovery of SQL, original PDFs, signed PDFs,
   wrapped keys and evidence together. Losing either key history can make recovery impossible.
5. Serve HTTPS, set exact allowed hosts and trusted proxies, and configure the external edge to
   reject unexpected hosts and bound requests. The application trusts no forwarding headers by
   default, uses secure cookies, and refuses plain HTTP without redirecting a secret-bearing URL.
   Preserve and protect the portal's antiforgery key ring across restarts and share it across
   instances of this environment only. Verify load-balanced requests and node replacement;
   database-backed signing sessions alone do not configure the host's antiforgery keys. A trusted
   proxy may forward the connection address for temporary rate limits; untrusted forwarding
   headers are ignored, and these addresses are not retained as signature evidence.
6. Disable or redact token-bearing paths at the edge, App Service, application monitoring,
   exception collection and support capture. The portal suppresses framework request/SQL/HTTP
   logs and does not use analytics, external assets, raw IP/user-agent evidence, or browser storage.
   Hosting logs and monitoring configured outside this repository still require verification.
7. Establish an appropriate operator alert for package/notification failures, backlog and disabled
   workers. The API emits content-free failure warnings; an external alert destination is not
   configured by this implementation.
   PDF generation was exercised on Windows with the existing server font configuration. A different
   server operating system needs an approved font resolver and its own PDF rendering acceptance.
8. Leave email suppressed until the sender/domain is verified. Review the current SPF, DKIM and
   DMARC records before making any change; do not blindly use historical DNS observations in the
   handoff. Test authentication headers in an approved external test mailbox. No mail was sent
   and no DNS was changed while implementing the feature.

## Recovery and accurate delivery status

The API prepares signed packages from durable completion records, then queues an encrypted
receipt notification. It uses short-lived database contexts and does not automatically replay a
write. A failed earlier package does not block every later one. An interrupted blob write/database
commit can leave an unreferenced immutable object; preserve and review it rather than automatically
purging it. The next attempt writes a new path and leaves the signed decision unchanged.

Notification processing saves its operation identity before calling ACS. After an uncertain
submission, it polls that identity rather than issuing a new email request. Durable leases and
revisions coordinate workers. Failed or exhausted work remains visible for staff review. Provider
acceptance is **not** proof of arrival, reading, signature or receipt by the person. Delivery/bounce
event integration and deployed alert wiring remain external activation work; never label a
provider-accepted message “delivered.”

The signing invitation expires after 72 hours by default (24–168 hours allowed). Authenticated
sessions last at most 30 minutes, with an explicit extension for unfinished signing bounded by
the invitation's expiry. Five wrong codes durably lock the request. A replacement uses a new
invitation and a different code; old links and sessions stay invalid.

After signing, `/s/` can no longer authenticate. The separate `/r/` receipt link requires the same
code and opens only the signed-copy session, while the invitation is still unexpired and unlocked.
It never reopens signing. Staff can retrieve the retained original and signed package after that
link expires. Establish a reliable free-paper/accessible-copy procedure for later requests.

Each page must submit its displayed session binding with decisions and downloads. It is a
non-secret correlation value, not a login token. If another tab replaces the shared sign-in
cookie, a stale page cannot act on that other document. A changed signer record stops old external
receipt access and pending copy mail, while retaining the signed history and staff copies.

No automatic purge, retention expiry, legal-hold release, state submission, supervisor signature,
billing approval or ordinary form-completion attestation is introduced by this feature.
