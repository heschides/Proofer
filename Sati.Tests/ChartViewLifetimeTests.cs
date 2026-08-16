using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// OxyPlot permits a PlotModel to be attached to exactly one PlotView and throws
/// InvalidOperationException on a second attach. Chart view models are singletons
/// while their views are built by DataTemplates, so every navigation creates a new
/// PlotView bound to the same PlotModel. Each view must therefore release the model
/// when it unloads.
///
/// StatisticsView shipped without that release and crashed the dispatcher on a return
/// visit to the Statistics screen. This guards the whole class of defect rather than
/// that one view, so a fourth chart cannot reintroduce it silently.
/// </summary>
public sealed class ChartViewLifetimeTests
{
    // A single <oxy:PlotView ... /> element.
    private static readonly Regex PlotViewElement = new(
        @"<oxy:PlotView\b(?<body>(?:(?!/>).)*?)/>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ModelBinding = new(
        @"Model\s*=\s*""\{\s*Binding\b",
        RegexOptions.Compiled);

    private static readonly Regex NameAttribute = new(
        @"\b(?:x:)?Name\s*=\s*""(?<name>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryBoundPlotViewReleasesItsModelOnUnload()
    {
        var viewsDirectory = Path.Combine(RepositoryRoot(), "Views");
        Assert.True(Directory.Exists(viewsDirectory), $"Views directory not found at {viewsDirectory}.");

        var offenders = new List<string>();
        var boundPlotViews = 0;

        foreach (var xamlPath in Directory.EnumerateFiles(viewsDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var xaml = File.ReadAllText(xamlPath);
            var view = Path.GetFileName(xamlPath);

            foreach (Match element in PlotViewElement.Matches(xaml))
            {
                var body = element.Groups["body"].Value;

                // Only a PlotView whose Model comes from the view model can be shared
                // between view instances; a locally constructed model cannot.
                if (!ModelBinding.IsMatch(body))
                    continue;

                boundPlotViews++;

                var name = NameAttribute.Match(body).Groups["name"].Value;
                if (string.IsNullOrEmpty(name))
                {
                    offenders.Add($"{view}: a PlotView binds Model but has no Name, so it cannot be detached.");
                    continue;
                }

                var codeBehindPath = xamlPath + ".cs";
                if (!File.Exists(codeBehindPath))
                {
                    offenders.Add($"{view}: binds Model on '{name}' but has no code-behind to detach it.");
                    continue;
                }

                var codeBehind = File.ReadAllText(codeBehindPath);
                var detaches =
                    codeBehind.Contains("Unloaded", StringComparison.Ordinal) &&
                    Regex.IsMatch(codeBehind, $@"{Regex.Escape(name)}\s*\.\s*Model\s*=\s*null");

                if (!detaches)
                {
                    offenders.Add(
                        $"{view}: '{name}' binds a shared PlotModel but never runs " +
                        $"'{name}.Model = null' on Unloaded.");
                }
            }
        }

        Assert.True(boundPlotViews > 0, "No bound PlotView was found — the detection pattern has gone stale.");
        Assert.True(
            offenders.Count == 0,
            "Every PlotView bound to a view model's PlotModel must release it on Unloaded, or the next " +
            "navigation throws InvalidOperationException inside OxyPlot:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string RepositoryRoot([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
