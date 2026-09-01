using Sati.Data;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class DailyAgendaPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sati-agenda-preferences-{Guid.NewGuid():N}");

    [Fact]
    public async Task NewUserDefaultsToEnabledAndNotShown()
    {
        var service = CreateService(SatiDataEnvironment.Demo);

        var preference = await service.LoadForUserAsync(41);

        Assert.True(preference.ShowAtSignIn);
        Assert.Null(preference.LastShownDate);
    }

    [Fact]
    public async Task DisabledPreferencePersistsAcrossServiceInstances()
    {
        var first = CreateService(SatiDataEnvironment.Demo);
        await first.SetShowAtSignInAsync(41, false);

        var restarted = CreateService(SatiDataEnvironment.Demo);
        var preference = await restarted.LoadForUserAsync(41);

        Assert.False(preference.ShowAtSignIn);
    }

    [Fact]
    public async Task PreferenceAndLastShownDateAreIsolatedBySatiUser()
    {
        var service = CreateService(SatiDataEnvironment.Demo);
        var shownDate = new DateOnly(2026, 9, 1);
        await service.SetShowAtSignInAsync(41, false);
        await service.MarkShownAsync(41, shownDate);

        var userOne = await service.LoadForUserAsync(41);
        var userTwo = await service.LoadForUserAsync(42);

        Assert.False(userOne.ShowAtSignIn);
        Assert.Equal(shownDate, userOne.LastShownDate);
        Assert.True(userTwo.ShowAtSignIn);
        Assert.Null(userTwo.LastShownDate);
    }

    [Fact]
    public async Task SameUserInDifferentEnvironmentsHasIndependentPreference()
    {
        var path = PreferencePath();
        var demo = new DailyAgendaPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);
        var production = new DailyAgendaPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Production), path);
        await demo.SetShowAtSignInAsync(41, false);

        Assert.False((await demo.LoadForUserAsync(41)).ShowAtSignIn);
        Assert.True((await production.LoadForUserAsync(41)).ShowAtSignIn);
    }

    [Fact]
    public async Task CorruptFileFailsOpenForDisplayButIsNotOverwrittenOnSave()
    {
        Directory.CreateDirectory(_directory);
        var path = PreferencePath();
        await File.WriteAllTextAsync(path, "not json");
        var service = new DailyAgendaPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);

        var loaded = await service.LoadForUserAsync(41);

        Assert.True(loaded.ShowAtSignIn);
        Assert.NotNull(service.LastLoadWarning);
        await Assert.ThrowsAsync<DailyAgendaPreferenceSaveException>(() =>
            service.SetShowAtSignInAsync(41, false));
        Assert.Equal("not json", await File.ReadAllTextAsync(path));
    }

    private DailyAgendaPreferenceService CreateService(SatiDataEnvironment environment) =>
        new(EnvironmentInfo(environment), PreferencePath());

    private string PreferencePath() => Path.Combine(_directory, "preferences.json");

    private static DataEnvironmentInfo EnvironmentInfo(SatiDataEnvironment environment) =>
        environment == SatiDataEnvironment.Demo
            ? new DataEnvironmentInfo(
                environment,
                "SatiDemo",
                ApiBaseAddress: new Uri("https://demo.invalid"))
            : new DataEnvironmentInfo(
                environment,
                "SatiProduction",
                "SatiProduction",
                "Server=(localdb)\\MSSQLLocalDB;Database=SatiProduction;");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
