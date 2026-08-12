using System.Collections.ObjectModel;
using Sati.Contracts.V1;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Models.Assessments;

namespace Sati.Data.Cloud;

internal static class CloudContractMapper
{
    public static User ToUser(UserProfileDto dto)
    {
        var user = User.Create(
            dto.Id,
            dto.Username,
            dto.DisplayName,
            string.Empty,
            string.Empty,
            Parse<UserRole>(dto.Role),
            dto.SupervisorId,
            dto.AgencyId);
        user.Email = dto.Email;
        user.Phone = dto.Phone;
        return user;
    }

    public static Person ToPerson(PersonDto dto)
    {
        var person = Person.Rehydrate(dto.Id, dto.UserId);
        person.FirstName = dto.FirstName;
        person.LastName = dto.LastName;
        person.BirthDate = dto.BirthDate;
        person.Gender = Parse<Gender>(dto.Gender);
        person.EffectiveDate = dto.EffectiveDate;
        person.Bio = dto.Bio;
        person.Waiver = Parse<WaiverType>(dto.Waiver);
        person.AgencyId = dto.AgencyId;
        person.MaineCareId = dto.MaineCareId;
        person.DiagnosisCode = dto.DiagnosisCode;
        person.PlaceOfService = dto.PlaceOfService;
        person.EvergreenId = dto.EvergreenId;
        person.OpenWithVR = dto.OpenWithVR;
        person.HasGuardian = dto.HasGuardian;
        person.GuardianName = dto.GuardianName;
        person.PhoneNumber = dto.PhoneNumber;
        person.Address = dto.Address;
        person.PrimaryCareProvider = dto.PrimaryCareProvider;
        person.HealthcareSystemName = dto.HealthcareSystemName;
        person.HasHomeSupport = dto.HasHomeSupport;
        person.HasSelfDirectedHomeSupport = dto.HasSelfDirectedHomeSupport;
        person.HasSharedLiving = dto.HasSharedLiving;
        person.HasCommunitySupport1To1 = dto.HasCommunitySupport1To1;
        person.HasCommunitySupportSelfDirected = dto.HasCommunitySupportSelfDirected;
        person.HasCommunitySupportDayProgram = dto.HasCommunitySupportDayProgram;
        person.DayProgramCount = dto.DayProgramCount;
        person.HasEmploymentSpecialist = dto.HasEmploymentSpecialist;
        person.HasWorkSupports = dto.HasWorkSupports;
        person.IsEmployed = dto.IsEmployed;
        person.Forms = dto.Forms.Select(ToForm).ToList();
        person.Notes = dto.Notes.Select(ToNoteSummary).ToList();
        return person;
    }

    public static Form ToForm(FormDto dto)
    {
        var form = new Form(Parse<FormType>(dto.Type), dto.DueDate, dto.IsCompliant)
        {
            Id = dto.Id,
            PersonId = dto.PersonId,
            CompletedDate = dto.CompletedDate,
            OpenedDate = dto.OpenedDate
        };
        return form;
    }

    public static Note ToNote(NoteDto dto)
    {
        var note = Note.Rehydrate(dto.Id);
        note.Narrative = dto.Narrative;
        note.EventDate = dto.EventDate;
        note.Status = ParseNullable<NoteStatus>(dto.Status);
        note.Minutes = dto.Minutes;
        note.StartTime = dto.StartTime;
        note.PersonId = dto.PersonId;
        note.FormType = ParseNullable<FormType>(dto.FormType);
        note.NoteType = ParseNullable<NoteType>(dto.NoteType);
        note.AgencyId = dto.AgencyId;
        note.ReturnReason = dto.ReturnReason;
        note.ReturnedById = dto.ReturnedById;
        note.ApprovedById = dto.ApprovedById;
        note.ApprovedAt = dto.ApprovedAt;
        note.ReturnedAt = dto.ReturnedAt;
        note.CaseManagerJustification = dto.CaseManagerJustification;
        note.VisitDocumentationJson = dto.VisitDocumentationJson;
        note.ComplianceOverride = dto.ComplianceOverride;
        note.OverrideReason = dto.OverrideReason;
        note.OverrideApprovedById = dto.OverrideApprovedById;
        note.OverrideApprovedAt = dto.OverrideApprovedAt;
        if (dto.Person is not null)
        {
            note.Person = Person.Rehydrate(dto.Person.Id, dto.Person.UserId);
            note.Person.FirstName = dto.Person.FirstName;
            note.Person.LastName = dto.Person.LastName;
        }
        return note;
    }

    public static Settings ToSettings(SettingsDto s) => new()
    {
        Id = s.Id,
        AbandonedAfterDays = s.AbandonedAfterDays,
        ProductivityThreshold = s.ProductivityThreshold,
        BaseIncentive = s.BaseIncentive,
        PerUnitIncentive = s.PerUnitIncentive,
        PassthroughRate = s.PassthroughRate,
        SalesTaxRate = s.SalesTaxRate,
        DefaultPassthroughProviderId = s.DefaultPassthroughProviderId,
        VisitTemplate = s.VisitTemplate,
        ContactTemplate = s.ContactTemplate,
        DocumentationTemplate = s.DocumentationTemplate,
        HealthcareSystemsJson = s.HealthcareSystemsJson,
        ExcludeMonday = s.ExcludeMonday,
        ExcludeTuesday = s.ExcludeTuesday,
        ExcludeWednesday = s.ExcludeWednesday,
        ExcludeThursday = s.ExcludeThursday,
        ExcludeFriday = s.ExcludeFriday,
        ExcludeNewYearsDay = s.ExcludeNewYearsDay,
        ExcludeMLKDay = s.ExcludeMLKDay,
        ExcludePresidentsDay = s.ExcludePresidentsDay,
        ExcludeMemorialDay = s.ExcludeMemorialDay,
        ExcludeJuneteenth = s.ExcludeJuneteenth,
        ExcludeIndependenceDay = s.ExcludeIndependenceDay,
        ExcludeLaborDay = s.ExcludeLaborDay,
        ExcludeIndigenousPeoplesDay = s.ExcludeIndigenousPeoplesDay,
        ExcludeVeteransDay = s.ExcludeVeteransDay,
        ExcludeThanksgiving = s.ExcludeThanksgiving,
        ExcludeDayAfterThanksgiving = s.ExcludeDayAfterThanksgiving,
        ExcludeChristmas = s.ExcludeChristmas,
        ReviewOpenDaysBefore = s.ReviewOpenDaysBefore,
        ReviewDaysAfterDue = s.ReviewDaysAfterDue,
        PcpOpenDaysBefore = s.PcpOpenDaysBefore,
        PcpDaysAfterDue = s.PcpDaysAfterDue,
        CompAssessmentOpenDaysBefore = s.CompAssessmentOpenDaysBefore,
        CompAssessmentDaysAfterDue = s.CompAssessmentDaysAfterDue,
        ReclassificationOpenDaysBefore = s.ReclassificationOpenDaysBefore,
        ReclassificationDaysAfterDue = s.ReclassificationDaysAfterDue,
        SafetyPlanOpenDaysBefore = s.SafetyPlanOpenDaysBefore,
        SafetyPlanDaysAfterDue = s.SafetyPlanDaysAfterDue,
        PrivacyPracticesOpenDaysBefore = s.PrivacyPracticesOpenDaysBefore,
        PrivacyPracticesDaysAfterDue = s.PrivacyPracticesDaysAfterDue,
        ReleaseAgencyOpenDaysBefore = s.ReleaseAgencyOpenDaysBefore,
        ReleaseAgencyDaysAfterDue = s.ReleaseAgencyDaysAfterDue,
        ReleaseDhhsOpenDaysBefore = s.ReleaseDhhsOpenDaysBefore,
        ReleaseDhhsDaysAfterDue = s.ReleaseDhhsDaysAfterDue,
        ReleaseMedicalOpenDaysBefore = s.ReleaseMedicalOpenDaysBefore,
        ReleaseMedicalDaysAfterDue = s.ReleaseMedicalDaysAfterDue,
        Q4RDaysBeforeAnniversary = s.Q4RDaysBeforeAnniversary,
        PcpDaysBeforeAnniversary = s.PcpDaysBeforeAnniversary,
        CompAssessmentDaysBeforeAnniversary = s.CompAssessmentDaysBeforeAnniversary,
        ReclassificationDaysBeforeAnniversary = s.ReclassificationDaysBeforeAnniversary,
        SafetyPlanDaysBeforeAnniversary = s.SafetyPlanDaysBeforeAnniversary,
        PrivacyPracticesDaysBeforeAnniversary = s.PrivacyPracticesDaysBeforeAnniversary,
        ReleaseAgencyDaysBeforeAnniversary = s.ReleaseAgencyDaysBeforeAnniversary,
        ReleaseDhhsDaysBeforeAnniversary = s.ReleaseDhhsDaysBeforeAnniversary,
        ReleaseMedicalDaysBeforeAnniversary = s.ReleaseMedicalDaysBeforeAnniversary
    };

    public static SettingsDto ToSettingsDto(Settings s) => new(
        s.Id, s.AbandonedAfterDays, s.ProductivityThreshold, s.BaseIncentive, s.PerUnitIncentive,
        s.PassthroughRate, s.SalesTaxRate, s.DefaultPassthroughProviderId, s.VisitTemplate,
        s.ContactTemplate, s.DocumentationTemplate, s.HealthcareSystemsJson, s.ExcludeMonday,
        s.ExcludeTuesday, s.ExcludeWednesday, s.ExcludeThursday, s.ExcludeFriday,
        s.ExcludeNewYearsDay, s.ExcludeMLKDay, s.ExcludePresidentsDay, s.ExcludeMemorialDay,
        s.ExcludeJuneteenth, s.ExcludeIndependenceDay, s.ExcludeLaborDay, s.ExcludeIndigenousPeoplesDay,
        s.ExcludeVeteransDay, s.ExcludeThanksgiving, s.ExcludeDayAfterThanksgiving, s.ExcludeChristmas,
        s.ReviewOpenDaysBefore, s.ReviewDaysAfterDue, s.PcpOpenDaysBefore, s.PcpDaysAfterDue,
        s.CompAssessmentOpenDaysBefore, s.CompAssessmentDaysAfterDue, s.ReclassificationOpenDaysBefore,
        s.ReclassificationDaysAfterDue, s.SafetyPlanOpenDaysBefore, s.SafetyPlanDaysAfterDue,
        s.PrivacyPracticesOpenDaysBefore, s.PrivacyPracticesDaysAfterDue, s.ReleaseAgencyOpenDaysBefore,
        s.ReleaseAgencyDaysAfterDue, s.ReleaseDhhsOpenDaysBefore, s.ReleaseDhhsDaysAfterDue,
        s.ReleaseMedicalOpenDaysBefore, s.ReleaseMedicalDaysAfterDue, s.Q4RDaysBeforeAnniversary,
        s.PcpDaysBeforeAnniversary, s.CompAssessmentDaysBeforeAnniversary, s.ReclassificationDaysBeforeAnniversary,
        s.SafetyPlanDaysBeforeAnniversary, s.PrivacyPracticesDaysBeforeAnniversary,
        s.ReleaseAgencyDaysBeforeAnniversary, s.ReleaseDhhsDaysBeforeAnniversary,
        s.ReleaseMedicalDaysBeforeAnniversary);

    public static Scratchpad ToScratchpad(ScratchpadDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        Date = dto.Date,
        Content = dto.Content,
        Comments = new ObservableCollection<ScratchpadComment>(dto.Comments.Select(ToScratchpadComment))
    };

    public static ScratchpadComment ToScratchpadComment(ScratchpadCommentDto dto) => new()
    {
        Id = dto.Id,
        ScratchpadId = dto.ScratchpadId,
        AuthorUserId = dto.AuthorUserId,
        AuthorDisplayName = dto.AuthorDisplayName,
        CreatedAtUtc = dto.CreatedAtUtc,
        Content = dto.Content
    };

    public static ExemptDate ToExemptDate(ExemptDateDto dto, int userId) => new()
    {
        Id = dto.Id,
        UserId = userId,
        Date = dto.Date,
        Reason = dto.Reason
    };

    public static Incentive ToIncentive(IncentiveDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        Month = dto.Month,
        Year = dto.Year,
        DaysScheduled = dto.DaysScheduled,
        BaseIncentive = dto.BaseIncentive,
        PerUnitIncentive = dto.PerUnitIncentive,
        UnitsPerDay = dto.UnitsPerDay,
        ExcludedDatesJson = dto.ExcludedDatesJson
    };

    public static SaveNoteRequest ToSaveNoteRequest(Note note) => new(
        note.Narrative,
        note.EventDate,
        note.Status?.ToString(),
        note.Minutes,
        note.StartTime,
        note.PersonId,
        note.FormType?.ToString(),
        note.NoteType?.ToString(),
        note.CaseManagerJustification,
        note.VisitDocumentationJson);

    public static SavePersonRequest ToSavePersonRequest(Person person) => new(
        person.FirstName ?? string.Empty,
        person.LastName ?? string.Empty,
        person.BirthDate,
        person.Gender.ToString(),
        person.EffectiveDate,
        person.Bio,
        person.Waiver.ToString(),
        person.MaineCareId,
        person.DiagnosisCode,
        person.PlaceOfService,
        person.EvergreenId,
        person.OpenWithVR,
        person.HasGuardian,
        person.GuardianName,
        person.PhoneNumber,
        person.Address,
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
        person.Forms.Select(form => new SavePersonFormRequest(
            form.Id,
            form.Type.ToString(),
            form.IsCompliant,
            form.CompletedDate,
            form.OpenedDate)).ToList());

    public static PersonContact ToPersonContact(PersonContactDto dto)
    {
        var contact = PersonContact.Rehydrate(dto.Id);
        contact.PersonId = dto.PersonId;
        contact.FirstName = dto.FirstName;
        contact.LastName = dto.LastName;
        contact.Kind = Parse<PersonContactKind>(dto.Kind);
        contact.Relationship = dto.Relationship;
        contact.Organization = dto.Organization;
        contact.Phone = dto.Phone;
        contact.Email = dto.Email;
        contact.IsEmergencyContact = dto.IsEmergencyContact;
        contact.HasActiveRelease = dto.HasActiveRelease;
        contact.IsActive = dto.IsActive;
        return contact;
    }

    public static SavePersonContactRequest ToSavePersonContactRequest(PersonContact contact) => new(
        contact.FirstName,
        contact.LastName,
        contact.Kind.ToString(),
        contact.Relationship,
        contact.Organization,
        contact.Phone,
        contact.Email,
        contact.IsEmergencyContact,
        contact.HasActiveRelease);

    public static BillingPeriod ToBillingPeriod(BillingPeriodDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        Month = dto.Month,
        Year = dto.Year,
        Status = Parse<BillingStatus>(dto.Status),
        SubmittedAt = dto.SubmittedAt,
        Lines = dto.Lines.Select(ToClaimLine).ToList()
    };

    public static ClaimLine ToClaimLine(ClaimLineDto dto) => new()
    {
        Id = dto.Id,
        NoteId = dto.NoteId,
        BillingPeriodId = dto.BillingPeriodId,
        DateOfService = dto.DateOfService,
        ProcedureCode = dto.ProcedureCode,
        Units = dto.Units,
        ClientMaineCareId = dto.ClientMaineCareId,
        RenderingProviderNpi = dto.RenderingProviderNpi,
        DiagnosisCode = dto.DiagnosisCode,
        PlaceOfService = dto.PlaceOfService,
        IsComplianceException = dto.IsComplianceException,
        ComplianceExceptionReason = dto.ComplianceExceptionReason
    };

    public static PersonSummary ToPersonSummary(PersonDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        EffectiveDate = dto.EffectiveDate,
        Forms = dto.Forms.Select(ToForm).ToList(),
        NoteSummaries = dto.Notes.Select(ToNoteSummaryValue).ToList()
    };

    public static ReviewItem ToReviewItem(ReviewItemDto dto)
    {
        var item = ReviewItem.Rehydrate(dto.Id, dto.PersonId, dto.CycleAnchor, dto.Quarter,
            Parse<ReviewCategory>(dto.Category), dto.SlotIndex);
        if (dto.RequestedDate.HasValue) item.MarkRequested(dto.RequestedDate.Value);
        if (dto.ReceivedDate.HasValue) item.MarkReceived(dto.ReceivedDate.Value);
        if (dto.LoggedDate.HasValue) item.MarkLogged(dto.LoggedDate.Value);
        item.Appointment = dto.Appointment is null ? null : ToAppointment(dto.Appointment);
        return item;
    }

    public static Appointment ToAppointment(AppointmentDto dto) =>
        Appointment.Rehydrate(dto.Id, dto.ReviewItemId, dto.Date, dto.ProviderName);

    public static ComprehensiveAssessment ToAssessment(ComprehensiveAssessmentDto dto) => new()
    {
        Id = dto.Id,
        PersonId = dto.PersonId,
        AuthorUserId = dto.AuthorUserId,
        Status = Parse<AssessmentStatus>(dto.Status),
        Version = dto.Version,
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        SubmittedAt = dto.SubmittedAt,
        ApprovedAt = dto.ApprovedAt,
        ApprovedByUserId = dto.ApprovedByUserId,
        DocumentJson = dto.DocumentJson
    };

    public static Provider ToProvider(ProviderDto dto) => new()
    {
        Id = dto.Id, Type = Parse<ProviderType>(dto.Type), Name = dto.Name,
        Street = dto.Street, City = dto.City, State = dto.State, Zip = dto.Zip,
        PrimaryContact = dto.PrimaryContact, Phone = dto.Phone,
        OfferedServices = (WaiverService)dto.OfferedServices,
        ProvidesPassthroughService = dto.ProvidesPassthroughService,
        BillingLocationEis = dto.BillingLocationEis, ProgramContact = dto.ProgramContact,
        BillingContact = dto.BillingContact
    };

    public static SaveProviderRequest ToSaveProviderRequest(Provider p) => new(
        p.Type.ToString(), p.Name, p.Street, p.City, p.State, p.Zip, p.PrimaryContact, p.Phone,
        (int)p.OfferedServices, p.ProvidesPassthroughService, p.BillingLocationEis, p.ProgramContact, p.BillingContact);

    public static ATRequest ToAtRequest(AtRequestDto dto)
    {
        var request = ATRequest.Rehydrate(dto.Id, dto.PersonId, dto.ClientName, dto.ClientEvergreenId,
            dto.CaseManagerName, dto.CaseManagerEmail, dto.CaseManagerPhone, dto.CaseManagerAgency,
            Parse<ATRequestStatus>(dto.Status));
        request.VendorName = dto.VendorName; request.VendorBillingLocation = dto.VendorBillingLocation;
        request.VendorProgramContact = dto.VendorProgramContact; request.VendorBillingContact = dto.VendorBillingContact;
        request.SalesTax = dto.SalesTax; request.SubmittedDate = dto.SubmittedDate; request.DecisionDate = dto.DecisionDate;
        request.Items = dto.Items.Select(i => { var item = ATRequestItem.Rehydrate(i.Id); item.ATRequestId = i.AtRequestId;
            item.Name = i.Name; item.ItemCost = i.ItemCost; item.Quantity = i.Quantity; item.Url = i.Url; return item; }).ToList();
        return request;
    }

    public static SaveAtRequestRequest ToSaveAtRequestRequest(ATRequest a) => new(
        a.PersonId, a.ClientName, a.ClientEvergreenId, a.CaseManagerName, a.CaseManagerEmail,
        a.CaseManagerPhone, a.CaseManagerAgency, a.VendorName, a.VendorBillingLocation,
        a.VendorProgramContact, a.VendorBillingContact, a.SalesTax, a.SubmittedDate, a.DecisionDate,
        a.Status.ToString(), a.Items.Select(i => new SaveAtRequestItemRequest(i.Id, i.Name, i.ItemCost, i.Quantity, i.Url)).ToList());

    private static Note ToNoteSummary(NoteSummaryDto dto)
    {
        var note = Note.Rehydrate(0);
        note.Status = ParseNullable<NoteStatus>(dto.Status);
        note.EventDate = dto.EventDate;
        note.NoteType = ParseNullable<NoteType>(dto.NoteType);
        return note;
    }

    private static NoteSummary ToNoteSummaryValue(NoteSummaryDto dto) => new()
    {
        Status = ParseNullable<NoteStatus>(dto.Status),
        EventDate = dto.EventDate,
        NoteType = ParseNullable<NoteType>(dto.NoteType)
    };

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The Demo API returned an unknown {typeof(T).Name} value '{value}'.");

    private static T? ParseNullable<T>(string? value) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Parse<T>(value);
}
