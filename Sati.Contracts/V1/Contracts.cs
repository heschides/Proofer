namespace Sati.Contracts.V1;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    UserProfileDto User);

public sealed record UserProfileDto(
    int Id,
    string Username,
    string DisplayName,
    string Role,
    int? SupervisorId,
    int AgencyId,
    string? Email,
    string? Phone);

public sealed record CreateUserRequest(
    string Username, string DisplayName, string Role, int? SupervisorId,
    int AgencyId, string? Email, string? Phone, string InitialPassword);

public sealed record SaveUserRequest(
    string Username, string DisplayName, string Role, int? SupervisorId,
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
    IReadOnlyList<NoteSummaryDto> Notes);

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
    bool UpdateBillingAddress = false);

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

public sealed record SupervisorNoteActionRequest(string? Reason, int ExpectedRevision = 0);

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
    int Revision = 0);

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
    string AssignedUserDisplayName);

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

public sealed record ProviderDto(
    int Id, string Type, string Name, string? Street, string? City, string? State, string? Zip,
    string? PrimaryContact, string? Phone, int OfferedServices, bool ProvidesPassthroughService,
    string? BillingLocationEis, string? ProgramContact, string? BillingContact,
    string? Npi = null, string? MaineCareProviderId = null);

public sealed record SaveProviderRequest(
    string Type, string Name, string? Street, string? City, string? State, string? Zip,
    string? PrimaryContact, string? Phone, int OfferedServices, bool ProvidesPassthroughService,
    string? BillingLocationEis, string? ProgramContact, string? BillingContact,
    string? Npi = null, string? MaineCareProviderId = null);

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
public sealed record ClientAiContextDto(string PromptText, IReadOnlyList<ClientAiContextSourceDto> Sources);
public sealed record BuildClientAiContextRequest(int RequestingUserId, string RoughNarrative, int? ExcludedNoteId);

public sealed record UpdateFormRequest(DateTime? CompletedDate, DateTime? OpenedDate);
public sealed record DeleteFormsRequest(IReadOnlyList<int> FormIds);

public sealed record ApiErrorDto(string Code, string Message, string CorrelationId);
