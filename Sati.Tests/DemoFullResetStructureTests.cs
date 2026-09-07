using Xunit;

namespace Sati.Tests;

public sealed class DemoFullResetStructureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(DemoFullResetStructureTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void BaselineCaptureIsDemoPinnedProtectedAndSchemaAware()
    {
        var script = File.ReadAllText(Path.Combine(
            Root, "scripts", "Initialize-DemoFullReset.ps1"));

        Assert.Contains("if ($Database -cne 'SatiDemo')", script);
        Assert.Contains("if (-not $ReplaceBaseline)", script);
        Assert.Contains("EnvironmentName=N'Demo'", script);
        Assert.Contains("@LockMode=N'Exclusive'", script);
        Assert.Contains("demo_baseline", script);
        Assert.Contains("WITH EXECUTE AS OWNER", script);
        Assert.Contains("The Demo schema changed after baseline capture", script);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline", script);
        Assert.Contains("InstanceId=NEWID()", script);
        Assert.DoesNotContain("SatiProduction", script);
    }

    [Fact]
    public void ManualAndScheduledFunctionsHoldResetLockThroughRollingValidation()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("Sati.DemoRefresh", "ResetDemo", "run.ps1"),
                     Path.Combine("Sati.DemoRefresh", "RefreshCaseload", "run.ps1")
                 })
        {
            var script = File.ReadAllText(Path.Combine(Root, relative));
            var acquire = script.IndexOf("@LockMode=N'Exclusive'", StringComparison.Ordinal);
            var restore = script.IndexOf("SatiResetToCanonicalBaseline", StringComparison.Ordinal);
            var seed = script.IndexOf("& $seed", StringComparison.Ordinal);
            var release = script.IndexOf("sp_releaseapplock", StringComparison.Ordinal);

            Assert.True(acquire >= 0 && acquire < restore);
            Assert.True(restore < seed);
            Assert.True(seed < release);
        }

        var binding = File.ReadAllText(Path.Combine(
            Root, "Sati.DemoRefresh", "ResetDemo", "function.json"));
        Assert.Contains("\"authLevel\": \"function\"", binding);
    }

    [Fact]
    public void FunctionCredentialStaysAtTheApiBoundary()
    {
        var coordinator = File.ReadAllText(Path.Combine(
            Root, "Sati.Api", "Infrastructure", "DemoResetCoordinator.cs"));
        var publicSettings = Directory.GetFiles(Root, "appsettings*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        Assert.Contains("x-functions-key", coordinator);
        Assert.Contains("FunctionKey", coordinator);
        Assert.DoesNotContain("?code=", coordinator);
        Assert.DoesNotContain(publicSettings, contents => contents.Contains(
            "DemoReset__FunctionKey", StringComparison.Ordinal));
    }
}
