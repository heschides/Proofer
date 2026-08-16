using Xunit;

namespace Sati.Tests;

/// <summary>
/// Serialises every test class that renders a PDF.
///
/// PDFsharp keeps process-global state behind a render: the font resolver, the
/// glyph typeface cache, and the embedded font SUBSETS with their randomly
/// generated six-letter tags. xUnit runs test classes in parallel by default, so
/// two classes rendering at once share and race that state.
///
/// The symptom was a flaky regeneration test — two renders of one unchanged
/// request came out different, but only sometimes, and never when the class ran
/// alone. It is not a clock-boundary problem: a probe that deliberately forced
/// the two renders into different wall-clock seconds produced identical output.
///
/// Related to <see cref="PdfFontResolverInitializer"/>, which fixes the ordering
/// half of the same shared-state problem.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PdfRenderingCollection
{
    public const string Name = "PDF rendering";
}
