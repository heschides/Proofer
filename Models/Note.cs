using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Sati.Models
{
    public class Note : INoteInfo
    {
        public int Id { get; private set; }
        public string Narrative { get; set; } = string.Empty;
        public DateTime? EventDate { get; set; }
        public NoteStatus? Status { get; set; }
        public int? Minutes { get; set; }
        public int? StartTime { get; set; } // minutes offset from 7AM, e.g. 60 = 8AM
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public FormType? FormType { get; set; }
        public NoteType? NoteType { get; set; }
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; }
        public string? ReturnReason { get; set; }
        public int? ReturnedById { get; set; }
        public int? ApprovedById { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? CaseManagerJustification { get; set; }

        // Visit-only, user-verified facts. JSON keeps this small historical
        // snapshot owned by the note; PersonContact remains the live profile.
        public string? VisitDocumentationJson { get; set; }

        [NotMapped]
        public VisitDocumentation? VisitDocumentation
        {
            get => string.IsNullOrWhiteSpace(VisitDocumentationJson)
                ? null
                : JsonSerializer.Deserialize<VisitDocumentation>(VisitDocumentationJson);
            set => VisitDocumentationJson = value is null
                ? null
                : JsonSerializer.Serialize(value);
        }

        public bool ComplianceOverride { get; set; }
        public string? OverrideReason { get; set; }
        public int? OverrideApprovedById { get; set; }
        public DateTime? OverrideApprovedAt { get; set; }

        [NotMapped]
        public int? Units => Minutes.HasValue
            ? Math.Max(1, (int)Math.Ceiling(Minutes.Value / 15.0))
            : null;

        private protected Note() { }

        public static Note Create(string narrative, DateTime? eventDate, NoteStatus? status, int? minutes, int personId, FormType? formType = null, NoteType? noteType = null)
        {
            return new Note()
            {
                Narrative = narrative,
                EventDate = eventDate,
                Status = status,
                Minutes = minutes,
                PersonId = personId,
                FormType = formType,
                NoteType = noteType
            };
        }
    }
}
