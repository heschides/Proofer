using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class PersonLifecycle(ApiDbContext db, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly SnapshotField[] Fields =
    [
        new("userId", "Assigned user ID", value => value.UserId),
        new("agencyId", "Agency ID", value => value.AgencyId),
        new("firstName", "First name", value => value.FirstName),
        new("lastName", "Last name", value => value.LastName),
        new("birthDate", "Birth date", value => value.BirthDate),
        new("gender", "Gender", value => value.Gender),
        new("effectiveDate", "Effective date", value => value.EffectiveDate),
        new("bio", "Biography", value => value.Bio),
        new("journal", "Journal", value => value.Journal),
        new("waiver", "Waiver", value => value.Waiver),
        new("maineCareId", "MaineCare ID", value => value.MaineCareId),
        new("diagnosisCode", "Diagnosis code", value => value.DiagnosisCode),
        new("placeOfService", "Place of service", value => value.PlaceOfService),
        new("evergreenId", "Evergreen ID", value => value.EvergreenId),
        new("openWithVR", "Open with VR", value => value.OpenWithVR),
        new("hasGuardian", "Has guardian", value => value.HasGuardian),
        new("guardianName", "Guardian name", value => value.GuardianName),
        new("phoneNumber", "Phone number", value => value.PhoneNumber),
        new("address", "Address", value => value.Address),
        new("primaryCareProvider", "Primary care provider", value => value.PrimaryCareProvider),
        new("healthcareSystemName", "Healthcare system", value => value.HealthcareSystemName),
        new("caseManagerIsRepPayee", "Case manager is representative payee", value => value.CaseManagerIsRepPayee),
        new("repPayeeMonthlyIncome", "Representative-payee monthly income", value => value.RepPayeeMonthlyIncome),
        new("repPayeeRegularCheckRequestNeeds", "Regular check-request needs", value => value.RepPayeeRegularCheckRequestNeeds),
        new("hasHomeSupport", "Home support", value => value.HasHomeSupport),
        new("hasSelfDirectedHomeSupport", "Self-directed home support", value => value.HasSelfDirectedHomeSupport),
        new("hasSharedLiving", "Shared living", value => value.HasSharedLiving),
        new("hasCommunitySupport1To1", "Community support 1:1", value => value.HasCommunitySupport1To1),
        new("hasCommunitySupportSelfDirected", "Self-directed community support", value => value.HasCommunitySupportSelfDirected),
        new("hasCommunitySupportDayProgram", "Community support day program", value => value.HasCommunitySupportDayProgram),
        new("dayProgramCount", "Day program count", value => value.DayProgramCount),
        new("hasEmploymentSpecialist", "Employment specialist", value => value.HasEmploymentSpecialist),
        new("hasWorkSupports", "Work supports", value => value.HasWorkSupports),
        new("isEmployed", "Employed", value => value.IsEmployed)
    ];

    public static PersonSnapshot Capture(ServerPerson person) => new(
        person.UserId,
        person.AgencyId,
        person.FirstName,
        person.LastName,
        person.BirthDate,
        NameAt(["Unknown", "Male", "Female", "NonBinary"], person.Gender),
        person.EffectiveDate,
        person.Bio,
        person.Journal,
        NameAt(["None", "Section21", "Section29"], person.Waiver),
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
        person.CaseManagerIsRepPayee,
        person.RepPayeeMonthlyIncome,
        person.RepPayeeRegularCheckRequestNeeds,
        person.HasHomeSupport,
        person.HasSelfDirectedHomeSupport,
        person.HasSharedLiving,
        person.HasCommunitySupport1To1,
        person.HasCommunitySupportSelfDirected,
        person.HasCommunitySupportDayProgram,
        person.DayProgramCount,
        person.HasEmploymentSpecialist,
        person.HasWorkSupports,
        person.IsEmployed);

    public void RecordCreated(Actor actor, ServerPerson person)
    {
        var snapshot = Capture(person);
        AddVersion(actor, person, "Created", snapshot,
            Fields.Select(field => ToChange(field, null, snapshot)).ToList());
    }

    public async Task<bool> EnsureBaselineAsync(
        ServerPerson person,
        CancellationToken cancellationToken)
    {
        if (db.PersonVersions.Local.Any(version => version.PersonId == person.Id) ||
            await db.PersonVersions.AsNoTracking().AnyAsync(
                version => version.PersonId == person.Id,
                cancellationToken))
        {
            return false;
        }

        var snapshot = Capture(person);
        var systemActor = new Actor(0, person.AgencyId ?? 0, "System", "Sati tracking baseline");
        AddVersion(systemActor, person, "TrackingBaseline", snapshot,
            Fields.Select(field => ToChange(field, null, snapshot)).ToList());
        return true;
    }

    public bool RecordChanged(
        Actor actor,
        ServerPerson person,
        PersonSnapshot before,
        string changeKind,
        IEnumerable<PersonFieldChangeDto>? additionalChanges = null)
    {
        var after = Capture(person);
        var changes = Fields
            .Where(field => !Equals(field.Value(before), field.Value(after)))
            .Select(field => ToChange(field, before, after))
            .ToList();
        if (additionalChanges is not null)
            changes.AddRange(additionalChanges);
        if (changes.Count == 0)
            return false;

        person.Revision++;
        AddVersion(actor, person, changeKind, after, changes);
        return true;
    }

    public static PersonVersionDto ToDto(ServerPersonVersion version) => new(
        version.Id,
        version.PersonId,
        version.Version,
        version.ChangeKind,
        version.ActorUserId,
        version.ActorDisplayName,
        version.ChangedAtUtc,
        version.CorrelationId,
        Deserialize<List<PersonFieldChangeDto>>(version.ChangesGzip) ?? []);

    private void AddVersion(
        Actor actor,
        ServerPerson person,
        string changeKind,
        PersonSnapshot snapshot,
        IReadOnlyList<PersonFieldChangeDto> changes)
    {
        db.PersonVersions.Add(new ServerPersonVersion
        {
            // Existing People may have been loaded through a read-oriented join
            // and therefore be detached. Linking that instance as a navigation
            // would make Add() traverse the graph and attempt to insert the
            // already-existing Person. New People still need the navigation so
            // EF can propagate their generated identity into the version row.
            Person = person.Id == 0 ? person : null!,
            PersonId = person.Id,
            AgencyId = actor.AgencyId,
            ActorUserId = actor.UserId,
            ActorDisplayName = actor.DisplayName,
            Version = person.Revision,
            ChangeKind = changeKind,
            ChangedAtUtc = DateTime.UtcNow,
            CorrelationId = httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty,
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

    private static PersonFieldChangeDto ToChange(
        SnapshotField field,
        PersonSnapshot? before,
        PersonSnapshot after) => new(
        field.Name,
        field.Label,
        before is null ? null : Format(field.Value(before)),
        Format(field.Value(after)));

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool flag => flag ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static string NameAt(string[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : values[0];

    private sealed record SnapshotField(
        string Name,
        string Label,
        Func<PersonSnapshot, object?> Value);
}

internal sealed record PersonSnapshot(
    int UserId,
    int? AgencyId,
    string? FirstName,
    string? LastName,
    DateTime BirthDate,
    string Gender,
    DateTime? EffectiveDate,
    string? Bio,
    string? Journal,
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
    string? PrimaryCareProvider,
    string? HealthcareSystemName,
    bool CaseManagerIsRepPayee,
    decimal? RepPayeeMonthlyIncome,
    string? RepPayeeRegularCheckRequestNeeds,
    bool HasHomeSupport,
    bool HasSelfDirectedHomeSupport,
    bool HasSharedLiving,
    bool HasCommunitySupport1To1,
    bool HasCommunitySupportSelfDirected,
    bool HasCommunitySupportDayProgram,
    int DayProgramCount,
    bool HasEmploymentSpecialist,
    bool HasWorkSupports,
    bool IsEmployed);
