using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Produces a filled official Maine DHHS form for a consumer.
///
/// Named for the forms it fills rather than the generic <c>IFormService</c>, which
/// already owns compliance forms, annual cycles, and quarterly reviews. The two have
/// nothing to do with each other.
///
/// ViewModels depend on this and never on HTTP, EF, Key Vault, or a database. The
/// two implementations differ in where the work happens and in what a consumer's
/// SSN can reach — see each one — but they answer the same shape, so the caller does
/// not branch on environment.
/// </summary>
public interface IDhhsFormService
{
    /// <summary>
    /// Whether this data path can store an encrypted SSN. Cloud-backed paths can;
    /// local Production deliberately cannot because no decryption key is placed on
    /// the workstation.
    /// </summary>
    bool SupportsSsnStorage { get; }

    /// <summary>
    /// Whether this path can show the case manager the actual number.
    ///
    /// Local Production can, because reading a consumer's SSN to the Social Security
    /// Administration on their behalf is routine work and cannot be done from a mask.
    /// The cloud path cannot, deliberately: plaintext never leaves the API, and the
    /// number reaches a workstation only as pixels inside a generated PDF.
    /// </summary>
    bool SupportsSsnReveal { get; }

    /// <summary>
    /// The stored number in full, for reading aloud or transcribing. Audited as a
    /// disclosure in its own right, separately from any document it feeds.
    /// </summary>
    /// <exception cref="InvalidOperationException">This path cannot reveal, or nothing is on file.</exception>
    Task<string> RevealSsnAsync(
        int personId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the mask only. Plaintext is never returned to the desktop.</summary>
    Task<SsnStatusDto> GetSsnStatusAsync(
        int personId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a new number to the authoritative store, or clears it when null. The
    /// response is a mask so the desktop never receives the plaintext back.
    /// </summary>
    Task<SsnStatusDto> UpdateSsnAsync(
        int personId,
        string? socialSecurityNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills <paramref name="form"/> for <paramref name="personId"/>.
    ///
    /// <paramref name="selections"/> carries only consent choices the case manager
    /// explicitly recorded on the consumer's instruction; nothing is inferred from the
    /// profile. Never throws over a value it could not supply — the result names the
    /// boxes left blank instead.
    /// </summary>
    Task<DhhsFormResult> GenerateAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        CancellationToken cancellationToken = default);
}
