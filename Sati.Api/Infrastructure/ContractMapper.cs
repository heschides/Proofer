using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal static class ContractMapper
{
    private static readonly string[] GenderNames = ["Unknown", "Male", "Female", "NonBinary"];
    private static readonly string[] WaiverNames = ["None", "Section21", "Section29"];
    // Mirrors Sati.PersonStatus. Append-only — see that enum's own comment.
    internal static readonly string[] PersonStatusNames = ["Active", "NoLongerServed", "Deceased", "Ghost"];
    internal static readonly string[] NoteStatusNames =
    [
        "Scheduled", "Pending", "Logged", "HeldForCompliance", "Cancelled", "Delayed",
        "Approved", "Returned", "Abandoned", "ComplianceBlocked"
    ];
    // Ordinals mirror the append-only desktop enum. Reminder is last so existing
    // persisted values keep their meaning; NoteSchedulingPolicy normalizes its
    // non-billable shape before this value reaches persistence.
    internal static readonly string[] NoteTypeNames = ["Visit", "Contact", "Form", "Other", "Reminder"];
    internal static readonly string[] FormTypeNames =
    [
        "Q1R", "Q2R", "Q3R", "Q4R", "PCP", "ComprehensiveAssessment", "Reclassification",
        "SafetyPlan", "PrivacyPractices", "Release_Agency", "Release_DHHS", "Release_Medical"
    ];

    public static UserProfileDto ToProfile(ServerUser user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.Role,
        user.Permissions,
        user.SupervisorId,
        user.AgencyId,
        user.Email,
        user.Phone);

    public static PersonDto ToPerson(
        ServerPerson person,
        IReadOnlyList<ServerForm> forms,
        IReadOnlyList<ServerNote> notes) => new(
        person.Id,
        person.UserId,
        person.FirstName,
        person.LastName,
        person.BirthDate,
        NameAt(GenderNames, person.Gender, "Unknown"),
        person.EffectiveDate,
        person.Bio,
        NameAt(WaiverNames, person.Waiver, "None"),
        person.AgencyId,
        person.MaineCareId,
        person.DiagnosisCode,
        person.PlaceOfService,
        person.EvergreenId,
        person.OpenWithVR,
        person.HasGuardian,
        person.GuardianName,
        person.PhoneNumber,
        person.Address,
        person.BillingStreet,
        person.BillingCity,
        person.BillingState,
        person.BillingZip,
        person.PrimaryCareProvider,
        person.HealthcareSystemName,
        person.HasHomeSupport,
        person.HasSelfDirectedHomeSupport,
        person.HasSharedLiving,
        person.HasCommunitySupport1To1,
        person.HasCommunitySupportSelfDirected,
        person.HasCommunitySupportDayProgram,
        person.DayProgramCount,
        person.HasEmploymentSpecialist,
        person.HasWorkSupports,
        person.IsEmployed,
        person.Revision,
        forms.Select(ToForm).ToList(),
        notes.Select(ToNoteSummary).ToList(),
        person.CaseManagerIsRepPayee,
        person.RepPayeeMonthlyIncome,
        person.RepPayeeRegularCheckRequestNeeds,
        person.CaseManagerIsDhhsRepresentative,
        person.UsesModivcare,
        person.Email,
        person.IsTestData,
        person.CredibleClientId,
        person.VrCounselorName,
        person.VrAssistantName,
        person.CreatedAtUtc,
        NameAt(PersonStatusNames, person.Status, "Active"),
        person.StatusNote,
        person.StatusChangedAtUtc,
        person.StatusChangedByUserId);

    public static FormDto ToForm(ServerForm form) => new(
        form.Id,
        form.Type,
        form.DueDate,
        form.IsCompliant,
        form.PersonId,
        form.CompletedDate,
        form.OpenedDate);

    public static NoteSummaryDto ToNoteSummary(ServerNote note) => new(
        NullableNameAt(NoteStatusNames, note.Status),
        note.EventDate,
        NullableNameAt(NoteTypeNames, note.NoteType));

    public static NoteDto ToNote(
        ServerNote note,
        ServerPerson? person = null,
        IReadOnlyList<string>? complianceFailureReasons = null) => new(
        note.Id,
        note.Narrative,
        note.EventDate,
        NullableNameAt(NoteStatusNames, note.Status),
        note.Minutes,
        note.StartTime,
        note.PersonId,
        NullableNameAt(FormTypeNames, note.FormType),
        NullableNameAt(NoteTypeNames, note.NoteType),
        note.AgencyId,
        note.ReturnReason,
        note.ReturnedById,
        note.ApprovedById,
        note.ApprovedAt,
        note.ReturnedAt,
        note.CaseManagerJustification,
        note.VisitDocumentationJson,
        note.ComplianceOverride,
        note.OverrideReason,
        note.OverrideApprovedById,
        note.OverrideApprovedAt,
        note.Revision,
        person is null ? null : new PersonReferenceDto(person.Id, person.UserId, person.FirstName, person.LastName),
        complianceFailureReasons);

    public static ProviderContactDto ToProviderContact(ServerProviderContact contact) => new(
        contact.Id, contact.ProviderId, contact.Name, contact.Role, contact.Phone,
        contact.Extension, contact.Email, contact.IsPrimary, contact.SortOrder);

    public static ConsumerProviderDto ToConsumerProvider(ServerPersonProvider link) => new(
        link.Id, link.PersonId, link.ProviderId, link.Role, link.IsPrimaryCare,
        link.StartDate, link.EndDate, link.HasActiveRelease, link.SortOrder);

    public static PersonContactDto ToPersonContact(ServerPersonContact contact) => new(
        contact.Id,
        contact.PersonId,
        contact.FirstName,
        contact.LastName,
        contact.Kind,
        contact.Relationship,
        contact.Organization,
        contact.Phone,
        contact.Email,
        contact.IsEmergencyContact,
        contact.HasActiveRelease,
        contact.IsActive);

    public static BillingPeriodDto ToBillingPeriod(ServerBillingPeriod period) => new(
        period.Id,
        period.UserId,
        period.Month,
        period.Year,
        NameAt(["Draft", "Submitted", "Accepted", "Rejected"], period.Status, "Draft"),
        period.SubmittedAt,
        period.Lines.Select(ToClaimLine).ToList());

    public static ClaimLineDto ToClaimLine(ServerClaimLine line) => new(
        line.Id,
        line.NoteId,
        line.BillingPeriodId,
        line.DateOfService,
        line.ProcedureCode,
        line.ProcedureModifier,
        line.Units,
        line.ChargeAmount,
        line.ClientMaineCareId,
        line.RenderingProviderNpi,
        line.DiagnosisCode,
        line.PlaceOfService,
        line.IsComplianceException,
        line.ComplianceExceptionReason);

    public static BillingConfigurationDto ToBillingConfiguration(ServerAgency agency) => new(
        agency.BillingProcedureCode ?? string.Empty,
        agency.BillingModifier,
        agency.BillingUnitRate,
        agency.EdiSubmitterId ?? string.Empty,
        agency.EdiPayerName ?? string.Empty,
        agency.EdiPayerId ?? string.Empty,
        agency.EdiContactName ?? string.Empty,
        agency.EdiContactPhone ?? string.Empty);

    public static ScratchpadDto ToScratchpad(
        ServerScratchpad scratchpad,
        IReadOnlyList<ServerScratchpadComment> comments) => new(
        scratchpad.Id,
        scratchpad.UserId,
        scratchpad.Date,
        scratchpad.Content,
        comments.Select(ToScratchpadComment).ToList(),
        scratchpad.Revision);

    public static ScratchpadCommentDto ToScratchpadComment(ServerScratchpadComment comment) => new(
        comment.Id,
        comment.ScratchpadId,
        comment.AuthorUserId,
        comment.AuthorDisplayName,
        comment.CreatedAtUtc,
        comment.Content);

    public static IncentiveDto ToIncentive(ServerIncentive incentive) => new(
        incentive.Id,
        incentive.UserId,
        incentive.Month,
        incentive.Year,
        incentive.DaysScheduled,
        incentive.BaseIncentive,
        incentive.PerUnitIncentive,
        incentive.UnitsPerDay,
        incentive.ExcludedDatesJson);

    public static ReviewItemDto ToReviewItem(ServerReviewItem item) => new(
        item.Id, item.PersonId, item.CycleAnchor, item.Quarter, item.Category,
        item.SlotIndex, item.RequestedDate, item.ReceivedDate, item.LoggedDate,
        item.Appointment is null ? null : ToAppointment(item.Appointment));

    public static AppointmentDto ToAppointment(ServerAppointment appointment) => new(
        appointment.Id, appointment.ReviewItemId, appointment.Date, appointment.ProviderName);

    public static ComprehensiveAssessmentDto ToAssessment(ServerComprehensiveAssessment assessment) => new(
        assessment.Id, assessment.PersonId, assessment.AuthorUserId, assessment.Status,
        assessment.Version, assessment.CreatedAt, assessment.UpdatedAt, assessment.SubmittedAt,
        assessment.ApprovedAt, assessment.ApprovedByUserId, assessment.DocumentJson, assessment.Revision);

    public static ProviderDto ToProvider(ServerProvider p) => new(
        p.Id, p.Type, p.Name, p.Street, p.City, p.State, p.Zip, p.PrimaryContact, p.Phone,
        p.OfferedServices, p.ProvidesPassthroughService, p.BillingLocationEis, p.ProgramContact, p.BillingContact,
        p.Npi, p.MaineCareProviderId, p.MedicalKind, p.ParentProviderId);

    public static AtRequestDto ToAtRequest(ServerAtRequest a) => new(
        a.Id, a.PersonId, a.ClientName, a.ClientEvergreenId, a.CaseManagerName, a.CaseManagerEmail,
        a.CaseManagerPhone, a.CaseManagerAgency, a.VendorName, a.VendorBillingLocation,
        a.VendorProgramContact, a.VendorBillingContact, a.SalesTax, a.SalesTaxOverridden, a.SubmittedDate, a.DecisionDate,
        a.Status, a.Revision,
        a.Items.Select(i => new AtRequestItemDto(i.Id, i.ATRequestId, i.Name, i.ItemCost, i.Quantity, i.Url,
            i.ScreenshotPng is null ? null : Convert.ToBase64String(i.ScreenshotPng))).ToList(),
        a.PassthroughRate,
        a.SignedByName, a.SignedByRole, a.SignedByUserId, a.SignedAtUtc, a.AttestationStatement);

    public static SettingsDto ToSettings(ServerSettings s) => new(
        s.Id, s.AbandonedAfterDays, s.ProductivityThreshold, s.BaseIncentive, s.PerUnitIncentive,
        s.PassthroughRate, s.SalesTaxRate, s.DefaultPassthroughProviderId, s.VisitTemplate,
        s.ContactTemplate, s.DocumentationTemplate, s.HealthcareSystemsJson, s.ExcludeMonday,
        s.ExcludeTuesday, s.ExcludeWednesday, s.ExcludeThursday, s.ExcludeFriday,
        s.ExcludeNewYearsDay, s.ExcludeMLKDay, s.ExcludePresidentsDay, s.ExcludeMemorialDay,
        s.ExcludeJuneteenth, s.ExcludeIndependenceDay, s.ExcludeLaborDay,
        s.ExcludeIndigenousPeoplesDay, s.ExcludeVeteransDay, s.ExcludeThanksgiving,
        s.ExcludeDayAfterThanksgiving, s.ExcludeChristmas, s.ReviewOpenDaysBefore,
        s.ReviewDaysAfterDue, s.PcpOpenDaysBefore, s.PcpDaysAfterDue,
        s.CompAssessmentOpenDaysBefore, s.CompAssessmentDaysAfterDue,
        s.ReclassificationOpenDaysBefore, s.ReclassificationDaysAfterDue,
        s.SafetyPlanOpenDaysBefore, s.SafetyPlanDaysAfterDue,
        s.PrivacyPracticesOpenDaysBefore, s.PrivacyPracticesDaysAfterDue,
        s.ReleaseAgencyOpenDaysBefore, s.ReleaseAgencyDaysAfterDue,
        s.ReleaseDhhsOpenDaysBefore, s.ReleaseDhhsDaysAfterDue,
        s.ReleaseMedicalOpenDaysBefore, s.ReleaseMedicalDaysAfterDue,
        s.Q4RDaysBeforeAnniversary, s.PcpDaysBeforeAnniversary,
        s.CompAssessmentDaysBeforeAnniversary, s.ReclassificationDaysBeforeAnniversary,
        s.SafetyPlanDaysBeforeAnniversary, s.PrivacyPracticesDaysBeforeAnniversary,
        s.ReleaseAgencyDaysBeforeAnniversary, s.ReleaseDhhsDaysBeforeAnniversary,
        s.ReleaseMedicalDaysBeforeAnniversary, s.Revision,
        s.BillingComplianceRequirements,
        s.AllowCredibleProfileUpdates,
        VocationalRehabilitationProfile.NormalizeAssistantTitle(s.VrAssistantTitle), s.AnnualPacketOpenDaysBefore);

    public static bool TryParseNoteStatus(string? value, out int? parsed) =>
        TryParseNullableOrdinal(NoteStatusNames, value, out parsed);

    public static bool TryParseNoteType(string? value, out int? parsed) =>
        TryParseNullableOrdinal(NoteTypeNames, value, out parsed);

    public static bool TryParseFormType(string? value, out int? parsed) =>
        TryParseNullableOrdinal(FormTypeNames, value, out parsed);

    public static bool TryParseGender(string? value, out int parsed) =>
        TryParseOrdinal(GenderNames, value, out parsed);

    public static bool TryParseWaiver(string? value, out int parsed) =>
        TryParseOrdinal(WaiverNames, value, out parsed);

    public static int FormTypeCount => FormTypeNames.Length;

    public static string FormTypeName(int index) => NameAt(FormTypeNames, index, string.Empty);
    public static string FormTypeNameSafe(string value) =>
        int.TryParse(value, out var ordinal) ? NameAt(FormTypeNames, ordinal, value) : value;

    private static bool TryParseNullableOrdinal(string[] names, string? value, out int? parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            return true;
        }

        var index = Array.FindIndex(names, x => string.Equals(x, value, StringComparison.Ordinal));
        parsed = index >= 0 ? index : null;
        return index >= 0;
    }

    private static bool TryParseOrdinal(string[] names, string? value, out int parsed)
    {
        parsed = Array.FindIndex(names, x => string.Equals(x, value, StringComparison.Ordinal));
        return parsed >= 0;
    }

    public static int ParseNoteStatus(string? value) => Array.IndexOf(NoteStatusNames, value);

    public static string? NoteStatusName(int? status) => NullableNameAt(NoteStatusNames, status);
    public static int ParseNoteType(string? value) => Array.IndexOf(NoteTypeNames, value);
    public static int ParseFormType(string? value) => Array.IndexOf(FormTypeNames, value);

    internal static string NameAt(string[] names, int index, string fallback) =>
        index >= 0 && index < names.Length ? names[index] : fallback;

    internal static string? NullableNameAt(string[] names, int? index) =>
        index.HasValue && index.Value >= 0 && index.Value < names.Length ? names[index.Value] : null;
}
