using System.Security.Cryptography;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Wraps per-record data keys with the Windows user's own DPAPI key, for Local
/// Production where there is no Key Vault to reach.
///
/// The API's wrapping key lives in Azure and never leaves it. A workstation has no
/// equivalent, and the alternatives were all worse: a key file beside the database
/// protects nothing, a passphrase becomes a second password to forget, and shipping
/// the cloud key to every desktop is the thing CLAUDE.md forbids. DPAPI is the one
/// option where the operating system holds the key material, tied to the signed-in
/// Windows account, and no secret is stored in the application at all.
///
/// WHAT THIS PROTECTS AGAINST: a copied database file. The <c>.mdf</c> lifted off
/// this machine, or read by a different Windows account on it, will not unwrap —
/// the data keys are inert without that user's DPAPI key. Together with the
/// BitLocker requirement in <c>OPERATIONS.md</c>, that covers a stolen or salvaged
/// laptop.
///
/// WHAT IT DOES NOT PROTECT AGAINST: anything running as that user while they are
/// signed in. DPAPI is a boundary between Windows accounts and machines, not between
/// programs. On a single-operator workstation that is the boundary that matters, and
/// it is the honest limit of what a local database can offer.
///
/// RECOVERY: if the Windows profile is lost or the account is recreated, wrapped
/// keys are unrecoverable and the stored numbers are gone. That is acceptable here
/// precisely because Sati is not the system of record for an SSN — Credible is, and
/// re-entering from there is the recovery procedure. It would not be acceptable for
/// data that exists nowhere else, and nothing else in Sati is stored this way.
/// </summary>
public sealed class DpapiKeyWrapper : IKeyWrapper
{
    /// <summary>
    /// Recorded on every row so a future scheme change is distinguishable rather than
    /// a silent failure to decrypt. If this ever becomes "dpapi.v2", rows wrapped
    /// under v1 still say so and can be migrated deliberately.
    /// </summary>
    public const string KeyIdentifier = "dpapi.user.v1";

    /// <summary>
    /// Additional entropy mixed into every wrap. Not a secret — it ships in the
    /// binary — but it scopes the protection to Sati, so another program running as
    /// the same user cannot unwrap these blobs simply by handing them to DPAPI.
    /// </summary>
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Sati.LocalProduction.Ssn.v1");

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var wrapped = ProtectedData.Protect(dataKey, Entropy, DataProtectionScope.CurrentUser);
        return Task.FromResult(new WrappedDataKey(wrapped, KeyIdentifier));
    }

    public Task<byte[]> UnwrapAsync(
        byte[] wrappedKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();

        // A row wrapped by some other scheme must fail closed rather than be handed
        // to DPAPI, which would report a confusing cryptographic error instead of the
        // real cause.
        if (!string.Equals(keyId, KeyIdentifier, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                $"This value was wrapped as '{keyId}', which this workstation cannot unwrap. " +
                "It was most likely written by a different environment or a different Windows account.");
        }

        try
        {
            return Task.FromResult(
                ProtectedData.Unprotect(wrappedKey, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException failure)
        {
            // The predictable way this happens is a database that moved: restored on a
            // different machine, opened under a different Windows account, or reached
            // after a profile was recreated. DPAPI reports that as a generic
            // cryptographic error, which tells a case manager nothing. The stored
            // last-four still displays, so without this the symptom is a mask that
            // looks fine beside a reveal that fails for no stated reason.
            throw new CryptographicException(
                "This Social Security number was encrypted by a different Windows account or on a " +
                "different computer, so it cannot be read here. Re-enter it from Credible on this " +
                "machine. Numbers do not travel with a copied database.",
                failure);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local SSN protection requires Windows DPAPI.");
    }
}
