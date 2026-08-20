using System.Security.Cryptography;
using System.Text;

namespace Sati.Contracts.V1;

/// <summary>A data key as stored: wrapped, beside the identifier of the key that wrapped it.</summary>
/// <param name="WrappedKey">The wrapped data key. Inert without whatever holds the wrapping key.</param>
/// <param name="KeyId">Identifies the wrapping key, including its version, so rotation leaves old rows readable.</param>
public readonly record struct WrappedDataKey(byte[] WrappedKey, string KeyId);

/// <summary>
/// Wraps and unwraps per-record data keys.
///
/// This is the seam that lets one envelope implementation serve two very different
/// places to keep a key. In the API the wrapping key lives in Azure Key Vault and
/// never leaves it. On a workstation there is no vault, so the local implementation
/// uses the Windows user's own DPAPI key — which is why this is an interface rather
/// than a Key Vault client with a more general name.
/// </summary>
public interface IKeyWrapper
{
    /// <summary>Wraps a freshly generated data key under the current key.</summary>
    Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unwraps using <paramref name="keyId"/> — the one recorded on the row, not the
    /// current one. That is what makes rotation a non-event for existing data.
    /// </summary>
    Task<byte[]> UnwrapAsync(byte[] wrappedKey, string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the one field of the one record a ciphertext belongs to.
///
/// Bound into the encryption as additional authenticated data, so a ciphertext
/// lifted out of one consumer's row and pasted into another's fails to decrypt
/// instead of silently yielding the first consumer's SSN. Without this, envelope
/// encryption protects the value from an outsider but not from a row-level swap by
/// anyone who can write to the table.
/// </summary>
/// <param name="AgencyId">Tenant. A ciphertext never crosses an agency boundary.</param>
/// <param name="RecordId">The row.</param>
/// <param name="FieldName">The column, so two protected fields on one row are not interchangeable.</param>
public readonly record struct FieldBinding(int AgencyId, int RecordId, string FieldName)
{
    /// <summary>
    /// Canonical bytes for the binding. Pipe-separated with an explicit version tag:
    /// if the encoding ever changes, existing ciphertexts must fail loudly rather
    /// than decrypt under a differently-shaped AAD.
    /// </summary>
    public byte[] ToAad() =>
        Encoding.UTF8.GetBytes($"sati.v1|{AgencyId}|{RecordId}|{FieldName}");
}

/// <summary>
/// One protected value, in the shape it is stored.
///
/// Every part is required to decrypt, and none of them is secret on its own — the
/// data key is wrapped, so the row is inert without the Key Vault key named by
/// <paramref name="KeyId"/>.
/// </summary>
/// <param name="Ciphertext">AES-256-GCM ciphertext.</param>
/// <param name="Nonce">96-bit nonce, fresh per encryption.</param>
/// <param name="Tag">128-bit authentication tag.</param>
/// <param name="WrappedDataKey">The per-record data key, wrapped by the Key Vault key.</param>
/// <param name="KeyId">
/// Full Key Vault key identifier including its version. Stored per record so a
/// rotated key leaves existing rows readable — each row remembers which key version
/// wrapped it.
/// </param>
public sealed record ProtectedValue(
    byte[] Ciphertext,
    byte[] Nonce,
    byte[] Tag,
    byte[] WrappedDataKey,
    string KeyId);

/// <summary>
/// Envelope encryption for a single field.
///
/// A fresh 256-bit data key per record, used once, wrapped by a key that never
/// leaves Azure Key Vault. Per-record keys mean a compromised data key exposes one
/// consumer rather than the table, and they make rotation cheap: rotating the Key
/// Vault key changes what wraps new data keys, and old rows keep decrypting through
/// the key version recorded on them.
///
/// AES-GCM rather than CBC because the authentication tag is what makes tampering a
/// failure instead of a plausible-looking wrong answer. A modified ciphertext,
/// nonce, tag, or binding throws.
/// </summary>
public sealed class EnvelopeProtector
{
    // AES-256. GCM's standard nonce is 96 bits; longer or shorter nonces are legal
    // but lose the guarantee that a random nonce is safe for the volumes involved.
    private const int DataKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly IKeyWrapper _keyWrapper;

    public EnvelopeProtector(IKeyWrapper keyWrapper) => _keyWrapper = keyWrapper;

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under a data key generated for this call
    /// alone and bound to <paramref name="binding"/>.
    /// </summary>
    public async Task<ProtectedValue> ProtectAsync(
        string plaintext,
        FieldBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var dataKey = RandomNumberGenerator.GetBytes(DataKeyBytes);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var source = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[source.Length];
            var tag = new byte[TagBytes];

            using (var aes = new AesGcm(dataKey, TagBytes))
                aes.Encrypt(nonce, source, ciphertext, tag, binding.ToAad());

            CryptographicOperations.ZeroMemory(source);

            var wrapped = await _keyWrapper.WrapAsync(dataKey, cancellationToken);
            return new ProtectedValue(ciphertext, nonce, tag, wrapped.WrappedKey, wrapped.KeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Recovers the plaintext.
    ///
    /// Callers are expected to be few and audited: an SSN is decrypted only inside an
    /// audited form-fill, never on an ordinary read path. See <c>SsnMask</c> for what
    /// every other path shows instead.
    /// </summary>
    /// <exception cref="CryptographicException">
    /// The stored parts, or the binding, do not authenticate. A tampered or
    /// transplanted row lands here rather than returning a value.
    /// </exception>
    public async Task<string> UnprotectAsync(
        ProtectedValue value,
        FieldBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        var dataKey = await _keyWrapper.UnwrapAsync(value.WrappedDataKey, value.KeyId, cancellationToken);
        try
        {
            var plaintext = new byte[value.Ciphertext.Length];
            using (var aes = new AesGcm(dataKey, TagBytes))
                aes.Decrypt(value.Nonce, value.Ciphertext, value.Tag, plaintext, binding.ToAad());

            var result = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}
