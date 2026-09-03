using System.IO.Compression;
using System.IO;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

internal static class PersonLifecycleLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Field[] Fields =
    [
        new("userId", "Assigned user ID", person => person.UserId.ToString()),
        new("agencyId", "Agency ID", person => person.AgencyId?.ToString()),
        new("firstName", "First name", person => person.FirstName),
        new("lastName", "Last name", person => person.LastName),
        new("birthDate", "Birth date", person => person.BirthDate.ToString("yyyy-MM-dd")),
        new("gender", "Gender", person => person.Gender.ToString()),
        new("effectiveDate", "Effective date", person => person.EffectiveDate?.ToString("yyyy-MM-dd")),
        new("bio", "Biography", person => person.Bio),
        new("journal", "Journal", person => person.Journal),
        new("waiver", "Waiver", person => person.Waiver.ToString()),
        new("maineCareId", "MaineCare ID", person => person.MaineCareId),
        new("diagnosisCode", "Diagnosis code", person => person.DiagnosisCode),
        new("placeOfService", "Place of service", person => person.PlaceOfService?.ToString()),
        new("evergreenId", "Evergreen ID", person => person.EvergreenId),
        new("credibleClientId", "Credible client ID", person => person.CredibleClientId),
        new("openWithVR", "Open with VR", person => YesNo(person.OpenWithVR)),
        new("vrCounselorName", "VR counselor", person => person.VrCounselorName),
        new("vrAssistantName", "VR assistant", person => person.VrAssistantName),
        new("hasGuardian", "Has guardian", person => YesNo(person.HasGuardian)),
        new("guardianName", "Guardian name", person => person.GuardianName),
        new("phoneNumber", "Phone number", person => person.PhoneNumber),
        new("email", "Email", person => person.Email),
        new("address", "Address", person => person.Address),
        new("billingStreet", "Claim street", person => person.BillingStreet),
        new("billingCity", "Claim city", person => person.BillingCity),
        new("billingState", "Claim state", person => person.BillingState),
        new("billingZip", "Claim ZIP", person => person.BillingZip),
        new("primaryCareProvider", "Primary care provider", person => person.PrimaryCareProvider),
        new("healthcareSystemName", "Healthcare system", person => person.HealthcareSystemName),
        new("caseManagerIsRepPayee", "Case manager is representative payee", person => YesNo(person.CaseManagerIsRepPayee)),
        new("caseManagerIsDhhsRepresentative", "Case manager is DHHS representative", person => YesNo(person.CaseManagerIsDhhsRepresentative)),
        new("usesModivcare", "Uses Modivcare", person => YesNo(person.UsesModivcare)),
        new("repPayeeMonthlyIncome", "Representative-payee monthly income", person =>
            person.RepPayeeMonthlyIncome?.ToString("0.00", CultureInfo.InvariantCulture)),
        new("repPayeeRegularCheckRequestNeeds", "Regular check-request needs", person => person.RepPayeeRegularCheckRequestNeeds),
        new("hasHomeSupport", "Home support", person => YesNo(person.HasHomeSupport)),
        new("hasSelfDirectedHomeSupport", "Self-directed home support", person => YesNo(person.HasSelfDirectedHomeSupport)),
        new("hasSharedLiving", "Shared living", person => YesNo(person.HasSharedLiving)),
        new("hasCommunitySupport1To1", "Community support 1:1", person => YesNo(person.HasCommunitySupport1To1)),
        new("hasCommunitySupportSelfDirected", "Self-directed community support", person => YesNo(person.HasCommunitySupportSelfDirected)),
        new("hasCommunitySupportDayProgram", "Community support day program", person => YesNo(person.HasCommunitySupportDayProgram)),
        new("dayProgramCount", "Day program count", person => person.DayProgramCount.ToString()),
        new("hasEmploymentSpecialist", "Employment specialist", person => YesNo(person.HasEmploymentSpecialist)),
        new("hasWorkSupports", "Work supports", person => YesNo(person.HasWorkSupports)),
        new("isEmployed", "Employed", person => YesNo(person.IsEmployed)),
        new("isTestData", "Test consumer", person => YesNo(person.IsTestData)),
        new("status", "Status", person => person.Status.ToString()),
        new("statusNote", "Status note", person => person.StatusNote)
    ];

    public static Dictionary<string, string?> Capture(Person person) =>
        Fields.ToDictionary(field => field.Name, field => field.Value(person));

    public static void RecordCreated(SatiContext context, User actor, Person person)
    {
        var snapshot = Capture(person);
        AddVersion(context, actor, person, "Created", snapshot,
            Fields.Select(field => new PersonFieldChangeDto(
                field.Name, field.Label, null, snapshot[field.Name])).ToList());
    }

    public static async Task<bool> EnsureBaselineAsync(
        SatiContext context,
        Person person,
        CancellationToken cancellationToken = default)
    {
        if (context.PersonVersions.Local.Any(version => version.PersonId == person.Id) ||
            await context.PersonVersions.AsNoTracking().AnyAsync(
                version => version.PersonId == person.Id,
                cancellationToken))
        {
            return false;
        }

        var snapshot = Capture(person);
        AddVersion(context, null, person, "TrackingBaseline", snapshot,
            Fields.Select(field => new PersonFieldChangeDto(
                field.Name, field.Label, null, snapshot[field.Name])).ToList());
        return true;
    }

    public static bool RecordChanged(
        SatiContext context,
        User actor,
        Person person,
        IReadOnlyDictionary<string, string?> before,
        string changeKind)
    {
        var after = Capture(person);
        var changes = Fields
            .Where(field => !string.Equals(before[field.Name], after[field.Name], StringComparison.Ordinal))
            .Select(field => new PersonFieldChangeDto(
                field.Name,
                field.Label,
                before[field.Name],
                after[field.Name]))
            .ToList();
        if (changes.Count == 0)
            return false;

        person.Revision++;
        AddVersion(context, actor, person, changeKind, after, changes);
        return true;
    }

    public static PersonVersionDto ToDto(PersonVersion version) => new(
        version.Id,
        version.PersonId,
        version.Version,
        version.ChangeKind,
        version.ActorUserId,
        version.ActorDisplayName,
        version.ChangedAtUtc,
        version.CorrelationId,
        Deserialize<List<PersonFieldChangeDto>>(version.ChangesGzip) ?? []);

    private static void AddVersion(
        SatiContext context,
        User? actor,
        Person person,
        string changeKind,
        IReadOnlyDictionary<string, string?> snapshot,
        IReadOnlyList<PersonFieldChangeDto> changes)
    {
        context.PersonVersions.Add(new PersonVersion
        {
            Person = person.Id == 0 ? person : null!,
            PersonId = person.Id,
            AgencyId = person.AgencyId ?? actor?.AgencyId ?? 0,
            ActorUserId = actor?.Id ?? 0,
            ActorDisplayName = actor?.DisplayName ?? "Sati tracking baseline",
            Version = person.Revision,
            ChangeKind = changeKind,
            ChangedAtUtc = DateTime.UtcNow,
            CorrelationId = $"desktop-{Guid.NewGuid():N}",
            SnapshotGzip = Serialize(snapshot),
            ChangesGzip = Serialize(changes)
        });
    }

    private static byte[] Serialize<T>(T value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, value, JsonOptions);
        return output.ToArray();
    }

    private static T? Deserialize<T>(byte[] value)
    {
        using var input = new MemoryStream(value);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<T>(gzip, JsonOptions);
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private sealed record Field(string Name, string Label, Func<Person, string?> Value);
}
