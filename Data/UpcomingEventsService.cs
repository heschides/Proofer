using Sati.Models;

namespace Sati.Data
{
    public class UpcomingEventService : IUpcomingEventService
    {
        public List<UpcomingEvent> GenerateEvents(IEnumerable<IEventSource> people, Settings settings, DateTime? asOf = null)
        {
            var today = asOf ?? DateTime.Today;
            var events = new List<UpcomingEvent>();

            foreach (var person in people)
            {
                if (person.EffectiveDate is null)
                    continue;

                GenerateFormEvents(person, today, settings, events);
                GenerateScheduledNoteEvents(person, today, events);
            }

            return events.OrderBy(e => e.Date).ToList();
        }

        private static void GenerateFormEvents(IEventSource person, DateTime today,
                    Settings settings, List<UpcomingEvent> events)
        {
            // All 12 form types in one table. Due dates come from the stored
            // form record via GetCurrentCycleForm — never recomputed here.
            // That keeps FormDueDateCalculator as the single source of truth
            // and means settings changes propagate automatically.
            var formMeta = FormMeta(settings);

            foreach (var (type, openBefore, daysAfter, label) in formMeta)
            {
                var form = person.GetCurrentCycleForm(type, today);
                // IsSatisfiedAsOf, not IsCompliant: a completion dated in the future is
                // recorded but not yet in force, and the billing gate still treats
                // that form as outstanding. Asking the same question keeps this list
                // and the gate from naming different forms.
                if (form is null || form.IsSatisfiedAsOf(today))
                    continue;

                var dueDate = form.DueDate.Date;
                var openDate = dueDate.AddDays(-openBefore);
                var lateDate = dueDate.AddDays(daysAfter);

                if (today < openDate || today > lateDate)
                    continue;

                var kind = today > dueDate ? UpcomingEventKind.LateReview : UpcomingEventKind.OpenReview;
                events.Add(new UpcomingEvent
                {
                    PersonId = person.Id,
                    ClientName = person.FullName,
                    Title = $"{label} — {person.FullName}",
                    Date = dueDate,
                    Kind = kind
                });
            }
        }

        private static void GenerateScheduledNoteEvents(IEventSource person, DateTime today,
                    List<UpcomingEvent> events)
        {
            var lookahead = today.AddDays(30);

            var scheduledNotes = person.Notes
                .Where(n => n.Status == NoteStatus.Scheduled &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value >= today &&
                            n.EventDate.Value <= lookahead)
                .OrderBy(n => n.EventDate);

            foreach (var note in scheduledNotes)
            {
                var kind = note.NoteType switch
                {
                    NoteType.Contact => UpcomingEventKind.ScheduledContact,
                    NoteType.Form => UpcomingEventKind.ScheduledForm,
                    NoteType.Reminder => UpcomingEventKind.ScheduledReminder,
                    _ => UpcomingEventKind.ScheduledVisit
                };

                var label = note.NoteType switch
                {
                    NoteType.Contact => $"Contact — {person.FullName}",
                    NoteType.Form => $"Form — {person.FullName}",
                    NoteType.Reminder => $"Reminder — {person.FullName}",
                    _ => $"Visit — {person.FullName}"
                };

                events.Add(new UpcomingEvent
                {
                    PersonId = person.Id,
                    ClientName = person.FullName,
                    Title = label,
                    Date = note.EventDate!.Value,
                    Kind = kind
                });
            }
        }

        // One table, two readers. GenerateEvents uses it for the open/late window;
        // NextFormSuggestion uses it for the note panel's follow-up hint. A second
        // copy of these labels would let the two disagree about a form's name.
        private static (FormType Type, int OpenBefore, int DaysAfter, string Label)[] FormMeta(Settings settings) =>
        [
            (FormType.PCP,                     settings.PcpOpenDaysBefore,              settings.PcpDaysAfterDue,              "PCP"),
            (FormType.ComprehensiveAssessment, settings.CompAssessmentOpenDaysBefore,   settings.CompAssessmentDaysAfterDue,   "Comp. Assessment"),
            (FormType.Reclassification,        settings.ReclassificationOpenDaysBefore, settings.ReclassificationDaysAfterDue, "Reclassification"),
            (FormType.SafetyPlan,              settings.SafetyPlanOpenDaysBefore,       settings.SafetyPlanDaysAfterDue,       "Safety Plan"),
            (FormType.PrivacyPractices,        settings.PrivacyPracticesOpenDaysBefore, settings.PrivacyPracticesDaysAfterDue, "Privacy Practices"),
            (FormType.Release_Agency,          settings.ReleaseAgencyOpenDaysBefore,    settings.ReleaseAgencyDaysAfterDue,    "Release — Agency"),
            (FormType.Release_DHHS,            settings.ReleaseDhhsOpenDaysBefore,      settings.ReleaseDhhsDaysAfterDue,      "Release — DHHS"),
            (FormType.Release_Medical,         settings.ReleaseMedicalOpenDaysBefore,   settings.ReleaseMedicalDaysAfterDue,   "Release — Medical"),
            (FormType.Q1R,                     settings.ReviewOpenDaysBefore,           settings.ReviewDaysAfterDue,           "Q1 Review"),
            (FormType.Q2R,                     settings.ReviewOpenDaysBefore,           settings.ReviewDaysAfterDue,           "Q2 Review"),
            (FormType.Q3R,                     settings.ReviewOpenDaysBefore,           settings.ReviewDaysAfterDue,           "Q3 Review"),
            (FormType.Q4R,                     settings.ReviewOpenDaysBefore,           settings.ReviewDaysAfterDue,           "Q4 Review"),
        ];

        /// <summary>
        /// The client's next outstanding form by due date, ignoring the open/late
        /// window that <see cref="GenerateEvents"/> applies.
        ///
        /// GenerateEvents answers "what is actionable right now", which is correct
        /// for the dashboard but leaves the note panel's follow-up hint blank for
        /// most of every cycle: with the default zero-day review window a quarterly
        /// review is only ever "open" on its exact due date. This answers the
        /// different question the note panel actually asks — "what is coming up next
        /// for this client" — using the same stored form records, the same
        /// GetCurrentCycleForm lookup, and the same IsSatisfiedAsOf test, so it can
        /// never name a form the compliance gate considers already met.
        /// </summary>
        public UpcomingEvent? NextFormSuggestion(IEventSource person, Settings settings, DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;
            if (person.EffectiveDate is null)
                return null;

            UpcomingEvent? next = null;
            foreach (var (type, _, _, label) in FormMeta(settings))
            {
                var form = person.GetCurrentCycleForm(type, today);
                if (form is null || form.IsSatisfiedAsOf(today))
                    continue;

                var dueDate = form.DueDate.Date;
                if (next is not null && dueDate >= next.Date)
                    continue;

                next = new UpcomingEvent
                {
                    PersonId = person.Id,
                    ClientName = person.FullName,
                    Title = $"{label} — {person.FullName}",
                    Date = dueDate,
                    Kind = today > dueDate ? UpcomingEventKind.LateReview : UpcomingEventKind.UpcomingForm
                };
            }

            return next;
        }
    }
}
