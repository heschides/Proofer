using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

internal static class PersonContractMapper
{
    public static SavePersonRequest ToSaveRequest(Person person) => new(
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
        person.Forms.Select(form => new SavePersonFormRequest(
            form.Id,
            form.Type.ToString(),
            form.IsCompliant,
            form.CompletedDate,
            form.OpenedDate)).ToList(),
        person.Revision,
        true,
        person.CaseManagerIsRepPayee,
        person.RepPayeeMonthlyIncome,
        person.RepPayeeRegularCheckRequestNeeds,
        person.CaseManagerIsDhhsRepresentative,
        person.UsesModivcare,
        person.Email,
        person.IsTestData);
}
