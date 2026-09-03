using Sati.Models;


using Sati.Contracts.V1;

namespace Sati.Data
{
    public interface IFormService
    {
        Task UpdateFormAsync(Form form);
        Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null);
        Task AttestAsync(
            Form form,
            DateTime completedOn,
            int? evidenceNoteId,
            string? supervisorOverrideReason) =>
            supervisorOverrideReason is null
                ? AttestAsync(form, completedOn, evidenceNoteId)
                : throw new NotSupportedException("Supervisor prerequisite overrides are not available on this data path.");
        Task<FormPrerequisiteStatusDto> GetPrerequisiteStatusAsync(Form form) =>
            Task.FromResult(new FormPrerequisiteStatusDto(
                PrerequisiteKind.None.ToString(), true,
                "No additional prerequisite applies.", [], false));
        Task<DocumentArtifactDto> RecordExternalPrerequisiteAsync(Form form, string note) =>
            throw new NotSupportedException("External document recording is not available on this data path.");
        Task RevokeAttestationAsync(Form form, string reason);
        Task OpenFormAsync(Form form);
        Task DeleteFormsAsync(IEnumerable<Form> forms);
    }
}
