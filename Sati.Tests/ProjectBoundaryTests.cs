using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void DesktopProjectExcludesStandaloneToolProjects()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "Sati.csproj");
        var project = XDocument.Load(projectPath);

        AssertRemove(project, "Compile", @"tools\**\*.cs");
        AssertRemove(project, "EmbeddedResource", @"tools\**\*");
        AssertRemove(project, "None", @"tools\**\*");
    }

    private static void AssertRemove(XDocument project, string itemName, string expectedPattern)
    {
        var patterns = project
            .Descendants(itemName)
            .Select(item => (string?)item.Attribute("Remove"))
            .Where(pattern => pattern is not null);

        Assert.Contains(expectedPattern, patterns);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
