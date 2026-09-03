using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>
/// Creates Sati's medical-release variant. It deliberately shares the validated
/// disclosure choices and legal safeguards of the agency release while remaining
/// clearly labeled as a Sati document rather than an official Maine form.
/// </summary>
public sealed class MedicalReleasePdfGenerator(AgencyReleasePdfGenerator sharedGenerator)
{
    public byte[] Generate(
        AgencyReleaseSubject subject,
        AgencyReleaseRequest request,
        DateTime generatedAtUtc) =>
        sharedGenerator.GenerateMedical(subject, request, generatedAtUtc);
}
