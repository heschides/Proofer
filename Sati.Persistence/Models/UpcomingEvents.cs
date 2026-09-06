using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{

    public record UpcomingEvent
    {
        public int PersonId { get; init; }
        public string ClientName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public DateTime Date { get; init; }
        public UpcomingEventKind Kind { get; init; }

        // Form-only context used by the note panel's concise work-status cue.
        // Scheduled visits, contacts, and reminders leave these null.
        public FormType? FormType { get; init; }
        public DateTime? OpenDate { get; init; }
        public DateTime? OpenedDate { get; init; }
    }
}
