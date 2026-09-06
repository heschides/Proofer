# Team chat: implementation, agency decisions, and compliance guide

Prepared September 5, 2026. This guide explains the reviewed design and the boundaries for the
initial build. Test results and remaining technical checks are recorded separately in
`TEAM_CHAT_VALIDATION.md`. This is engineering and product guidance, not a legal opinion or a
representation that Sati is HIPAA compliant. Qualified Maine counsel and the agency's privacy and
security officers must determine which obligations apply to the actual organization and services.

## 1. What the feature is for

Staff can use named rooms to coordinate work inside Sati. An administrator chooses the people in
each room. Members can post and read messages. Rooms can be closed to new messages while keeping
their history. An authorized supervisor or administrator can hide a mistaken message, with a
reason, while preserving the original for an appropriately controlled investigation.

There are two uses. General coordination rooms are for matters such as meeting arrangements and
workplace questions, without client details. A discussion about an individual consumer belongs in
a room linked to that person. Sati checks both room membership and the person's existing access
restrictions. Being invited to a room does not give someone new access to a consumer's record.

This is narrower than the first design. A worker cannot use chat to bypass a caseload boundary by
inviting a colleague. If legitimate cross-caseload care coordination is needed, establish an
approved access process first. Do not solve it by making all agency staff able to see all clients.

The first version has no private direct messages, attachments, email delivery, automatic copying
into service notes, or AI analysis of messages. Staff presence tracking and typing indicators are
left out. Chat is not an emergency communication service.

Updates arrive while the chat workspace is open and visible. Outside chat, its unread indicator
shows the last refreshed count. Staff should not treat it as a guaranteed immediate alert.

## 2. What the safest defaults mean in daily use

| Default | What it means for staff |
|---|---|
| No automatically joined all-staff room | A new account does not silently receive access to everyone's conversations. |
| Only named room members may read | An administrator's ability to manage a room does not by itself permit reading it. |
| Existing consumer access still applies | A room invitation cannot open a client's information that Sati otherwise restricts. |
| New members see later messages | Adding or re-adding a person does not silently disclose old history. |
| Original posts are preserved | A correction hides the text from ordinary readers but does not erase the original evidence. |
| No automatic deletion | A message does not disappear merely because it is old. An approved disposal policy is still needed. |
| No desktop message previews | Client information is not copied into pop-up notifications. |
| No saved chat drafts or message files | Sati does not create a separate chat cache on the workstation. Ordinary device protection is still necessary. |
| Real-data use is not enabled | The first implementation is for explicitly enabled synthetic-data testing. |

The general-room rule is an agency policy supported by the interface. The program cannot reliably
tell whether arbitrary typed text contains confidential information. Do not describe those rooms
as guaranteed to be free of client information. Use fictional people and fictional content during
the initial testing.

## 3. What was wrong with the first design

The proposed activity history could miss the last messages someone viewed. It waited for another
action several minutes later; if that action never happened, the history stayed incomplete. The
replacement records the particular messages the system makes available before returning them.

The proposed recovery process could miss a delayed message or a correction to an old message.
The replacement keeps a lasting ordered history of new posts and corrections. A connection failure
can delay updates, but recovery is based on that saved history rather than a guess about timing.

The proposed access rules were too broad. A supervisor could add themselves to other rooms in the
agency. The replacement separates room management from permission to read and keeps existing
consumer restrictions. New members do not inherit past conversations automatically.

Two statements were also too strong: working for the same agency does not settle the right to
receive every client's information, and calling chat something other than a clinical record does
not remove records-access obligations. The guide below explains those limits.

## 4. Decide who is responsible before real-client use

Name the person who owns privacy decisions, the person who owns security and technical operations,
and the person responsible for records requests and preservation. In a small agency, one person
may hold several duties, but those duties must still be explicit.

Determine whether the agency is a HIPAA-covered organization and what role Sati's operating company
has when it stores or supports protected information. Review the necessary business associate
agreements and the actual cloud services, support access, backups, and responsibilities. Using
Azure does not settle this. HHS explains that cloud services holding protected information
generally require the relevant contractual protections even when they cannot decrypt it.
[HHS cloud guidance](https://www.hhs.gov/hipaa/for-professionals/special-topics/health-information-technology/cloud-computing/index.html)

Keep a written approval record for the actual agency and configuration. Successful software tests
do not substitute for the agency accepting its responsibilities.

## 5. Approve the audience for each kind of conversation

HIPAA generally requires internal access policies to identify which workers need which
information for their duties. Its treatment exception to the minimum-necessary rule concerns
certain disclosures to, and requests by, health care providers; it is not blanket permission for
all coworkers to browse every client's information.
[HHS minimum-necessary guidance](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/minimum-necessary-requirement/index.html)

Write down the purpose of each room, who may join, who approves membership, how access is reviewed,
and how quickly it ends when duties change. Use neutral room names where possible. Keep consumer
discussions focused on one person so that other clients' information is not mixed into the same
conversation. Staff should check the room and its participants before posting.
The room also displays the linked consumer's name and record number so that a neutral room name
is not the only clue to whose information is being discussed.

The chosen Admin-only membership control is a conservative product default, not a law requiring
that exact job title. If the agency later delegates room management, that delegation needs a
specific limit and an activity record. Likewise, adding someone to a room is an administrative
action, not a consumer's authorization to disclose.

## 6. Check Maine and specially protected information

Maine's general health-information confidentiality law contains permitted disclosure grounds and
requires safeguards and limits tied to purpose. Its reach depends on the provider and information;
some categories are governed by other laws. Have counsel review the agency's actual services and
sharing arrangements against [22 M.R.S. §1711-C](https://legislature.maine.gov/statutes/22/title22sec1711-C.html).

Ask specifically about developmental and behavioral health services, minors, guardians, HIV
information, and records received with restrictions. Relevant starting points include
[34-B M.R.S. §1207](https://legislature.maine.gov/statutes/34-B/title34-Bsec1207.html),
[34-B M.R.S. §5605, including subsection 15](https://legislature.maine.gov/statutes/34-B/title34-Bsec5605.html),
and [Maine's HIV-test confidentiality law](https://legislature.maine.gov/statutes/5/title5sec19203.html).
Applicable mental-health recipient-rights rules may include 14-193 Chapter 1 or the children's
14-472 Chapter 1. Use the [state's current rule index](https://www.maine.gov/sos/rulemaking/agency-rules/department-health-and-human-services-rules)
and check the program's licensing and funding arrangements.

Substance-use treatment records may fall under 42 CFR Part 2. The revised rule's general compliance
deadline was February 16, 2026. A qualifying consent can permit specified future uses and sharing,
but restrictions remain, including special treatment of certain counseling notes and proceedings
against a patient. Not every mention of substance use automatically becomes a Part 2 record.
[HHS Part 2 explanation](https://www.hhs.gov/hipaa/for-professionals/regulatory-initiatives/fact-sheet-42-cfr-part-2-final-rule/index.html)

The safest launch policy is to exclude specially restricted records from ordinary chat until
privacy staff have mapped the relevant programs, incoming records, sharing grounds, consent and
revocation requirements. Sati's ordinary room membership check does not perform that analysis.
Psychotherapy notes also have special HIPAA protections; they are different from ordinary
mental-health information. [HHS mental-health guidance](https://www.hhs.gov/hipaa/for-professionals/faq/2088/does-hipaa-provide-extra-protections-mental-health-information-compared-other-health.html)

## 7. Treat chat as potentially obtainable records

Under HIPAA, the collection of records a person may obtain includes specified medical, billing
and case-management records, and other records used to make decisions about that person. A care
decision made in chat may therefore matter even if staff were told to make a service note too.
[HHS explanation of accessible information](https://www.hhs.gov/hipaa/for-professionals/faq/2042/what-personal-health-information-do-individuals/index.html)

The interface accordingly says that chat does not replace service notes and that messages may be
retained and included in records requests. Staff must still document services and decisions in
the correct part of Sati. Posting a message does not submit a note, authorize a service, approve
billing, or establish a signature.

Before real use, decide how staff will find a person's relevant conversations, including messages
put in the wrong room, general rooms, messages mentioning several clients, and hidden originals.
Assign a controlled process for review and release, protecting other people's information.
Do not rely on automatic name recognition or assume that linking the room finds every relevant
message. A secure export/recovery process and response deadlines need agency approval.

## 8. Approve retention and preservation separately

HIPAA does not prescribe a general medical-record retention period. Its six-year requirements
for certain compliance documents are not a universal instruction to keep every chat message for
six years. Other laws, contracts and program requirements may set different periods.
[HHS retention explanation](https://www.hhs.gov/hipaa/for-professionals/faq/580/does-hipaa-require-covered-entities-to-keep-medical-records-for-any-period/index.html)

The current MaineCare Chapter I, §1.03-8(M)(3), requires relevant service records for at least five
years from the service, longer where other statutes require, and through completion and settlement
of an audit begun during that period. That does not automatically establish a disposal date for
every chat message. Review [current MaineCare Chapter I](https://www.maine.gov/sos/sites/maine.gov.sos/files/inline-files/c1s001.docx)
and the applicable service chapter. Current Section 13 is the 2026 replacement; documentation is
addressed in §13.09. [Current Section 13](https://www.maine.gov/sos/sites/maine.gov.sos/files/inline-files/c2s013-2026-093%20RPR.docx)

Approve a written schedule stating which messages fall into which record category, the starting
date for each period, who may approve disposal, and which longer obligations override it. Include
membership history, correction reasons, release history, exported copies and backups.

The initial default is no automatic deletion. This preserves evidence while the policy is
unsettled; it is not a recommendation to retain everything forever. A litigation hold, complaint,
audit or other preservation duty must stop disposal of relevant material, including backups.
Sati's existing person-specific hold feature does not yet provide that complete chat process.
Linking a room to a consumer helps prevent destructive deletion but does not solve all discovery.

## 9. Understand what the activity history does and does not prove

The revised feature records which message text the system made available to an authorized user.
It separately records membership changes and hiding a message. The original posted message
preserves its author and time. General activity records do not duplicate message narratives.

This does not prove that a person read or understood the text, that a screen was private, or that
the disclosure was legally appropriate. It is also not automatically the legal accounting of
disclosures that may be required for particular circumstances. Privacy staff must determine that
separately. Someone must regularly examine activity and investigate suspicious access.

HIPAA security obligations include risk assessment, workforce access procedures, activity review,
incident handling and continuity arrangements. The rule does not prescribe this feature's exact
number of activity entries or its update interval.
[HHS Security Rule summary](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## 10. Handle a wrong-room message as a possible privacy incident

Staff should immediately report a mistaken disclosure through the agency's approved internal
process. An authorized person can hide the message to reduce further exposure. Preserve the
original, the reason for hiding it, the membership history and the available access evidence.
Hiding text cannot erase a copy somebody already saw or obtained.

The privacy officer should assess the information, recipients, actual acquisition or viewing,
mitigation and applicable exceptions. HIPAA generally treats an impermissible use or disclosure
as a breach unless an exception or a documented low-probability assessment supports another
conclusion. Applicable notification deadlines can include a 60-day outer limit; contracts or
other laws may require faster action. Staff should report immediately, not wait for that limit.
[HHS breach-notification guidance](https://www.hhs.gov/hipaa/for-professionals/breach-notification/index.html)

This work does not add a client reportable-incident workflow. Sati's existing operational error
reporting is also not that workflow. The agency must maintain its established reporting process.

## 11. Prepare workstations, accounts and daily practice

Require individual accounts and agency-approved devices. Review sign-in strength, account removal,
lost-device procedures, protected storage, updates, network access, backup restoration and support
access. A privacy screen is a visual cover; it is not the same as locking Windows or requiring
credentials to reopen the session. Use the organization's actual screen-lock policy as well.

The feature clears its own old messages and drafts during account changes and stops requesting
hidden content. This cannot erase screenshots, clipboard copies, previously released records,
memory captured by a compromised device, or an unauthorized photograph of a screen.

The repository still lacks a complete account-disable and immediate session-revocation workflow.
Removing chat membership restricts chat, but resetting a password must not be described as ending
every already-open session. Resolve the broader account lifecycle before real-client rollout.

Train staff on room selection, audience checks, documenting services elsewhere, reporting mistakes,
handling records requests and avoiding sensitive content in general room names/descriptions.
Set response expectations and off-duty rules. Use the established emergency and urgent-care
channels when immediate action is needed.

For employees entitled to hourly and overtime protections, time spent responding to work messages
outside scheduled hours can be paid work. Provide a practical way to report that time, and have
the agency review federal and Maine wage rules for its staff. A rule against unauthorized overtime
does not by itself make known work unpaid. [U.S. Department of Labor guidance](https://www.dol.gov/newsroom/releases/whd/whd20200824)

Tell workers what activity is recorded, who may review retained conversations, and how an
investigation is authorized. Do not promise that workplace chat is private from every agency
records or privacy process.

## 12. Test accessibility with the people who will use it

Test the complete conversation, room-selection and administration workflows using only a keyboard,
a screen reader, larger text, the agency's display settings and high-contrast needs. New-message
announcements should not continually interrupt reading or reveal text while a screen is covered.
Staff must be able to recognize unread items without distinguishing colors.

Workplace accommodation obligations can apply to staff software.
[EEOC accommodation guidance](https://www.eeoc.gov/laws/guidance/enforcement-guidance-reasonable-accommodation-and-undue-hardship-under-ada)
Do not automatically apply public website/mobile deadlines to a Windows staff application. If a
future web or mobile version falls under relevant rules, evaluate those separately. The HHS
Section 504 web/mobile dates were extended in 2026; the state/local government web rule also has
its own scope and dates. [HHS extension](https://www.hhs.gov/press-room/hhs-extends-mobile-and-web-accessibility-deadline.html),
[DOJ explanation](https://www.ada.gov/resources/2024-03-08-web-rule/)

## 13. A practical rollout order

1. **Finish technical acceptance with fictional data.** Verify room access, membership removal,
   new-member history limits, repeated sends, interrupted connections, corrections, preserved
   records and privacy-screen/account switching. Review the recorded automated-test evidence.
2. **Rehearse the database update and restoration.** Use a new disposable synthetic database and
   a synthetic upgrade copy. Confirm the existing notes, forms, scheduling and billing still work.
3. **Deploy only to the separate Demo environment when authorized.** Apply the reviewed database
   update through the controlled process. Explicitly enable synthetic chat; test two workstations
   and a network that blocks prompt notifications. No real clients belong in Demo.
4. **Complete the agency decisions above.** Record the named owners, approved audience, sensitive
   records rules, records-request process, retention/preservation, contracts, incident procedures,
   training and accessibility acceptance.
5. **Resolve the real-data platform gaps.** Complete approved API-backed Production, account
   suspension/session revocation, controlled retained-message recovery, broad preservation including
   backups, monitoring and restore evidence. Local Production remains a single-workstation mode
   without this team chat feature.
6. **Authorize a limited real-data pilot separately.** Review the final configuration and test
   evidence with agency stakeholders and qualified Maine counsel. A successful Demo is useful
   evidence but does not itself approve real-client use.

No release, cloud database change, production deployment, or security-setting change is authorized
merely by this guide. Its purpose is to make the remaining work explicit and reviewable.
