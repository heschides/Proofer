using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Sati
{
    public enum FormType
    {
        [Description("Q1 90-Day Review")] Q1R,
        [Description("Q2 90-Day Review")] Q2R,
        [Description("Q3 90-Day Review")] Q3R,
        [Description("Q4 90-Day Review")] Q4R,
        [Description("Person-Centered Plan")] PCP,
        [Description("Comprehensive Assessment")] ComprehensiveAssessment,
        [Description("Reclassification")] Reclassification,
        [Description("Safety Plan")] SafetyPlan,
        [Description("Privacy Practices")] PrivacyPractices,
        [Description("Agency Release")] Release_Agency,
        [Description("DHHS Release")] Release_DHHS,
        [Description("Medical Release")] Release_Medical
    }
    public enum UpcomingEventKind
    {
        OpenReview,
        LateReview,
        ScheduledVisit,
        ScheduledContact
    }

    public enum WaiverType
    {
        None,
        Section21,
        Section29
    }

    public enum NoteStatus
    {
        Scheduled,
        Pending,
        Logged,
        Cancelled,
        Delayed,
        Abandoned
    }

    public enum NoteType
    {
        Visit,
        Contact,
        Form,
        Other
    }
}

