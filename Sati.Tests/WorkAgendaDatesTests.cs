using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class WorkAgendaDatesTests
{
    [Theory]
    [InlineData(2026, 8, 17, 2026, 8, 18)] // Monday -> Tuesday
    [InlineData(2026, 8, 20, 2026, 8, 21)] // Thursday -> Friday
    [InlineData(2026, 8, 21, 2026, 8, 24)] // Friday -> Monday
    [InlineData(2026, 8, 22, 2026, 8, 24)] // Saturday -> Monday
    [InlineData(2026, 8, 23, 2026, 8, 24)] // Sunday -> Monday
    public void NextWorkdaySkipsTheWeekend(
        int year, int month, int day,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var result = WorkAgendaDates.NextWorkday(new DateTime(year, month, day, 15, 30, 0));

        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay), result);
    }
}
