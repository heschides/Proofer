using System.Security.Cryptography;
using Sati.Contracts.V1;
using Sati.Data;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Local SSN protection, which is DPAPI wrapping the same envelope the API wraps with
/// Key Vault.
///
/// The property worth pinning is not that encryption works — that is
/// <c>EnvelopeProtectorTests</c>' job — but that a value which cannot be decrypted
/// here fails with something a case manager can act on. The realistic cause is a
/// database that moved between Windows accounts or machines, and the stored
/// last-four keeps displaying either way, so a bare cryptographic error would leave
/// a mask that looks healthy next to a reveal that fails for no stated reason.
/// </summary>
public sealed class DpapiKeyWrapperTests
{
    private static readonly FieldBinding Binding = new(AgencyId: 401, RecordId: 7, FieldName: "Ssn");

    [Fact]
    public async Task ANumberRoundTripsForTheSameWindowsUser()
    {
        var protector = new EnvelopeProtector(new DpapiKeyWrapper());

        var stored = await protector.ProtectAsync("123456789", Binding);

        Assert.Equal("123456789", await protector.UnprotectAsync(stored, Binding));
    }

    /// <summary>The key identifier is recorded so a future scheme change is distinguishable.</summary>
    [Fact]
    public async Task TheKeyIdentifierIsRecordedOnTheRow()
    {
        var stored = await new EnvelopeProtector(new DpapiKeyWrapper())
            .ProtectAsync("123456789", Binding);

        Assert.Equal(DpapiKeyWrapper.KeyIdentifier, stored.KeyId);
    }

    /// <summary>
    /// A wrapped key this account cannot open — the shape a copied database takes —
    /// must explain itself rather than surface as a raw cryptographic error.
    /// </summary>
    [Fact]
    public async Task AKeyThisAccountCannotOpenExplainsWhy()
    {
        var wrapper = new DpapiKeyWrapper();
        var stored = await new EnvelopeProtector(wrapper).ProtectAsync("123456789", Binding);

        var foreign = stored.WrappedDataKey.ToArray();
        foreign[^1] ^= 0xFF;

        var failure = await Assert.ThrowsAsync<CryptographicException>(
            () => wrapper.UnwrapAsync(foreign, DpapiKeyWrapper.KeyIdentifier));

        Assert.Contains("different Windows account", failure.Message);
        Assert.Contains("Re-enter it from Credible", failure.Message);
    }

    /// <summary>
    /// A row wrapped by Key Vault must not be handed to DPAPI. Naming the scheme it
    /// was written under is more useful than letting the platform report a mismatch it
    /// cannot explain.
    /// </summary>
    [Fact]
    public async Task ARowWrappedByAnotherSchemeIsRefusedByName()
    {
        var wrapper = new DpapiKeyWrapper();

        var failure = await Assert.ThrowsAsync<CryptographicException>(
            () => wrapper.UnwrapAsync([1, 2, 3], "https://sati-demo-kv.vault.azure.net/keys/ssn-demo/abc"));

        Assert.Contains("vault.azure.net", failure.Message);
        Assert.Contains("cannot unwrap", failure.Message);
    }

    /// <summary>
    /// The binding still applies locally: a ciphertext moved to another consumer's row
    /// fails rather than yielding the first consumer's number.
    /// </summary>
    [Fact]
    public async Task ACiphertextMovedToAnotherConsumerFails()
    {
        var protector = new EnvelopeProtector(new DpapiKeyWrapper());
        var stored = await protector.ProtectAsync("123456789", Binding);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => protector.UnprotectAsync(stored, Binding with { RecordId = 8 }));
    }
}
