using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;


namespace Sati.Data
{
    public class UpcomingEventService : IUpcomingEventService
    {
        public List<UpcomingEvent> GenerateEvents(IEnumerable<Person> people, Settings settings)
        {
            var today = DateTime.Today;
            var lookahead = today.AddDays(30);
            var events = new List<UpcomingEvent>();

            foreach (var person in people)
            {
                var anniversary = GetNextAnniversary(person.EffectiveDate);
                var prevAnniversary = anniversary.AddYears(-1);

                GenerateFormEvents(person, anniversary, prevAnniversary, today, lookahead, settings, events);
                GenerateScheduledNoteEvents(person, today, lookahead, events);
            }

            return events.OrderBy(e => e.Date).ToList();
        }

        private static DateTime GetNextAnniversary(DateTime effectiveDate)
        {
            var today = DateTime.Today;
            var anniversary = new DateTime(today.Year, effectiveDate.Month, effectiveDate.Day);
            if (anniversary <= today)
                anniversary = anniversary.AddYears(1);
            return anniversary;
        }

        private static void GenerateFormEvents(Person person, DateTime anniversary, DateTime prevAnniversary, DateTime today, DateTime lookahead, Settings settings, List<UpcomingEvent> events)
        {
            // Annual forms due at anniversary − 30
            var minus30Forms = new[]
            {
    (FormType.PCP,              settings.PcpOpenDaysBefore,              settings.PcpDaysAfterDue,              "PCP"),
    (FormType.SafetyPlan,       settings.SafetyPlanOpenDaysBefore,       settings.SafetyPlanDaysAfterDue,       "Safety Plan"),
    (FormType.PrivacyPractices, settings.PrivacyPracticesOpenDaysBefore, settings.PrivacyPracticesDaysAfterDue, "Privacy Practices"),
    (FormType.Release_Agency,   settings.ReleaseAgencyOpenDaysBefore,    settings.ReleaseAgencyDaysAfterDue,    "Release — Agency"),
    (FormType.Release_DHHS,     settings.ReleaseDhhsOpenDaysBefore,      settings.ReleaseDhhsDaysAfterDue,      "Release — DHHS"),
    (FormType.Release_Medical,  settings.ReleaseMedicalOpenDaysBefore,   settings.ReleaseMedicalDaysAfterDue,   "Release — Medical"),
};

            foreach (var (type, openBefore, daysAfter, label) in minus30Forms)
                AddAnnualFormEvent(person, type, anniversary.AddDays(-30), openBefore, daysAfter, label, prevAnniversary, anniversary, today, lookahead, events);

            // Annual forms due at anniversary − 60
            var minus60Forms = new[]
            {
    (FormType.ComprehensiveAssessment, settings.CompAssessmentOpenDaysBefore,   settings.CompAssessmentDaysAfterDue,   "Comp. Assessment"),
    (FormType.Reclassification,        settings.ReclassificationOpenDaysBefore, settings.ReclassificationDaysAfterDue, "Reclassification"),
};

            foreach (var (type, openBefore, daysAfter, label) in minus60Forms)
                AddAnnualFormEvent(person, type, anniversary.AddDays(-60), openBefore, daysAfter, label, prevAnniversary, anniversary, today, lookahead, events);

            // 90-day reviews
            GenerateReviewEvents(person, prevAnniversary, anniversary, today, lookahead, settings, events);
        }

        private static void GenerateScheduledNoteEvents(Person person, DateTime today, DateTime lookahead, List<UpcomingEvent> events)
        {
            var scheduledNotes = person.Notes
                .Where(n => n.Status == NoteStatus.Scheduled &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value >= today &&
                            n.EventDate.Value <= lookahead)
                .OrderBy(n => n.EventDate);

            foreach (var note in scheduledNotes)
            {
                var kind = note.NoteType == NoteType.Contact
                    ? UpcomingEventKind.ScheduledContact
                    : UpcomingEventKind.ScheduledVisit;

                var label = note.NoteType == NoteType.Contact
                    ? $"Contact — {person.FullName}"
                    : $"Visit — {person.FullName}";

                events.Add(new UpcomingEvent
                {
                    ClientName = person.FullName,
                    Title = label,
                    Date = note.EventDate!.Value,
                    Kind = kind
                });
            }
        }

        private static void AddAnnualFormEvent(Person person, FormType type, DateTime dueDate, int openBefore, int daysAfter, string label, DateTime prevAnniversary, DateTime anniversary, DateTime today, DateTime lookahead, List<UpcomingEvent> events)
        {
            var isCompliant = person.Forms.Any(f => f.Type == type &&
                                                   f.IsCompliant &&
                                                   f.DueDate >= prevAnniversary &&
                                                   f.DueDate <= anniversary);
            if (isCompliant)
                return;

            var openDate = dueDate.AddDays(-openBefore);
            var lateDate = dueDate.AddDays(daysAfter);

            if (today > lateDate)
                return;

            if (today >= openDate)
            {
                var kind = today > dueDate ? UpcomingEventKind.LateReview : UpcomingEventKind.OpenReview;
                events.Add(new UpcomingEvent
                {
                    ClientName = person.FullName,
                    Title = label,
                    Date = dueDate,
                    Kind = kind
                });
            }
        }

        private static void GenerateReviewEvents(Person person, DateTime prevAnniversary, DateTime anniversary, DateTime today, DateTime lookahead, Settings settings, List<UpcomingEvent> events)
        {
            var reviewTypes = new[] { FormType.Q1R, FormType.Q2R, FormType.Q3R, FormType.Q4R };
            var intervals = new[] { 90, 180, 270, 365 };

            for (int i = 0; i < 4; i++)
            {
                var dueDate = prevAnniversary.AddDays(intervals[i]);
                var type = reviewTypes[i];

                var isCompliant = person.Forms.Any(f => f.Type == type &&
                                                       f.IsCompliant &&
                                                       f.DueDate >= prevAnniversary &&
                                                       f.DueDate <= anniversary);
                if (isCompliant)
                    continue;

                var openDate = dueDate.AddDays(-settings.ReviewOpenDaysBefore);
                var lateDate = dueDate.AddDays(settings.ReviewDaysAfterDue);

                if (today > lateDate)
                    continue;

                if (today >= openDate)
                {
                    var kind = today > dueDate ? UpcomingEventKind.LateReview : UpcomingEventKind.OpenReview;
                    events.Add(new UpcomingEvent
                    {
                        ClientName = person.FullName,
                        Title = $"Q{i + 1} Review",
                        Date = dueDate,
                        Kind = kind
                    });
                }
            }
        }
    }
}

