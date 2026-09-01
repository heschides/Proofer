using Sati.Helpers;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class AgendaGreetingTests
{
    [Fact]
    public void EveryGreetingTemplateContainsItsRequiredPlaceholders()
    {
        foreach (var set in Enum.GetValues<AgendaGreetingSet>())
        {
            var templates = AgendaGreetings.Templates(set);
            Assert.NotEmpty(templates);
            foreach (var template in templates)
            {
                Assert.Contains("{0}", template);
                if (set is AgendaGreetingSet.Quiet or AgendaGreetingSet.AssessmentSuggested)
                    Assert.Contains("{1}", template);
                if (set == AgendaGreetingSet.AssessmentSuggested)
                    Assert.Contains("{2}", template);
            }
        }
    }

    [Fact]
    public void GreetingSelectionCoversEveryAgendaState()
    {
        Assert.Equal(AgendaGreetingSet.NoClients, AgendaGreetings.SelectSet(Result(0)));
        Assert.Equal(AgendaGreetingSet.Overdue, AgendaGreetings.SelectSet(Result(1, overdue: 1)));
        Assert.Equal(AgendaGreetingSet.ComingUp, AgendaGreetings.SelectSet(Result(1, upcoming: true)));
        Assert.Equal(AgendaGreetingSet.AssessmentSuggested,
            AgendaGreetings.SelectSet(Result(1, assessment: true)));
        Assert.Equal(AgendaGreetingSet.Quiet, AgendaGreetings.SelectSet(Result(1)));
    }

    [Fact]
    public void FormattedGreetingUsesStableSelectedIndex()
    {
        const int index = 3;

        var first = AgendaGreetings.Format(
            AgendaGreetingSet.AssessmentSuggested,
            index,
            "Case Manager One",
            30,
            "Alex");
        var second = AgendaGreetings.Format(
            AgendaGreetingSet.AssessmentSuggested,
            index,
            "Case Manager One",
            30,
            "Alex");

        Assert.Equal(first, second);
        Assert.Contains("Case Manager One", first);
        Assert.Contains("Alex", first);
    }

    private static DailyAgendaBuildResult Result(
        int people,
        int overdue = 0,
        bool upcoming = false,
        bool assessment = false)
    {
        var item = new DailyAgendaItem(
            "item",
            1,
            "Alex",
            "Q1 Review",
            new DateTime(2026, 9, 12),
            DailyAgendaItemKind.UpcomingWork,
            false);
        return new DailyAgendaBuildResult(
            people,
            overdue,
            overdue > 0 ? [item] : [],
            upcoming ? [item] : [],
            assessment ? item with { Kind = DailyAgendaItemKind.SuggestedAssessment } : null);
    }
}
