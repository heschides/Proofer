namespace Sati.Contracts.V1;

/// <summary>
/// Asks the server to fill one official DHHS form for one consumer.
///
/// <paramref name="Checks"/> and <paramref name="Text"/> carry consent choices the
/// case manager recorded on the consumer's instruction. They are the only way a box
/// on the consent half of either form gets set: nothing is inferred from the
/// profile, and the server refuses any name that is not a consent field of the
/// requested form. See <see cref="DhhsFormDefinition"/>.
///
/// Omitting both is the ordinary case — demographics filled, every consent box left
/// blank for the consumer to complete and sign.
/// </summary>
public sealed record DhhsFormRequest(
    string Form,
    IReadOnlyDictionary<string, bool>? Checks = null,
    IReadOnlyDictionary<string, string>? Text = null);

/// <summary>
/// What a client is told about a stored SSN: the mask and nothing else.
///
/// There is no DTO anywhere that carries a plaintext SSN outward. The number is
/// decrypted only inside the API, only during an audited form fill, and it leaves
/// the process only as pixels inside the finished PDF.
/// </summary>
public sealed record SsnStatusDto(string Masked, bool IsOnFile);

/// <summary>
/// Sets or clears a consumer's SSN. Inbound only.
///
/// <paramref name="Ssn"/> is normalized and shape-checked server-side before it is
/// encrypted; null or empty clears the stored number. This record is never returned
/// from any route.
/// </summary>
public sealed record SsnUpdateRequest(string? Ssn);

/// <summary>
/// A filled form, with whatever the server could not fill named rather than left for
/// the case manager to discover on the printed page.
///
/// <paramref name="BlankFields"/> is a non-blocking warning: a representative
/// without a phone number on file, or an SSN in an environment that does not store
/// them, produces a form that is still correct and still usable — it just needs
/// those boxes completed by hand. The fill never fails over a missing value.
/// </summary>
public sealed record DhhsFormResult(
    byte[] Pdf,
    string FileName,
    IReadOnlyList<string> BlankFields);
