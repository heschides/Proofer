using System.Collections.Concurrent;
using Azure.Core;
using Sati.Contracts.V1;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Sati.Api.Security;

/// <summary>
/// The production wrapper: RSA-OAEP-256 against a key that never leaves Azure Key
/// Vault, reached with the API's managed identity.
///
/// The identity needs <c>wrapKey</c> and <c>unwrapKey</c> and nothing else. It
/// cannot read, export, or delete the key, so a compromise of the API process buys
/// an attacker the ability to unwrap data keys while it runs — not a copy of the key
/// to take away. Granting <c>get</c> or <c>list</c> here would give up most of that.
///
/// <see cref="_keyUri"/> is deliberately versionless: wrapping always uses the
/// current version, and the version that was current is recorded per row by
/// <see cref="WrappedDataKey.KeyId"/>. Rotating in Key Vault therefore needs no
/// deployment and no backfill.
/// </summary>
internal sealed class KeyVaultKeyWrapper : IKeyWrapper
{
    private static readonly KeyWrapAlgorithm Algorithm = KeyWrapAlgorithm.RsaOaep256;

    private readonly Uri _keyUri;
    private readonly TokenCredential _credential;

    // One client per key version. Creating a CryptographyClient is cheap but not
    // free, and a busy form-fill hour would otherwise rebuild the same few.
    private readonly ConcurrentDictionary<string, CryptographyClient> _clients = new(StringComparer.Ordinal);

    public KeyVaultKeyWrapper(Uri keyUri, TokenCredential credential)
    {
        _keyUri = keyUri;
        _credential = credential;
    }

    public async Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default)
    {
        var client = ClientFor(_keyUri.ToString());
        var result = await client.WrapKeyAsync(Algorithm, dataKey, cancellationToken);
        return new WrappedDataKey(result.EncryptedKey, result.KeyId);
    }

    public async Task<byte[]> UnwrapAsync(
        byte[] wrappedKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        var client = ClientFor(keyId);
        var result = await client.UnwrapKeyAsync(Algorithm, wrappedKey, cancellationToken);
        return result.Key;
    }

    private CryptographyClient ClientFor(string keyId) =>
        _clients.GetOrAdd(keyId, id => new CryptographyClient(new Uri(id), _credential));
}

/// <summary>
/// What is registered when no key is configured: every SSN operation fails closed.
///
/// The alternative — refusing to start — would take an entire environment's notes,
/// billing, and scheduling offline because a vault it never needed was not
/// provisioned. The alternative in the other direction, quietly storing plaintext,
/// is not an alternative. So the API starts, everything else works, and the first
/// attempt to store or read an SSN says exactly what is missing.
/// </summary>
internal sealed class UnconfiguredKeyWrapper : IKeyWrapper
{
    private const string Explanation =
        "SSN protection is not configured: set Ssn:KeyUri to an Azure Key Vault key and grant " +
        "this environment's managed identity wrapKey and unwrapKey on it. Demo and Production " +
        "must use different keys.";

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Explanation);

    public Task<byte[]> UnwrapAsync(
        byte[] wrappedKey,
        string keyId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Explanation);
}
