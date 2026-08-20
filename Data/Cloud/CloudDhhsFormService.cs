using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

/// <summary>
/// The Demo and future cloud-Production implementation: the server fills the form
/// and the client receives finished bytes.
///
/// The division is not arbitrary. The Appointment form has an SSN box, SSNs are
/// encrypted under a Key Vault key only the API can reach, and the plaintext must
/// never land on a workstation — so on this path the filling happens where the
/// number already is, and what crosses the wire is a PDF rather than a consumer's
/// Social Security number. <see cref="DhhsFormService"/> is the counterpart for
/// local Production, where there is no SSN to protect and no network to use.
///
/// Both implementations answer the same shape, so a ViewModel does not branch on
/// environment.
/// </summary>
public sealed class CloudDhhsFormService(CloudApiClient client) : IDhhsFormService
{
    /// <summary>
    /// Names the boxes the server left blank. Pipe-separated because a field name on
    /// these forms is a sentence with commas in it.
    /// </summary>
    private const string UnfilledHeader = "X-Sati-Unfilled-Fields";

    public bool SupportsSsnStorage => true;

    /// <summary>
    /// The cloud path never reveals. Plaintext stays inside the API and reaches a
    /// workstation only as pixels in a generated PDF — that containment is the whole
    /// reason the filler runs server-side, and a reveal endpoint would undo it.
    /// </summary>
    public bool SupportsSsnReveal => false;

    public Task<string> RevealSsnAsync(
        int personId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "A stored Social Security number is never sent to this workstation from the cloud. " +
            "It appears only on the generated form.");

    public Task<SsnStatusDto> GetSsnStatusAsync(
        int personId,
        CancellationToken cancellationToken = default) =>
        client.GetAsync<SsnStatusDto>($"/api/v1/people/{personId}/ssn", cancellationToken);

    public Task<SsnStatusDto> UpdateSsnAsync(
        int personId,
        string? socialSecurityNumber,
        CancellationToken cancellationToken = default) =>
        client.PutAsync<SsnUpdateRequest, SsnStatusDto>(
            $"/api/v1/people/{personId}/ssn",
            new SsnUpdateRequest(socialSecurityNumber),
            cancellationToken);

    public async Task<DhhsFormResult> GenerateAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var request = new DhhsFormRequest(form.ToString(), selections.Checks, selections.Text);
        var (pdf, headers) = await client.PostBytesWithHeaderAsync(
            $"/api/v1/people/{personId}/forms.pdf",
            request,
            UnfilledHeader,
            cancellationToken);

        var unfilled = headers.Count == 0
            ? []
            : headers[0].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new DhhsFormResult(
            pdf,
            DhhsFormService.SuggestedFileName(form, null, null, personId),
            unfilled);
    }
}
