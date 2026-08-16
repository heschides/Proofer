using System.Runtime.CompilerServices;
using PdfSharp.Fonts;

namespace Sati.Tests;

/// <summary>
/// Points PdfSharp at the Windows font collection before any test runs.
///
/// This is a module initializer rather than a line inside each PDF test because
/// MigraDoc resolves its predefined fonts ONCE, on first render, and caches the
/// outcome. A test that rendered before the resolver was configured left the
/// cache in a failed state and took every later PDF test down with it — the
/// symptom was an unrelated exporter failing on "Courier New cannot be resolved
/// for predefined error font", depending on execution order.
///
/// App.xaml.cs and Sati.Api/Program.cs do the same thing at their own startup.
/// </summary>
internal static class PdfFontResolverInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;
}
