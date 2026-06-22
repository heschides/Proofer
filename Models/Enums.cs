using System.ComponentModel;

namespace Sati
{
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
        Admin
    }
    public enum UpcomingEventKind
    {
        OpenReview,
        LateReview,
        ScheduledVisit,
        ScheduledContact,
        ScheduledForm
    }

    public enum Gender
    {
        Unknown,
        Male,
        Female
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
        Other
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
}
