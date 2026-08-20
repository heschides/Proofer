using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

/// <summary>
/// Reads and writes the encrypted SSN columns on the local database.
///
/// The columns are declared as EF shadow properties on <c>SatiContext</c>, so
/// <c>Person</c> has no property for them and this class is the only thing that can
/// reach them. That is deliberate and worth keeping: a ciphertext column that is not
/// on the entity cannot be projected into a DTO, bound to a control, written to a
/// log, or included in an export by someone who did not know it was there. Everything
/// that touches an SSN goes through here or through the API, and both keep the
/// plaintext to a single method call.
///
/// The envelope is the same one the API uses — <see cref="EnvelopeProtector"/>, per
/// record data key, AES-256-GCM, tenant and record and field bound in as additional
/// authenticated data. Only the wrapper differs: Key Vault there,
/// <see cref="DpapiKeyWrapper"/> here. So a ciphertext written on this workstation is
/// structurally identical to one written in Azure and equally unreadable anywhere
/// else, which is the property that matters.
/// </summary>
public sealed class LocalSsnStore(EnvelopeProtector protector)
{
    private const string Ciphertext = "SsnCiphertext";
    private const string Nonce = "SsnNonce";
    private const string Tag = "SsnTag";
    private const string WrappedKey = "SsnWrappedKey";
    private const string KeyId = "SsnKeyId";
    private const string LastFour = "SsnLastFour";

    /// <summary>
    /// The mask, read without touching the ciphertext.
    ///
    /// The stored last four is what the mask displays, so an ordinary screen costs a
    /// single column read and never involves a key. Nothing but an explicit reveal
    /// should be decrypting.
    /// </summary>
    public static string MaskFor(SatiContext context, Person person) =>
        SsnMask.Format(context.Entry(person).Property<string?>(LastFour).CurrentValue);

    public static bool IsOnFile(SatiContext context, Person person) =>
        !string.IsNullOrEmpty(context.Entry(person).Property<string?>(LastFour).CurrentValue);

    /// <summary>
    /// Recovers the number, for the one thing a case manager actually needs it for:
    /// reading it aloud to the Social Security Administration on the consumer's
    /// behalf, and filling the Appointment form.
    /// </summary>
    /// <exception cref="InvalidOperationException">No SSN is on file for this consumer.</exception>
    public async Task<string> RevealAsync(
        SatiContext context,
        Person person,
        CancellationToken cancellationToken = default)
    {
        var entry = context.Entry(person);
        var ciphertext = entry.Property<byte[]?>(Ciphertext).CurrentValue;
        var keyId = entry.Property<string?>(KeyId).CurrentValue;

        if (ciphertext is null || keyId is null)
            throw new InvalidOperationException("No Social Security number is on file for this consumer.");

        var stored = new ProtectedValue(
            ciphertext,
            entry.Property<byte[]>(Nonce).CurrentValue!,
            entry.Property<byte[]>(Tag).CurrentValue!,
            entry.Property<byte[]>(WrappedKey).CurrentValue!,
            keyId);

        return await protector.UnprotectAsync(stored, BindingFor(person), cancellationToken);
    }

    /// <summary>
    /// Stores a number, or clears it when <paramref name="normalized"/> is null.
    /// Does not save; the caller owns the transaction and the audit entry.
    /// </summary>
    public async Task SetAsync(
        SatiContext context,
        Person person,
        string? normalized,
        CancellationToken cancellationToken = default)
    {
        var entry = context.Entry(person);

        if (normalized is null)
        {
            // Every part goes, the last four included. Leaving the tail would keep a
            // consumer who asked to be removed partially on file, with a mask claiming
            // a number that can no longer be produced.
            foreach (var column in new[] { Ciphertext, Nonce, Tag, WrappedKey })
                entry.Property<byte[]?>(column).CurrentValue = null;
            entry.Property<string?>(KeyId).CurrentValue = null;
            entry.Property<string?>(LastFour).CurrentValue = null;
            return;
        }

        if (!SsnMask.IsWellFormed(normalized))
            throw new ArgumentException("That is not a structurally valid Social Security number.", nameof(normalized));

        var stored = await protector.ProtectAsync(normalized, BindingFor(person), cancellationToken);
        entry.Property<byte[]?>(Ciphertext).CurrentValue = stored.Ciphertext;
        entry.Property<byte[]?>(Nonce).CurrentValue = stored.Nonce;
        entry.Property<byte[]?>(Tag).CurrentValue = stored.Tag;
        entry.Property<byte[]?>(WrappedKey).CurrentValue = stored.WrappedDataKey;
        entry.Property<string?>(KeyId).CurrentValue = stored.KeyId;
        entry.Property<string?>(LastFour).CurrentValue = SsnMask.LastFourOf(normalized);
    }

    /// <summary>
    /// Binds the ciphertext to this consumer's row and this field, so a value moved
    /// to another row fails to decrypt instead of quietly yielding the wrong person's
    /// number. AgencyId is nullable on the local model; zero stands in for "no agency",
    /// consistently, because the binding only has to be stable rather than meaningful.
    /// </summary>
    private static FieldBinding BindingFor(Person person) =>
        new(person.AgencyId ?? 0, person.Id, "Ssn");
}
