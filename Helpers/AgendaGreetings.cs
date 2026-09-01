using Sati.Services;

namespace Sati.Helpers;

internal enum AgendaGreetingSet
{
    ComingUp,
    Overdue,
    NoClients,
    Quiet,
    AssessmentSuggested
}

internal static class AgendaGreetings
{
    private static readonly string[] ComingUp =
    [
        "Hello, {0}. Here's what's coming up on your caseload. Select anything you'd like on today's agenda.",
        "Welcome back, {0}. A few items are due soon. Choose the ones you'd like to carry into today.",
        "Hi, {0}. These are the next items on your caseload. Pick whichever ones belong on today's list.",
        "Good to see you, {0}. Here's what Sati is tracking for the days ahead. Select what you'd like to work on.",
        "Hello again, {0}. Some deadlines are approaching. Choose any you'd like to add to today's work.",
        "Welcome, {0}. Here's a short list of what's coming due. Take whatever makes sense for today.",
        "Hi there, {0}. These items are waiting on your caseload. Select the ones you want for today.",
        "Hello, {0}. Sati has gathered your upcoming items. Choose where you'd like to start.",
        "Good to have you back, {0}. A few things need attention soon. Pick whatever fits today.",
        "Welcome back, {0}. Here's what lies ahead. Add anything you'd like to today's work.",
        "Hello, {0}. These are your nearest deadlines. Select what you want on today's agenda.",
        "Hi, {0}. Here's what's coming due. Choose the items you'd like to take on.",
        "Welcome, {0}. A handful of items are coming due. Pick whichever ones belong on today's list.",
        "Hello, {0}. Here's the upcoming work on your caseload. Select any you'd like to add to today.",
        "Good to see you, {0}. These items come due soon. Choose what you'd like to handle today.",
        "Hi there, {0}. Sati has your next items ready. Take whichever ones fit today.",
        "Hello again, {0}. Here's what's on the horizon. Select anything for today's work.",
        "Welcome back, {0}. A few deadlines are close. Pick the ones you want on today's agenda.",
        "Hi, {0}. Here's what's next on your caseload. Choose what you'd like to bring into today.",
        "Hello, {0}. These items are coming due. Select any you'd like to work on today."
    ];

    private static readonly string[] Overdue =
    [
        "Hello, {0}. Your records show a few items needing attention. Take a moment to review them; some may simply need their status brought up to date.",
        "Welcome back, {0}. A few items are showing as overdue. They're worth a look, since some may already be done and not yet recorded.",
        "Hi, {0}. These items are past due in your records. Some may need work, and others may need only recording.",
        "Hello, {0}. Sati still shows these as outstanding. Review them, then either finish the work or record what's already complete.",
        "Good to see you, {0}. Some items have passed their due dates. A quick pass will show which need doing and which need only a status update.",
        "Hi there, {0}. Your records list these as incomplete. Check whether each still needs work or has simply gone unmarked.",
        "Hello again, {0}. A few items slipped past their due dates. Review their status when you have a moment.",
        "Welcome, {0}. These items are overdue according to your records. Some may need work; others may need only an update.",
        "Hello, {0}. Sati still has these open. It's worth confirming whether each one is genuinely outstanding.",
        "Hi, {0}. These items are past due. Take a moment to bring their status current, whether or not the work is done.",
        "Welcome back, {0}. A handful of items are showing overdue. Reviewing them will clear anything already finished.",
        "Hello, {0}. These items remain open past their due dates. Check each one and record any that are already complete."
    ];

    private static readonly string[] NoClients =
    [
        "Hello, {0}. Your caseload is empty right now. Add a client to begin tracking reviews, forms, and deadlines.",
        "Welcome, {0}. Once you add your first client, Sati will begin tracking what's due and when.",
        "Hi, {0}. There's nothing to show yet. Add a client, and your upcoming work will appear here.",
        "Hello, {0}. This is where your upcoming items will live. Add a client to get started.",
        "Welcome to Sati, {0}. Add your first client, and Sati will begin building your agenda.",
        "Hi there, {0}. You have no clients on your caseload yet. Add one to begin tracking reviews and forms.",
        "Hello, {0}. Your caseload is ready when you are. Add a client to see upcoming work here.",
        "Welcome back, {0}. There's nothing to list yet — adding a client is the first step.",
        "Hi, {0}. Sati will track deadlines for you once there's a caseload to follow. Add a client to begin.",
        "Hello, {0}. Add a client, and their reviews, forms, and due dates will begin appearing here."
    ];

    private static readonly string[] Quiet =
    [
        "Hello, {0}. Nothing is coming due in the next {1} days.",
        "Welcome back, {0}. No deadlines are approaching in the next {1} days.",
        "Hi, {0}. Nothing on your caseload comes due in the next {1} days.",
        "Hello, {0}. There's nothing due in the next {1} days.",
        "Good to see you, {0}. No items come due in the next {1} days.",
        "Hi there, {0}. Nothing is scheduled or due in the next {1} days.",
        "Hello again, {0}. No dates are approaching in the next {1} days.",
        "Welcome, {0}. Nothing comes due over the next {1} days.",
        "Hi, {0}. Your next {1} days are clear.",
        "Hello, {0}. No items are coming due in the next {1} days."
    ];

    private static readonly string[] AssessmentSuggested =
    [
        "Hello, {0}. Nothing comes due in the next {1} days. That makes it a good stretch for a Comprehensive Assessment, and {2}'s is next up.",
        "Welcome back, {0}. Your calendar is clear for the next {1} days. Consider putting time into {2}'s Comprehensive Assessment while there's room.",
        "Hi, {0}. Nothing comes due in the next {1} days. Assessments are meant to be built gradually, and {2}'s is the next one due.",
        "Hello, {0}. No dates are approaching in the next {1} days. This is a good time for a Comprehensive Assessment; {2}'s comes due soonest.",
        "Good to see you, {0}. Nothing is due in the next {1} days. {2}'s Comprehensive Assessment is next on the calendar if you'd like to make progress.",
        "Hi there, {0}. The next {1} days are open. Comprehensive Assessments reward steady work, and {2}'s is up next.",
        "Hello again, {0}. Nothing is pressing in the next {1} days. {2}'s Comprehensive Assessment is next due, if you'd like to chip away at it.",
        "Welcome, {0}. Nothing comes due in the next {1} days. A quiet stretch is a good time for {2}'s Comprehensive Assessment.",
        "Hi, {0}. Nothing is coming due in the next {1} days. {2}'s Comprehensive Assessment is the next one on the horizon.",
        "Hello, {0}. Nothing is due in the next {1} days. Consider spending the time on {2}'s Comprehensive Assessment — its due date is a deadline, not a start date."
    ];

    public static AgendaGreetingSet SelectSet(DailyAgendaBuildResult agenda) =>
        agenda.PersonCount == 0
            ? AgendaGreetingSet.NoClients
            : agenda.HasOverdue
                ? AgendaGreetingSet.Overdue
                : agenda.HasUpcoming
                    ? AgendaGreetingSet.ComingUp
                    : agenda.AssessmentSuggestion is not null
                        ? AgendaGreetingSet.AssessmentSuggested
                        : AgendaGreetingSet.Quiet;

    public static int Count(AgendaGreetingSet set) => Templates(set).Count;

    public static string Format(
        AgendaGreetingSet set,
        int index,
        string displayName,
        int lookaheadDays,
        string? assessmentPersonName = null)
    {
        var templates = Templates(set);
        if ((uint)index >= (uint)templates.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return string.Format(
            templates[index],
            displayName,
            lookaheadDays,
            assessmentPersonName ?? string.Empty);
    }

    internal static IReadOnlyList<string> Templates(AgendaGreetingSet set) => set switch
    {
        AgendaGreetingSet.ComingUp => ComingUp,
        AgendaGreetingSet.Overdue => Overdue,
        AgendaGreetingSet.NoClients => NoClients,
        AgendaGreetingSet.Quiet => Quiet,
        AgendaGreetingSet.AssessmentSuggested => AssessmentSuggested,
        _ => throw new ArgumentOutOfRangeException(nameof(set))
    };
}
