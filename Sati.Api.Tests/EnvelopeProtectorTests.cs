using Sati.Contracts.V1;
using System.Security.Cryptography;
using Sati.Api.Security;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Stands in for Azure Key Vault: a wrapping key per version, held in memory.
///
/// The real key never leaves the vault, which is precisely what a test cannot
/// reach. Substituting here exercises the envelope, the binding, and the tamper
/// behaviour without a network or a secret — <see cref="EnvelopeProtector"/> is
/// unchanged, and <see cref="Rotate"/> reproduces the one operation rotation
/// actually performs: new wraps use a new version, old rows keep theirs.
/// </summary>
internal sealed class TestKeyWrapper : IKeyWrapper
{
    private readonly Dictionary<string, byte[]> _versions = new(StringComparer.Ordinal);
    private string _current = null!;

    public TestKeyWrapper() => Rotate();

    public string CurrentKeyId => _current;

    /// <summary>Publishes a new key version and makes it current, as a vault rotation does.</summary>
    public string Rotate()
    {
        _current = $"https://vault.invalid/keys/ssn/{_versions.Count + 1:D3}";
        _versions[_current] = RandomNumberGenerator.GetBytes(32);
        return _current;
    }

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken = default)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_versions[_current], 16))
            aes.Encrypt(nonce, dataKey, ciphertext, tag);
        return Task.FromResult(new WrappedDataKey([.. nonce, .. tag, .. ciphertext], _current));
    }

    public Task<byte[]> UnwrapAsync(
        byte[] wrappedKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (!_versions.TryGetValue(keyId, out var wrappingKey))
            throw new CryptographicException($"Unknown key version '{keyId}'.");

        var nonce = wrappedKey[..12];
        var tag = wrappedKey[12..28];
        var ciphertext = wrappedKey[28..];
        var dataKey = new byte[ciphertext.Length];
        using (var aes = new AesGcm(wrappingKey, 16))
            aes.Decrypt(nonce, ciphertext, tag, dataKey);
        return Task.FromResult(dataKey);
    }
}

public sealed class EnvelopeProtectorTests
{
    private static readonly FieldBinding Binding = new(AgencyId: 7, RecordId: 42, FieldName: "Ssn");
    private const string Ssn = "123456789";

    [Fact]
    public async Task A_protected_value_round_trips()
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());
        var stored = await protector.ProtectAsync(Ssn, Binding);

        Assert.Equal(Ssn, await protector.UnprotectAsync(stored, Binding));
    }

    /// <summary>
    /// The ciphertext must not be a fingerprint. Two consumers with the same SSN, or
    /// one consumer's SSN re-saved, must not produce matching bytes — otherwise the
    /// column leaks equality to anyone who can read it, without any key at all.
    /// </summary>
    [Fact]
    public async Task The_same_number_never_encrypts_to_the_same_bytes()
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());

        var first = await protector.ProtectAsync(Ssn, Binding);
        var second = await protector.ProtectAsync(Ssn, Binding);

        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.WrappedDataKey, second.WrappedDataKey);
    }

    /// <summary>
    /// The reason for AES-GCM over a mode without a tag: a modified row fails loudly
    /// rather than returning a plausible wrong number onto a state form.
    /// </summary>
    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("wrapped-key")]
    public async Task A_tampered_value_fails_rather_than_decrypting(string part)
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());
        var stored = await protector.ProtectAsync(Ssn, Binding);

        var tampered = part switch
        {
            "ciphertext" => stored with { Ciphertext = Flip(stored.Ciphertext) },
            "nonce" => stored with { Nonce = Flip(stored.Nonce) },
            "tag" => stored with { Tag = Flip(stored.Tag) },
            _ => stored with { WrappedDataKey = Flip(stored.WrappedDataKey) },
        };

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => protector.UnprotectAsync(tampered, Binding));
    }

    /// <summary>
    /// The swap protection Josh asked for. Envelope encryption alone stops an
    /// outsider; binding tenant, record, and field into the AAD is what stops someone
    /// who can write to the table from moving one consumer's SSN onto another's row
    /// and having it decrypt cleanly.
    /// </summary>
    [Theory]
    [InlineData(8, 42, "Ssn")]      // another agency
    [InlineData(7, 43, "Ssn")]      // another consumer
    [InlineData(7, 42, "TaxId")]    // another field on the same row
    public async Task A_ciphertext_moved_anywhere_else_fails(int agencyId, int recordId, string field)
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());
        var stored = await protector.ProtectAsync(Ssn, Binding);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => protector.UnprotectAsync(stored, new FieldBinding(agencyId, recordId, field)));
    }

    /// <summary>
    /// Rotation must not require a backfill. Each row records the key version that
    /// wrapped it, so publishing a new version changes what new rows use and leaves
    /// every existing row readable.
    /// </summary>
    [Fact]
    public async Task Rotating_the_vault_key_leaves_existing_rows_readable()
    {
        var vault = new TestKeyWrapper();
        var protector = new EnvelopeProtector(vault);

        var beforeRotation = await protector.ProtectAsync(Ssn, Binding);
        var oldKeyId = vault.CurrentKeyId;

        var newKeyId = vault.Rotate();
        var afterRotation = await protector.ProtectAsync("987654321", Binding);

        Assert.Equal(oldKeyId, beforeRotation.KeyId);
        Assert.Equal(newKeyId, afterRotation.KeyId);
        Assert.NotEqual(oldKeyId, newKeyId);

        Assert.Equal(Ssn, await protector.UnprotectAsync(beforeRotation, Binding));
        Assert.Equal("987654321", await protector.UnprotectAsync(afterRotation, Binding));
    }

    /// <summary>
    /// A row is inert without the vault. If the wrapping key version is gone —
    /// destroyed, or an environment's vault swapped for another's — the row must fail
    /// closed rather than surrender a number.
    /// </summary>
    [Fact]
    public async Task Without_the_wrapping_key_the_row_is_inert()
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());
        var stored = await protector.ProtectAsync(Ssn, Binding);

        var otherVault = new EnvelopeProtector(new TestKeyWrapper());

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => otherVault.UnprotectAsync(stored, Binding));
    }

    /// <summary>Every part needed to decrypt is recorded, and the plaintext is not among them.</summary>
    [Fact]
    public async Task The_stored_shape_carries_no_plaintext()
    {
        var protector = new EnvelopeProtector(new TestKeyWrapper());
        var stored = await protector.ProtectAsync(Ssn, Binding);

        Assert.Equal(12, stored.Nonce.Length);
        Assert.Equal(16, stored.Tag.Length);
        Assert.NotEmpty(stored.WrappedDataKey);
        Assert.Contains("/keys/ssn/", stored.KeyId);
        Assert.DoesNotContain(Ssn, System.Text.Encoding.UTF8.GetString(stored.Ciphertext));
    }

    private static byte[] Flip(byte[] source)
    {
        var copy = source.ToArray();
        copy[^1] ^= 0xFF;
        return copy;
    }
}
