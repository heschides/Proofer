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
        user.Permissions = dto.Permissions;
        user.Email = dto.Email;
        user.Phone = dto.Phone;
        return user;
    }

    public static Person ToPerson(PersonDto dto)
    {
        var person = Person.Rehydrate(dto.Id, dto.UserId, dto.CreatedAtUtc);
        person.Status = Parse<PersonStatus>(dto.Status);
        person.StatusNote = dto.StatusNote;
        person.StatusChangedAtUtc = dto.StatusChangedAtUtc;
        person.StatusChangedByUserId = dto.StatusChangedByUserId;
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
        person.CredibleClientId = dto.CredibleClientId;
        person.OpenWithVR = dto.OpenWithVR;
        person.VrCounselorName = dto.VrCounselorName;
        person.VrAssistantName = dto.VrAssistantName;
        person.HasGuardian = dto.HasGuardian;
        person.GuardianName = dto.GuardianName;
        person.PhoneNumber = dto.PhoneNumber;
        person.Address = dto.Address;
        person.BillingStreet = dto.BillingStreet;
        person.BillingCity = dto.BillingCity;
        person.BillingState = dto.BillingState;
        person.BillingZip = dto.BillingZip;
        person.PrimaryCareProvider = dto.PrimaryCareProvider;
        person.HealthcareSystemName = dto.HealthcareSystemName;
        person.CaseManagerIsRepPayee = dto.CaseManagerIsRepPayee;
        person.CaseManagerIsDhhsRepresentative = dto.CaseManagerIsDhhsRepresentative;
        person.UsesModivcare = dto.UsesModivcare;
        person.Email = dto.Email;
        person.RepPayeeMonthlyIncome = dto.RepPayeeMonthlyIncome;
        person.RepPayeeRegularCheckRequestNeeds = dto.RepPayeeRegularCheckRequestNeeds;
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
        person.IsTestData = dto.IsTestData;
        person.Revision = dto.Revision;
        person.Forms = dto.Forms.Select(ToForm).ToList();
        person.Notes = dto.Notes.Select(ToNoteSummary).ToList();
        return person;
    }

    public static Form ToForm(FormDto dto)
    {
        // dto.IsCompliant is deliberately ignored. The server derives it from
        // CompletedDate too, so it carries no information the date does not, and
        // trusting it over the date is how a client could reconstruct the very
        // disagreement this model removed.
        var form = new Form(Parse<FormType>(dto.Type), dto.DueDate, dto.CompletedDate)
        {
            Id = dto.Id,
            PersonId = dto.PersonId,
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
        note.Revision = dto.Revision;
        note.ComplianceFailureReasons = dto.ComplianceFailureReasons ?? [];
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
        AllowCredibleProfileUpdates = s.AllowCredibleProfileUpdates,
        VrAssistantTitle = VocationalRehabilitationProfile.NormalizeAssistantTitle(s.VrAssistantTitle),
        BillingComplianceRequirements = s.BillingComplianceRequirements,
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
        ReleaseMedicalDaysBeforeAnniversary = s.ReleaseMedicalDaysBeforeAnniversary,
        Revision = s.Revision
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
        s.ReleaseMedicalDaysBeforeAnniversary, s.Revision,
        s.BillingComplianceRequirements,
        s.AllowCredibleProfileUpdates,
        VocationalRehabilitationProfile.NormalizeAssistantTitle(s.VrAssistantTitle));

    public static Scratchpad ToScratchpad(ScratchpadDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        Date = dto.Date,
        Content = dto.Content,
        Comments = new ObservableCollection<ScratchpadComment>(dto.Comments.Select(ToScratchpadComment)),
        Revision = dto.Revision
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
        note.VisitDocumentationJson,
        note.Revision);

    public static SavePersonRequest ToSavePersonRequest(Person person) =>
        PersonContractMapper.ToSaveRequest(person);

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
        ProcedureModifier = dto.ProcedureModifier,
        Units = dto.Units,
        ChargeAmount = dto.ChargeAmount,
        ClientMaineCareId = dto.ClientMaineCareId,
        RenderingProviderNpi = dto.RenderingProviderNpi,
        DiagnosisCode = dto.DiagnosisCode,
        PlaceOfService = dto.PlaceOfService,
        IsComplianceException = dto.IsComplianceException,
        ComplianceExceptionReason = dto.ComplianceExceptionReason
    };

    public static BillingConfiguration ToBillingConfiguration(BillingConfigurationDto dto) => new(
        dto.ProcedureCode, dto.Modifier, dto.UnitRate, dto.EdiSubmitterId,
        dto.PayerName, dto.PayerId, dto.ContactName, dto.ContactPhone);

    public static PersonSummary ToPersonSummary(PersonDto dto) => new()
    {
        Id = dto.Id,
        UserId = dto.UserId,
        Revision = dto.Revision,
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
        DocumentJson = dto.DocumentJson,
        Revision = dto.Revision
    };

    public static PersonProvider ToConsumerProvider(ConsumerProviderDto dto)
    {
        var link = PersonProvider.Rehydrate(dto.Id);
        link.PersonId = dto.PersonId;
        link.ProviderId = dto.ProviderId;
        link.Role = dto.Role;
        link.IsPrimaryCare = dto.IsPrimaryCare;
        link.StartDate = dto.StartDate;
        link.EndDate = dto.EndDate;
        link.HasActiveRelease = dto.HasActiveRelease;
        link.SortOrder = dto.SortOrder;
        return link;
    }

    public static SaveConsumerProviderRequest ToSaveConsumerProviderRequest(PersonProvider link) => new(
        link.ProviderId, link.Role, link.IsPrimaryCare, link.StartDate, link.EndDate,
        link.HasActiveRelease, link.SortOrder);

    public static ProviderContact ToProviderContact(ProviderContactDto dto)
    {
        var contact = ProviderContact.Rehydrate(dto.Id);
        contact.ProviderId = dto.ProviderId;
        contact.Name = dto.Name;
        contact.Role = dto.Role;
        contact.Phone = dto.Phone;
        contact.Extension = dto.Extension;
        contact.Email = dto.Email;
        contact.IsPrimary = dto.IsPrimary;
        contact.SortOrder = dto.SortOrder;
        return contact;
    }

    public static SaveProviderContactRequest ToSaveProviderContactRequest(ProviderContact contact) =>
        new(contact.Name, contact.Role, contact.Phone, contact.Extension, contact.Email,
            contact.IsPrimary, contact.SortOrder);

    public static Provider ToProvider(ProviderDto dto) => new()
    {
        Id = dto.Id, Type = Parse<ProviderType>(dto.Type), Name = dto.Name,
        Street = dto.Street, City = dto.City, State = dto.State, Zip = dto.Zip,
        PrimaryContact = dto.PrimaryContact, Phone = dto.Phone,
        OfferedServices = (WaiverService)dto.OfferedServices,
        ProvidesPassthroughService = dto.ProvidesPassthroughService,
        BillingLocationEis = dto.BillingLocationEis, ProgramContact = dto.ProgramContact,
        BillingContact = dto.BillingContact,
        Npi = dto.Npi, MaineCareProviderId = dto.MaineCareProviderId,
        // Absent or unrecognised leaves the entry unaffiliated rather than guessing a tier.
        MedicalKind = Enum.TryParse<MedicalProviderKind>(dto.MedicalKind, out var kind) ? kind : null,
        ParentProviderId = dto.ParentProviderId
    };

    public static SaveProviderRequest ToSaveProviderRequest(Provider p) => new(
        p.Type.ToString(), p.Name, p.Street, p.City, p.State, p.Zip, p.PrimaryContact, p.Phone,
        (int)p.OfferedServices, p.ProvidesPassthroughService, p.BillingLocationEis, p.ProgramContact, p.BillingContact,
        p.Npi, p.MaineCareProviderId, p.MedicalKind?.ToString(), p.ParentProviderId);

    public static ATRequest ToAtRequest(AtRequestDto dto)
    {
        var request = ATRequest.Rehydrate(dto.Id, dto.PersonId, dto.ClientName, dto.ClientEvergreenId,
            dto.CaseManagerName, dto.CaseManagerEmail, dto.CaseManagerPhone, dto.CaseManagerAgency,
            Parse<ATRequestStatus>(dto.Status));
        request.VendorName = dto.VendorName; request.VendorBillingLocation = dto.VendorBillingLocation;
        request.VendorProgramContact = dto.VendorProgramContact; request.VendorBillingContact = dto.VendorBillingContact;
        request.SalesTax = dto.SalesTax; request.SalesTaxOverridden = dto.SalesTaxOverridden; request.SubmittedDate = dto.SubmittedDate; request.DecisionDate = dto.DecisionDate;
        request.Revision = dto.Revision;
        request.RehydrateAttestation(dto.SignedByName, dto.SignedByRole, dto.SignedByUserId,
            dto.SignedAtUtc, dto.AttestationStatement, dto.PassthroughRate);
        request.Items = dto.Items.Select(i => { var item = ATRequestItem.Rehydrate(i.Id); item.ATRequestId = i.AtRequestId;
            item.Name = i.Name; item.ItemCost = i.ItemCost; item.Quantity = i.Quantity; item.Url = i.Url;
            item.ScreenshotPng = string.IsNullOrEmpty(i.ScreenshotBase64) ? null : Convert.FromBase64String(i.ScreenshotBase64);
            return item; }).ToList();
        return request;
    }

    public static SaveAtRequestRequest ToSaveAtRequestRequest(ATRequest a) => new(
        a.PersonId, a.ClientName, a.ClientEvergreenId, a.CaseManagerName, a.CaseManagerEmail,
        a.CaseManagerPhone, a.CaseManagerAgency, a.VendorName, a.VendorBillingLocation,
        a.VendorProgramContact, a.VendorBillingContact, a.SalesTax, a.SalesTaxOverridden, a.SubmittedDate, a.DecisionDate,
        a.Status.ToString(), a.Items.Select(i => new SaveAtRequestItemRequest(i.Id, i.Name, i.ItemCost, i.Quantity, i.Url,
            i.ScreenshotPng is null ? null : Convert.ToBase64String(i.ScreenshotPng))).ToList(),
        a.Revision);

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
