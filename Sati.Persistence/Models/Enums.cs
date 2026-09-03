using System;
using System.ComponentModel;

namespace Sati
{
    // The three provider categories. Waiver is the one that matters for AT
    // passthrough; Healthcare and Other exist for directory completeness.
    public enum ProviderType
    {
        Waiver,
        Healthcare,
        Other
    }

    // Waiver services a Provider offers. [Flags] so one provider can offer several.
    // Only the four in-scope services are modeled now; AT Assessments and the rest
    // are deferred. Passthrough is NOT here — it's an orthogonal bool on Provider.
    [Flags]
    public enum WaiverService
    {
        None = 0,
        HomeSupport = 1,
        CommunitySupport = 2,
        SelfDirection = 4,
        CommunityMembership = 8
    }

    public enum FormWorkflowState
    {
        NotStarted,
        Opened,
        Completed
    }
    public enum BoardTab
    {
        CompAssessments,
        Reclasses,
        Pcps,
        Releases,
        Appointments,
        Reviews,
        EffectiveDates,
        All
    }

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
    public enum UserRole
    {
        CaseManager,
        Supervisor,
        Director,
        Admin,
        PlatformOperator
    }
    public enum UpcomingEventKind
    {
        OpenReview,
        LateReview,
        ScheduledVisit,
        ScheduledContact,
        ScheduledForm,
        ScheduledReminder,
        // Produced only by NextFormSuggestion for the note panel hint: a real, not-yet-due
        // form. The dashboard never sees it, because GenerateEvents never emits it.
        UpcomingForm
    }

    public enum Gender
    {
        Unknown,
        Male,
        Female,
        NonBinary
    }

    public enum WaiverType
    {
        None,
        Section21,
        Section29
    }

    // Persisted as int — do not reorder or renumber existing members; append only.
    // Ghost is a data-quality state about the record (should have been deleted inside
    // the creation window and was not), not a service fact about a person, but it lives
    // in the same enum for simplicity. Every clients-served count, report, and clinical
    // or billing surface must exclude Ghost explicitly. See HANDOFF_CLIENT_DELETION_POLICY.md.
    public enum PersonStatus
    {
        Active = 0,
        NoLongerServed = 1,
        Deceased = 2,
        Ghost = 3
    }

    public enum NoteStatus
    {
        Scheduled,
        Pending,
        Logged,
        HeldForCompliance,
        Cancelled,
        Delayed,
        Approved,
        Returned,
        Abandoned,
        ComplianceBlocked
    }

    public enum NoteType
    {
        Visit,
        Contact,
        Form,
        Other,
        // Appended last on purpose: the column is a nullable int, so every stored
        // value keeps its meaning and no migration is required. An undated
        // Reminder takes the journal-entry path; NoteSchedulingPolicy persists a
        // future-dated Reminder as Scheduled so the calendar can retrieve it.
        Reminder
    }

    public enum FormComplianceStatus
    {
        NotYetDue,
        InWindow,
        CompliantOnTime,
        CompliantLate,
        Overdue,
        NoForm
    }

    public enum FormCellStatus
    {
        Complete,
        DueThisMonth,
        DueNextMonth,
        NotYetOpen,
        Overdue
    }

    //Billing

    public enum BillingStatus
    {
        Draft,
        Submitted,
        Accepted,
        Rejected
    }

    public enum PlaceOfService
    {
        [Description("Office")]
        Office = 11,
        [Description("Home")]
        Home = 12,
        [Description("Group Home")]
        GroupHome = 14,
        [Description("Other")]
        Other = 99
    }

    public enum ATRequestStatus
    {
        Development,
        Review,
        Approved,
        Denied,
        Appeal,
        Received,
        Withdrawn
    }
}
