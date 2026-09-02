using Sati.Data;
using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class EasyEyesPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sati-easy-eyes-preferences-{Guid.NewGuid():N}");

    [Fact]
    public async Task NewUserDefaultsToOff()
    {
        var service = CreateService(SatiDataEnvironment.Demo);

        Assert.False(await service.LoadForUserAsync(41));
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public async Task EnabledPreferencePersistsAndNotifiesTheOpenShell()
    {
        var service = CreateService(SatiDataEnvironment.Demo);
        bool? notification = null;
        service.PreferenceChanged += (_, enabled) => notification = enabled;

        await service.SetEnabledAsync(41, true);

        Assert.True(service.IsEnabled);
        Assert.True(notification);
        Assert.True(await CreateService(SatiDataEnvironment.Demo).LoadForUserAsync(41));
    }

    [Fact]
    public async Task PreferenceIsIsolatedByUserAndEnvironment()
    {
        var path = PreferencePath();
        var demo = new EasyEyesPreferenceService(EnvironmentInfo(SatiDataEnvironment.Demo), path);
        var production = new EasyEyesPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Production), path);
        await demo.SetEnabledAsync(41, true);

        Assert.True(await demo.LoadForUserAsync(41));
        Assert.False(await demo.LoadForUserAsync(42));
        Assert.False(await production.LoadForUserAsync(41));
    }

    [Fact]
    public async Task CorruptFileFallsBackToOffAndIsNotOverwritten()
    {
        Directory.CreateDirectory(_directory);
        var path = PreferencePath();
        await File.WriteAllTextAsync(path, "not json");
        var service = new EasyEyesPreferenceService(
            EnvironmentInfo(SatiDataEnvironment.Demo), path);

        Assert.False(await service.LoadForUserAsync(41));
        Assert.NotNull(service.LastLoadWarning);
        await Assert.ThrowsAsync<EasyEyesPreferenceSaveException>(() =>
            service.SetEnabledAsync(41, true));
        Assert.Equal("not json", await File.ReadAllTextAsync(path));
    }

    private EasyEyesPreferenceService CreateService(SatiDataEnvironment environment) =>
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
