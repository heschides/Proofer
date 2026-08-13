namespace Sati.Contracts.V1;

using System.Text.Json;

/// <summary>
/// Exact billing inputs frozen when a service note becomes a claim line. Generators must use
/// this record instead of mutable Person or Agency rows so a later profile/configuration edit
/// cannot silently rewrite a submitted financial record.
/// </summary>
public sealed record ProfessionalClaimSnapshot(
    int Version,
    int AgencyId,
    int PersonId,
    string SubscriberFirstName,
    string SubscriberLastName,
    DateTime SubscriberBirthDate,
    string SubscriberGenderCode,
    string SubscriberMemberId,
    string SubscriberStreet,
    string SubscriberCity,
    string SubscriberState,
    string SubscriberZip,
    string BillingProviderName,
    string BillingProviderNpi,
    string BillingProviderTaxId,
    string BillingProviderStreet,
    string BillingProviderCity,
    string BillingProviderState,
    string BillingProviderZip,
    string SubmitterId,
    string SubmitterContactName,
    string SubmitterContactPhone,
    string PayerName,
    string PayerId);

public static class ProfessionalClaimSnapshotCodec
{
    public const int CurrentVersion = 1;

    public static string Serialize(ProfessionalClaimSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot);

    public static ProfessionalClaimSnapshot Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The claim line has no immutable billing snapshot.");

        try
        {
            var snapshot = JsonSerializer.Deserialize<ProfessionalClaimSnapshot>(json)
                ?? throw new InvalidOperationException("The claim snapshot is empty.");
            if (snapshot.Version != CurrentVersion)
                throw new InvalidOperationException($"Claim snapshot version {snapshot.Version} is not supported.");
            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The claim line contains an unreadable billing snapshot.", exception);
        }
    }
}
