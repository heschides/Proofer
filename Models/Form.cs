namespace Sati.Models
{
    public class Form
    {
        public int Id { get; set; }
        public FormType Type { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsCompliant { get; private set; }
        public Person Person { get; set; } = null!; 
        public int PersonId { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime? OpenedDate { get; set; }

        // EF Core needs a parameterless constructor to materialize entities from the
        // database; it's protected so application code can't bypass the generation
        // constructor below.
        protected Form() { }

        // The ONE sanctioned way to create a form with an initial compliance value.
        // Generation is the single legitimate exception to "compliant implies a
        // completion date": an annual document born compliant at admission represents
        // a pre-existing, in-force record whose real completion date is unknown — the
        // exact state the backfill later resolves. Because this is the only entry point
        // that sets initial compliance, that exception lives here and nowhere else;
        // every post-creation change still goes through MarkComplete/Reset.
        public Form(FormType type, DateTime dueDate, bool isCompliant)
        {
            Type = type;
            DueDate = dueDate;
            IsCompliant = isCompliant;
        }

        // The only sanctioned writers of compliance state. IsCompliant and
        // CompletedDate must agree — compliant iff there's a completion date — and
        // routing every caller through these two methods is what guarantees that.
        // MarkComplete takes a real date by value (not nullable), so "compliant with
        // no date," the exact shape that breaks the windowed billing gate, is
        // unreachable through this door.
        //
        // The date is whatever the caller captured — the dialog's per-row picker, the
        // board's "today," a backfill's due date — never synthesized here, because the
        // late-vs-on-time distinction (CompletedDate vs DueDate) is a billing fact the
        // entity has no business guessing.
        public void MarkComplete(DateTime completedOn)
        {
            CompletedDate = completedOn;
            IsCompliant = true;
        }

        public void Reset()
        {
            CompletedDate = null;
            IsCompliant = false;
        }
    }
}