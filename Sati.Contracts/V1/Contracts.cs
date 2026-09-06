namespace Sati.Contracts.V1;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    UserProfileDto User);

public sealed record SessionRenewalResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record UserProfileDto(
    int Id,
    string Username,
    string DisplayName,
    string Role,
    UserPermissions Permissions,
    int? SupervisorId,
    int AgencyId,
    string? Email,
    string? Phone);

public sealed record CreateUserRequest(
    string Username, string DisplayName, UserPermissions Permissions, int? SupervisorId,
    int AgencyId, string? Email, string? Phone, string InitialPassword);

public sealed record SaveUserRequest(
    string Username, string DisplayName, UserPermissions Permissions, int? SupervisorId,
    int AgencyId, string? Email, string? Phone);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record FormDto(
    int Id,
    string Type,
    DateTime DueDate,
    bool IsCompliant,
    int PersonId,
    DateTime? CompletedDate,
    DateTime? OpenedDate);

public sealed record NoteSummaryDto(
    string? Status,
    DateTime? EventDate,
    string? NoteType);

public sealed record PersonDto(
    int Id,
    int UserId,
    string? FirstName,
    string? LastName,
    DateTime BirthDate,
    string Gender,
    DateTime? EffectiveDate,
    string? Bio,
    string Waiver,
    int? AgencyId,
    string? MaineCareId,
    string? DiagnosisCode,
    int? PlaceOfService,
    string? EvergreenId,
    bool OpenWithVR,
    bool HasGuardian,
    string? GuardianName,
    string? PhoneNumber,
    string? Address,
    string? BillingStreet,
    string? BillingCity,
    string? BillingState,
    string? BillingZip,
    string? PrimaryCareProvider,
    string? HealthcareSystemName,
    bool HasHomeSupport,
    bool HasSelfDirectedHomeSupport,
    bool HasSharedLiving,
    bool HasCommunitySupport1To1,
    bool HasCommunitySupportSelfDirected,
    bool HasCommunitySupportDayProgram,
    int DayProgramCount,
    bool HasEmploymentSpecialist,
    bool HasWorkSupports,
    bool IsEmployed,
    int Revision,
    IReadOnlyList<FormDto> Forms,
    IReadOnlyList<NoteSummaryDto> Notes,
    bool CaseManagerIsRepPayee = false,
    decimal? RepPayeeMonthlyIncome = null,
    string? RepPayeeRegularCheckRequestNeeds = null,
    bool CaseManagerIsDhhsRepresentative = false,
    bool UsesModivcare = false,
    string? Email = null,
    bool IsTestData = false,
    string? CredibleClientId = null,
    string? VrCounselorName = null,
    string? VrAssistantName = null,
    DateTime CreatedAtUtc = default,
    string Status = "Active",
    string? StatusNote = null,
    DateTime? StatusChangedAtUtc = null,
    int? StatusChangedByUserId = null);

public sealed record SavePersonFormRequest(
    int Id,
    string Type,
    bool IsCompliant,
    DateTime? CompletedDate,
    DateTime? OpenedDate);

public sealed record SavePersonRequest(
    string FirstName,
    string LastName,
    DateTime BirthDate,
    string Gender,
    DateTime? EffectiveDate,
    string? Bio,
    string Waiver,
    string? MaineCareId,
    string? DiagnosisCode,
    int? PlaceOfService,
    string? EvergreenId,
    bool OpenWithVR,
    bool HasGuardian,
    string? GuardianName,
    string? PhoneNumber,
    string? Address,
    string? BillingStreet,
    string? BillingCity,
    string? BillingState,
    string? BillingZip,
    string? PrimaryCareProvider,
    string? HealthcareSystemName,
    bool HasHomeSupport,
    bool HasSelfDirectedHomeSupport,
    bool HasSharedLiving,
    bool HasCommunitySupport1To1,
    bool HasCommunitySupportSelfDirected,
    bool HasCommunitySupportDayProgram,
    int DayProgramCount,
    bool HasEmploymentSpecialist,
    bool HasWorkSupports,
    bool IsEmployed,
    IReadOnlyList<SavePersonFormRequest> Forms,
    int ExpectedRevision = 0,
    bool UpdateBillingAddress = false,
    bool CaseManagerIsRepPayee = false,
    decimal? RepPayeeMonthlyIncome = null,
    string? RepPayeeRegularCheckRequestNeeds = null,
    bool CaseManagerIsDhhsRepresentative = false,
    bool UsesModivcare = false,
    string? Email = null,
    bool IsTestData = false,
    string? CredibleClientId = null,
    string? VrCounselorName = null,
    string? VrAssistantName = null);

/// <summary>
/// Moves one consumer to another case manager's caseload.
///
/// <para>
/// <paramref name="ExpectedRevision"/> is not optional in practice: a supervisor distributing an
/// imported batch and a case manager editing the same consumer's profile are exactly the
/// concurrent pair this record has to lose to rather than overwrite. A mismatch answers with the
/// same <c>stale_person</c> conflict a profile save does.
/// </para>
/// </summary>
public sealed record TransferCaseloadRequest(int TargetUserId, int ExpectedRevision);

/// <summary>
/// Moves a consumer between <c>PersonStatusRules</c> statuses. <see cref="Status"/> is one of
/// <c>PersonStatusRules.AllStatuses</c>. See HANDOFF_CLIENT_DELETION_POLICY.md's archive
/// semantics.
/// </summary>
public sealed record SetPersonStatusRequest(string Status, string? Note, int ExpectedRevision);

/// <summary>The consumer's archive status as it stands after a change.</summary>
public sealed record PersonStatusDto(int PersonId, string Status, string? StatusNote, int Revision);

/// <summary>
/// Asks which of these consumers the agency already holds, checked in the same precedence order
/// as import matching: Credible client id, then MaineCare id, then normalized name and birth
/// date. See CREDIBLE_IMPORT_DESIGN.md's "client_id is the dedupe key" section.
///
/// <para>
/// A POST rather than a query string on purpose. These are identifiers for real people, and a
/// query string is the one part of a request that reliably reaches access logs, proxies and
/// browser history. The body keeps them out of all three.
/// </para>
/// </summary>
public sealed record CredibleClientLookupRequest(
    IReadOnlyList<string> CredibleClientIds,
    IReadOnlyList<string>? MaineCareIds = null,
    IReadOnlyList<PersonNameBirthDate>? NameBirthDates = null);

/// <summary>One consumer's last name, first name and birth date, for the name+DOB match tier.</summary>
public sealed record PersonNameBirthDate(string LastName, string FirstName, DateTime BirthDate);

/// <summary>
/// One Credible id the agency already holds.
///
/// <para>
/// Deliberately thin. It answers "already imported?" and nothing else — no person id, no name,
/// no date of birth. <paramref name="OwnerDisplayName"/> is filled only when the caller could
/// already see that caseload, so a plain case manager learns that an id is taken without
/// learning whose consumer it is.
/// </para>
/// </summary>
public sealed record CredibleClientMatchDto(string CredibleClientId, string? OwnerDisplayName);

/// <summary>One MaineCare id the agency already holds. Same disclosure rule as <see cref="CredibleClientMatchDto"/>.</summary>
public sealed record MaineCareIdMatchDto(string MaineCareId, string? OwnerDisplayName);

/// <summary>One name+birth-date pair the agency already holds. Same disclosure rule as <see cref="CredibleClientMatchDto"/>.</summary>
public sealed record NameBirthDateMatchDto(PersonNameBirthDate NameBirthDate, string? OwnerDisplayName);

/// <summary>
/// The three match tiers, each independent. A caller correlates a submitted identifier back to
/// its own row locally — the response never says which submitted row triggered a match, only
/// which values are already held, exactly as the single-tier <see cref="CredibleClientMatchDto"/>
/// always has.
/// </summary>
public sealed record CredibleMatchLookupResult(
    IReadOnlyList<CredibleClientMatchDto> CredibleClientMatches,
    IReadOnlyList<MaineCareIdMatchDto> MaineCareIdMatches,
    IReadOnlyList<NameBirthDateMatchDto> NameBirthDateMatches)
{
    public static readonly CredibleMatchLookupResult Empty = new([], [], []);
}

/// <summary>The consumer's ownership as it stands after a transfer.</summary>
public sealed record CaseloadOwnershipDto(int PersonId, int UserId, int Revision);

public sealed record PersonContactDto(
    int Id,
    int PersonId,
    string FirstName,
    string LastName,
    string Kind,
    string? Relationship,
    string? Organization,
    string? Phone,
    string? Email,
    bool IsEmergencyContact,
    bool HasActiveRelease,
    bool IsActive);

public sealed record SavePersonContactRequest(
    string FirstName,
    string LastName,
    string Kind,
    string? Relationship,
    string? Organization,
    string? Phone,
    string? Email,
    bool IsEmergencyContact,
    bool HasActiveRelease);

// One consumer's link to a directory entry. It carries the relationship's own fields and
// nothing derived: the practice and the network are resolved from the directory the caller
// already holds, so a payload can never disagree with the directory it was built from.
// EndDate alone says whether the link is current; there is no separate active flag.
public sealed record ConsumerProviderDto(
    int Id,
    int PersonId,
    int ProviderId,
    string? Role,
    bool IsPrimaryCare,
    DateTime? StartDate,
    DateTime? EndDate,
    bool HasActiveRelease,
    int SortOrder);

public sealed record SaveConsumerProviderRequest(
    int ProviderId,
    string? Role,
    bool IsPrimaryCare,
    DateTime? StartDate,
    DateTime? EndDate,
    bool HasActiveRelease,
    int SortOrder);

public sealed record NoteDto(
    int Id,
    string Narrative,
    DateTime? EventDate,
    string? Status,
    int? Minutes,
    int? StartTime,
    int PersonId,
    string? FormType,
    string? NoteType,
    int? AgencyId,
    string? ReturnReason,
    int? ReturnedById,
    int? ApprovedById,
    DateTime? ApprovedAt,
    DateTime? ReturnedAt,
    string? CaseManagerJustification,
    string? VisitDocumentationJson,
    bool ComplianceOverride,
    string? OverrideReason,
    int? OverrideApprovedById,
    DateTime? OverrideApprovedAt,
    int Revision,
    PersonReferenceDto? Person,
    IReadOnlyList<string>? ComplianceFailureReasons = null);

public sealed record SaveNoteRequest(
    string Narrative,
    DateTime? EventDate,
    string? Status,
    int? Minutes,
    int? StartTime,
    int PersonId,
    string? FormType,
    string? NoteType,
    string? CaseManagerJustification,
    string? VisitDocumentationJson,
    int ExpectedRevision = 0);

public sealed record PersonReferenceDto(int Id, int UserId, string? FirstName, string? LastName);

public sealed record SupervisorNoteActionRequest(string? Reason, int ExpectedRevision = 0, int? MaximumUnits = null);

public sealed record SettingsDto(
    int Id,
    int AbandonedAfterDays,
    int ProductivityThreshold,
    decimal BaseIncentive,
    decimal PerUnitIncentive,
    decimal PassthroughRate,
    decimal SalesTaxRate,
    int? DefaultPassthroughProviderId,
    string VisitTemplate,
    string ContactTemplate,
    string DocumentationTemplate,
    string HealthcareSystemsJson,
    bool ExcludeMonday,
    bool ExcludeTuesday,
    bool ExcludeWednesday,
    bool ExcludeThursday,
    bool ExcludeFriday,
    bool ExcludeNewYearsDay,
    bool ExcludeMLKDay,
    bool ExcludePresidentsDay,
    bool ExcludeMemorialDay,
    bool ExcludeJuneteenth,
    bool ExcludeIndependenceDay,
    bool ExcludeLaborDay,
    bool ExcludeIndigenousPeoplesDay,
    bool ExcludeVeteransDay,
    bool ExcludeThanksgiving,
    bool ExcludeDayAfterThanksgiving,
    bool ExcludeChristmas,
    int ReviewOpenDaysBefore,
    int ReviewDaysAfterDue,
    int PcpOpenDaysBefore,
    int PcpDaysAfterDue,
    int CompAssessmentOpenDaysBefore,
    int CompAssessmentDaysAfterDue,
    int ReclassificationOpenDaysBefore,
    int ReclassificationDaysAfterDue,
    int SafetyPlanOpenDaysBefore,
    int SafetyPlanDaysAfterDue,
    int PrivacyPracticesOpenDaysBefore,
    int PrivacyPracticesDaysAfterDue,
    int ReleaseAgencyOpenDaysBefore,
    int ReleaseAgencyDaysAfterDue,
    int ReleaseDhhsOpenDaysBefore,
    int ReleaseDhhsDaysAfterDue,
    int ReleaseMedicalOpenDaysBefore,
    int ReleaseMedicalDaysAfterDue,
    int Q4RDaysBeforeAnniversary,
    int PcpDaysBeforeAnniversary,
    int CompAssessmentDaysBeforeAnniversary,
    int ReclassificationDaysBeforeAnniversary,
    int SafetyPlanDaysBeforeAnniversary,
    int PrivacyPracticesDaysBeforeAnniversary,
    int ReleaseAgencyDaysBeforeAnniversary,
    int ReleaseDhhsDaysBeforeAnniversary,
    int ReleaseMedicalDaysBeforeAnniversary,
    int Revision = 0,
    BillingComplianceRequirements BillingComplianceRequirements =
        BillingComplianceGate.DefaultRequirements,
    bool AllowCredibleProfileUpdates = false,
    string VrAssistantTitle = VocationalRehabilitationProfile.DefaultAssistantTitle,
    int AnnualPacketOpenDaysBefore = AnnualPacketWindow.DefaultOpenDays);

public sealed record ScratchpadDto(
    int Id,
    int UserId,
    DateTime Date,
    string Content,
    IReadOnlyList<ScratchpadCommentDto> Comments,
    int Revision);

public sealed record ScratchpadCommentDto(
    int Id,
    int ScratchpadId,
    int AuthorUserId,
    string AuthorDisplayName,
    DateTime CreatedAtUtc,
    string Content);

public sealed record SaveScratchpadRequest(int Id, string Content, int ExpectedRevision = 0);
public sealed record AddScratchpadCommentRequest(string Content);
public sealed record SaveJournalRequest(string? Journal);

// The entry's timestamp is deliberately absent: the server stamps it from the
// agency clock so the record cannot claim a moment the caller invented.
// See Sati.Contracts.V1.JournalEntry.
public sealed record AddJournalReminderRequest(string Text);

public sealed record ExemptDateDto(int Id, DateTime Date, string? Reason);
public sealed record AddExemptDateRequest(DateTime Date, string? Reason);

public sealed record IncentiveDto(
    int Id,
    int UserId,
    int Month,
    int Year,
    int DaysScheduled,
    decimal BaseIncentive,
    decimal PerUnitIncentive,
    int UnitsPerDay,
    string ExcludedDatesJson);

public sealed record IncentiveEnvelopeDto(IncentiveDto Incentive, bool WasCreated);

public sealed record DateSetRequest(IReadOnlyList<DateTime> Dates);
public sealed record RemainingEligibleDaysRequest(
    int Month,
    int Year,
    IReadOnlyList<DateTime> DaysAlreadyWorked,
    IReadOnlyList<DateTime> ExemptDates);
public sealed record DateWindowRequest(DateTime StartInclusive, DateTime EndInclusive);
public sealed record CountDto(int Count);

public sealed record AuditEventDto(
    long Id,
    Guid EventId,
    int AgencyId,
    int ActorUserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    DateTime OccurredAtUtc,
    string CorrelationId);

public sealed record PersonFieldChangeDto(
    string Field,
    string Label,
    string? PreviousValue,
    string? NewValue);

public sealed record PersonVersionDto(
    long Id,
    int PersonId,
    int Version,
    string ChangeKind,
    int ActorUserId,
    string ActorDisplayName,
    DateTime ChangedAtUtc,
    string CorrelationId,
    IReadOnlyList<PersonFieldChangeDto> Changes);

public sealed record AdminOverviewDto(
    int AgencyId,
    string AgencyName,
    int UserCount,
    int CaseManagerCount,
    int PersonCount,
    int NotesThisMonth,
    int ActiveUsersLast30Days,
    int SuccessfulSignInsLast30Days,
    int PersonChangesLast30Days,
    int AuditEventsToday,
    DateTime? LastActivityUtc);

public static class OperationalPolicyDefaults
{
    public const int AuditRetentionDays = 2_555;
    public const int EdiReplayRetentionDays = 90;
}

public sealed record AdminOperationsDto(
    DateTime ObservedAtUtc,
    string DatabaseStatus,
    string RetentionEnforcementMode,
    int AuditRetentionDays,
    int EdiReplayRetentionDays,
    long AuditEventCount,
    long EdiReplayCount,
    long EdiReplayCharacters,
    DateTime? OldestAuditEventUtc,
    DateTime? OldestEdiReplayUtc);

public sealed record AdminAuditExportRequest(
    DateTime FromUtc,
    DateTime ToUtc,
    string Reason);

public sealed record AdminPersonListItemDto(
    int PersonId,
    string DisplayName,
    int Revision,
    int AssignedUserId,
    string AssignedUserDisplayName,
    bool IsTestData = false,
    DateTime CreatedAtUtc = default,
    string Status = "Active");

public sealed record DeleteTestConsumerRequest(
    int ExpectedRevision,
    string Attestation);

public sealed record TestConsumerDeletionResultDto(
    int PersonId,
    int FormsDeleted,
    int NotesDeleted,
    int ContactsDeleted,
    int ReviewsDeleted,
    int AppointmentsDeleted,
    int AssessmentsDeleted,
    int AtRequestsDeleted,
    int AtRequestItemsDeleted,
    int PersonVersionsDeleted,
    int PersonProvidersDeleted = 0,
    int FormAttestationsDeleted = 0,
    int DocumentArtifactsDeleted = 0,
    int SafetyPlansDeleted = 0,
    int DocumentAcknowledgmentsDeleted = 0)
{
    public int RelatedRecordsDeleted =>
        FormsDeleted + NotesDeleted + ContactsDeleted + ReviewsDeleted +
        AppointmentsDeleted + AssessmentsDeleted + AtRequestsDeleted +
        AtRequestItemsDeleted + PersonVersionsDeleted + PersonProvidersDeleted +
        FormAttestationsDeleted + DocumentArtifactsDeleted + SafetyPlansDeleted + DocumentAcknowledgmentsDeleted;
}

/// <summary>Rule-3 deletion: delete a consumer created within the window. See <c>ConsumerDeletionRules</c>.</summary>
public sealed record DeleteConsumerInWindowRequest(int ExpectedRevision, string Attestation, string Reason);

/// <summary>
/// Counts of what rule-3 deletion removed, shown to the Admin before and after confirming. The
/// itemized inventory behind these counts lives only in the audit tombstone
/// (<c>consumer.deleted-in-window</c>), never in this response.
/// </summary>
public sealed record ConsumerDeletionResultDto(
    int PersonId,
    int FormsDeleted,
    int NotesDeleted,
    int ContactsDeleted,
    int ReviewsDeleted,
    int AppointmentsDeleted,
    int AssessmentsDeleted,
    int AtRequestsDeleted,
    int AtRequestItemsDeleted,
    int PersonVersionsDeleted,
    int PersonProvidersDeleted,
    int FormAttestationsDeleted,
    int DocumentArtifactsDeleted,
    int ClaimLinesDeleted,
    int SafetyPlansDeleted = 0,
    int DocumentAcknowledgmentsDeleted = 0)
{
    public int RelatedRecordsDeleted =>
        FormsDeleted + NotesDeleted + ContactsDeleted + ReviewsDeleted +
        AppointmentsDeleted + AssessmentsDeleted + AtRequestsDeleted +
        AtRequestItemsDeleted + PersonVersionsDeleted + PersonProvidersDeleted +
        FormAttestationsDeleted + DocumentArtifactsDeleted + ClaimLinesDeleted + SafetyPlansDeleted + DocumentAcknowledgmentsDeleted;
}

public sealed record AdminActivityDto(
    long Id,
    int ActorUserId,
    string ActorDisplayName,
    string Action,
    string ResourceType,
    string? ResourceId,
    DateTime OccurredAtUtc,
    string CorrelationId);

public sealed record ConsumerBillingLossRowDto(
    int PersonId,
    string ConsumerName,
    int BillableDays,
    int NonBillableDays,
    int BillableUnits,
    int NonBillableUnits,
    decimal? LostWorkPercentage);

public sealed record ConsumerBillingLossReportDto(
    IReadOnlyList<ConsumerBillingLossRowDto> Consumers,
    int TotalBillableUnits,
    int TotalNonBillableUnits,
    decimal? LostWorkPercentage);

public sealed record ProductivityMonthUnitsDto(
    int Year,
    int Month,
    int Units);

public sealed record BillingPeriodDto(
    int Id,
    int UserId,
    int Month,
    int Year,
    string Status,
    DateTime? SubmittedAt,
    IReadOnlyList<ClaimLineDto> Lines);

public sealed record ClaimLineDto(
    int Id,
    int NoteId,
    int BillingPeriodId,
    DateTime DateOfService,
    string ProcedureCode,
    string? ProcedureModifier,
    decimal? Units,
    decimal ChargeAmount,
    string ClientMaineCareId,
    string RenderingProviderNpi,
    string DiagnosisCode,
    int PlaceOfService,
    bool IsComplianceException,
    string? ComplianceExceptionReason);

public sealed record BillingConfigurationDto(
    string ProcedureCode,
    string? Modifier,
    decimal? UnitRate,
    string EdiSubmitterId,
    string PayerName,
    string PayerId,
    string ContactName,
    string ContactPhone);

public sealed record SaveBillingConfigurationRequest(
    string ProcedureCode,
    string? Modifier,
    decimal? UnitRate,
    string EdiSubmitterId,
    string PayerName,
    string PayerId,
    string ContactName,
    string ContactPhone);

public sealed record BillingCandidateDto(NoteDto Note, IReadOnlyList<string> Errors);
public sealed record CreateClaimLineRequest(int NoteId, bool IsComplianceException, string? ComplianceExceptionReason);
public sealed record GenerateEdiRequest(bool IsTest, string IdempotencyKey);
public sealed record EdiFileDto(string FileName, string Content);

public sealed record AppointmentDto(int Id, int ReviewItemId, DateTime Date, string? ProviderName);

public sealed record ReviewItemDto(
    int Id,
    int PersonId,
    DateTime CycleAnchor,
    int Quarter,
    string Category,
    int SlotIndex,
    DateTime? RequestedDate,
    DateTime? ReceivedDate,
    DateTime? LoggedDate,
    AppointmentDto? Appointment);

public sealed record EnsureReviewItemsRequest(IReadOnlyList<int> PersonIds, DateTime Today);
public sealed record SetReviewStageRequest(string Stage, DateTime? Date);
public sealed record SetAppointmentRequest(DateTime? Date, string? ProviderName);
public sealed record LatestAppointmentsDto(AppointmentDto? Medical, AppointmentDto? Dental);

public sealed record ComprehensiveAssessmentDto(
    int Id, int PersonId, int AuthorUserId, string Status, int Version,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? SubmittedAt,
    DateTime? ApprovedAt, int? ApprovedByUserId, string DocumentJson, int Revision);

public sealed record SaveAssessmentDocumentRequest(string DocumentJson, int ExpectedRevision);

public sealed record PersonCenteredPlanSourceDto(
    int AssessmentId, int Version, string Status, DateTime UpdatedAt, string DocumentJson);

// MedicalKind and ParentProviderId are optional trailing parameters for the same reason
// Npi and MaineCareProviderId were: adding them is additive, so an older client that omits
// them still round-trips. ProviderDto.Type stays the existing "Waiver"/"Healthcare"/"Other"
// string — flattening the tiers into it would have broken that value for no modelling gain.
public sealed record ProviderDto(
    int Id, string Type, string Name, string? Street, string? City, string? State, string? Zip,
    string? PrimaryContact, string? Phone, int OfferedServices, bool ProvidesPassthroughService,
    string? BillingLocationEis, string? ProgramContact, string? BillingContact,
    string? Npi = null, string? MaineCareProviderId = null,
    string? MedicalKind = null, int? ParentProviderId = null);

public sealed record SaveProviderRequest(
    string Type, string Name, string? Street, string? City, string? State, string? Zip,
    string? PrimaryContact, string? Phone, int OfferedServices, bool ProvidesPassthroughService,
    string? BillingLocationEis, string? ProgramContact, string? BillingContact,
    string? Npi = null, string? MaineCareProviderId = null,
    string? MedicalKind = null, int? ParentProviderId = null);

// A named person at a provider, distinct from the organization's general directory contact on
// ProviderDto.PrimaryContact/Phone. A directory entry accumulates several of these.
public sealed record ProviderContactDto(
    int Id, int ProviderId, string Name, string? Role, string? Phone, string? Extension,
    string? Email, bool IsPrimary, int SortOrder);

public sealed record SaveProviderContactRequest(
    string Name, string? Role, string? Phone, string? Extension, string? Email,
    bool IsPrimary, int SortOrder);

public sealed record MergeProvidersRequest(int MergedProviderId);

public sealed record MergeProvidersResultDto(int SurvivingProviderId, string Summary);

// ScreenshotBase64 is the item's pasted evidence clip, carried inline rather
// than through a separate blob route. It travels on the ordinary item payload
// because it is edited as part of the item and must be saved atomically with the
// price it evidences; AtRequestScreenshot caps how big it may get.
public sealed record AtRequestItemDto(
    int Id, int AtRequestId, string? Name, decimal ItemCost, int Quantity, string? Url,
    string? ScreenshotBase64 = null);
public sealed record AtRequestDto(
    int Id, int PersonId, string? ClientName, string? ClientEvergreenId,
    string? CaseManagerName, string? CaseManagerEmail, string? CaseManagerPhone, string? CaseManagerAgency,
    string? VendorName, string? VendorBillingLocation, string? VendorProgramContact, string? VendorBillingContact,
    decimal SalesTax, bool SalesTaxOverridden, DateTime? SubmittedDate, DateTime? DecisionDate, string Status,
    int Revision,
    IReadOnlyList<AtRequestItemDto> Items,
    // The passthrough rate this request was published under. Null on a draft.
    // Outbound only, for the same reason as the attestation below: the server
    // reads it from agency settings at publication, never from the client.
    decimal? PassthroughRate = null,
    // Attestation — OUTBOUND ONLY. There is deliberately no matching field on
    // SaveAtRequestRequest: a signer name arriving from a client is a claim about
    // who signed, and the server records who actually did. See the publish route.
    string? SignedByName = null, string? SignedByRole = null, int? SignedByUserId = null,
    DateTime? SignedAtUtc = null, string? AttestationStatement = null);
public sealed record AtRequestListItemDto(
    int Id, string? ClientName, string Status, decimal TotalCost, DateTime? SubmittedDate,
    string? VendorName, string? CaseManagerName, bool HasSnapshot,
    string? SignedByName = null, DateTime? SignedAtUtc = null);
public sealed record SaveAtRequestItemRequest(
    int Id, string? Name, decimal ItemCost, int Quantity, string? Url,
    string? ScreenshotBase64 = null);
public sealed record SaveAtRequestRequest(
    int PersonId, string? ClientName, string? ClientEvergreenId,
    string? CaseManagerName, string? CaseManagerEmail, string? CaseManagerPhone, string? CaseManagerAgency,
    string? VendorName, string? VendorBillingLocation, string? VendorProgramContact, string? VendorBillingContact,
    decimal SalesTax, bool SalesTaxOverridden, DateTime? SubmittedDate, DateTime? DecisionDate, string Status,
    IReadOnlyList<SaveAtRequestItemRequest> Items,
    int ExpectedRevision = 0);
public sealed record PublishAtRequestRequest(int ExpectedRevision);
public sealed record ReopenAtRequestRequest(int ExpectedRevision);
public sealed record BinaryPayloadDto(string? Base64);

public sealed record ClientAiContextSourceDto(string Category, string Description);
public sealed record ClientAiContextDto(
    int PersonId,
    string? ConsumerFirstName,
    IReadOnlyList<ClientAiContextSourceDto> Sources);

public sealed record UpdateFormRequest(DateTime? CompletedDate, DateTime? OpenedDate);
public sealed record AttestFormRequest(
    int FormId,
    DateTime CompletedOn,
    int? EvidenceNoteId = null,
    string? SupervisorOverrideReason = null);
public sealed record RevokeFormAttestationRequest(int FormId, string Reason);
public sealed record PendingAttestationDto(
    int FormId,
    int PersonId,
    string FormType,
    DateTime CycleStart,
    DateTime CycleEnd,
    DateTime DueDate,
    int EvidenceNoteId,
    DateTime EvidenceDate);
public sealed record DeleteFormsRequest(IReadOnlyList<int> FormIds);

public sealed record ApiErrorDto(string Code, string Message, string CorrelationId);

/// <summary>
/// What <c>GET /health/version</c> reports. <c>ContractRevision</c> fingerprints both routes and
/// persistence-relevant contract shapes and is the field that
/// decides compatibility; <c>ReleaseVersion</c> is for humans reading a log, because
/// a release number is bumped when a release is cut rather than when a route changes
/// and so cannot answer "does this server serve what my client calls".
/// </summary>
public sealed record ApiVersionDto(string Product, string ReleaseVersion, string ContractRevision);
