using System;
using System.Collections.Generic;
using System.Text;


namespace Sati.Models
{
    public class Note
    {
        public int Id { get; private set; }
        public string Narrative { get; set;  } = string.Empty;
        public DateTime? EventDate { get; set; }
        public NoteStatus? Status { get; set; } 
        public int? Units {  get; set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public FormType? FormType { get; set; }
        public NoteType? NoteType { get; set; }


        private protected Note() { }

        public static Note Create( string narrative, DateTime? eventDate, NoteStatus? status, int? unitCount, int personId, FormType? formType=null, NoteType? noteType = null)
        {
            var _note = new Note()
            {
                Narrative = narrative,
                EventDate = eventDate,
                Status = status,
                Units = unitCount,
                PersonId = personId,
                FormType = formType,
                NoteType = noteType
            };
            return _note;
        }
    }
}
