using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
using Sati.ViewModels.Children;
using System.IO;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The single-consumer import review panel.
///
/// <para>
/// The property that matters most here is what this screen does <b>not</b> do: it never saves.
/// It reads, maps, shows, and hands accepted values to the entry form, which submits through the
/// same path a typed consumer does. Everything below is either that property or the reviewer's
/// ability to see what they are accepting.
/// </para>
/// </summary>
public sealed class ConsumerImportViewModelTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    // ---- Reading and mapping ----

    [Fact]
    public async Task ItListsEveryMappedFieldWithItsValueAndSource()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup()));

        var firstName = Field(model, CredibleFields.FirstName);
        Assert.Equal("CREDIBLE", firstName.Value);
        Assert.Equal("CONSUMER INFO / First Name", firstName.Source);
        Assert.Equal("First name", firstName.DisplayName);
    }

    // A field-by-field acceptance step means nothing if the reviewer cannot see what the export
    // actually said. F84.0 is only trustworthy alongside "(F84.0) Autistic disorder".
    [Fact]
    public async Task AConvertedValueShowsWhatTheExportHeld()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup()));

        var diagnosis = Field(model, CredibleFields.DiagnosisCode);
        Assert.Equal("F84.0", diagnosis.Value);
        Assert.Equal("(F84.0) Autistic disorder", diagnosis.RawValue);
        Assert.True(diagnosis.ShowsRawValue);
    }

    [Fact]
    public async Task AValueThatNeededNoConversionDoesNotShowARawValue()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup()));

        Assert.False(Field(model, CredibleFields.FirstName).ShowsRawValue);
    }

    // Fields that were not found are listed too. Knowing what did NOT come across is the point
    // of showing them — a short list would read as a complete import.
    [Fact]
    public async Task FieldsThatCouldNotBeBroughtAcrossAreListedAndCannotBeAccepted()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup(diagnosis: "Diagnosis pending")));

        var diagnosis = Field(model, CredibleFields.DiagnosisCode);
        Assert.False(diagnosis.CanAccept);
        Assert.False(diagnosis.IsAccepted);
        Assert.True(diagnosis.IsProblem);
        Assert.Equal("Could not be read", diagnosis.StatusText);
    }

    [Fact]
    public async Task AMissingSectionIsCalledOutInTheSummary()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(MinimalMarkup()));

        Assert.Contains("Sections not in the export", model.StatusMessage);
        Assert.Contains("print options", model.StatusMessage);
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("%PDF-1.7\n", "printing to PDF")]
    public async Task ARefusedFileExplainsTheFixAndOpensNothing(string content, string expected)
    {
        var model = Build();

        await model.LoadAsync(WriteExport(content));

        Assert.True(model.HasRefusal);
        Assert.Contains(expected, model.RefusalMessage);
        Assert.False(model.IsOpen);
        Assert.Empty(model.Fields);
    }

    [Fact]
    public async Task LoadingAGoodExportAfterARefusalClearsTheRefusal()
    {
        var model = Build();
        await model.LoadAsync(WriteExport("%PDF-1.7\n"));
        Assert.True(model.HasRefusal);

        await model.LoadAsync(WriteExport(Markup()));

        Assert.False(model.HasRefusal);
        Assert.True(model.IsOpen);
    }

    // ---- Acceptance ----

    [Fact]
    public async Task EveryFoundFieldStartsAcceptedAndTheRestDoNot()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup(diagnosis: "Diagnosis pending")));

        Assert.True(Field(model, CredibleFields.FirstName).IsAccepted);
        Assert.False(Field(model, CredibleFields.DiagnosisCode).IsAccepted);
    }

    [Fact]
    public async Task ClearingAndAcceptingMovesTheCountAndTheApplyButton()
    {
        var model = Build();
        await model.LoadAsync(WriteExport(Markup()));

        model.ClearAllCommand.Execute(null);
        Assert.Equal(0, model.AcceptedCount);
        Assert.False(model.CanApply);

        model.AcceptAllCommand.Execute(null);
        Assert.True(model.AcceptedCount > 0);
        Assert.True(model.CanApply);
    }

    [Fact]
    public async Task OnlyAcceptedFieldsAreHandedOver()
    {
        var model = Build();
        AcceptedImportDraft? handed = null;
        model.DraftAccepted += draft => handed = draft;
        await model.LoadAsync(WriteExport(Markup()));

        model.ClearAllCommand.Execute(null);
        Field(model, CredibleFields.FirstName).IsAccepted = true;
        model.ApplyCommand.Execute(null);

        Assert.NotNull(handed);
        Assert.Equal("CREDIBLE", handed.Values[CredibleFields.FirstName]);
        Assert.False(handed.Values.ContainsKey(CredibleFields.LastName));
    }

    // Nothing writes an SSN yet, so the panel must not imply that accepting one does anything.
    // Showing the number ticked and then discarding it is worse than not offering it: it tells a
    // case manager the number was captured when nothing was written.
    [Fact]
    public async Task TheSsnIsShownButCannotBeAccepted()
    {
        var model = Build();

        await model.LoadAsync(WriteExport(Markup()));

        var ssn = Field(model, CredibleFields.Ssn);
        Assert.Equal("000001800", ssn.Value);
        Assert.False(ssn.CanAccept);
        Assert.False(ssn.IsAccepted);
        Assert.Contains("SSN panel", ssn.StatusText);
    }

    // Even Accept All must not pick it up, and nothing must reach the form that fills a
    // demographic save.
    [Fact]
    public async Task TheSsnNeverReachesTheAcceptedValuesEvenWhenAcceptingEverything()
    {
        var model = Build();
        AcceptedImportDraft? handed = null;
        model.DraftAccepted += draft => handed = draft;
        await model.LoadAsync(WriteExport(Markup()));

        model.AcceptAllCommand.Execute(null);
        model.ApplyCommand.Execute(null);

        Assert.NotNull(handed);
        Assert.False(handed.Values.ContainsKey(CredibleFields.Ssn));
        Assert.DoesNotContain("000001800", string.Join("|", handed.Values.Values));
    }

    [Fact]
    public async Task TheCredibleClientIdIsCarriedOnTheAcceptedDraft()
    {
        var model = Build();
        AcceptedImportDraft? handed = null;
        model.DraftAccepted += draft => handed = draft;
        await model.LoadAsync(WriteExport(Markup()));

        model.ApplyCommand.Execute(null);

        Assert.Equal("21864", handed!.CredibleClientId);
    }

    [Fact]
    public async Task CancellingHandsOverNothingAndClearsTheList()
    {
        var model = Build();
        var handedOver = false;
        model.DraftAccepted += _ => handedOver = true;
        await model.LoadAsync(WriteExport(Markup()));

        model.CancelCommand.Execute(null);

        Assert.False(handedOver);
        Assert.False(model.IsOpen);
        Assert.Empty(model.Fields);
    }

    // ---- Helpers ----

    private static ConsumerImportViewModel Build() =>
        new(new CredibleExportReader(), new StubPicker());

    private static ImportFieldViewModel Field(ConsumerImportViewModel model, string satiField) =>
        model.Fields.Single(row => row.SatiField == satiField);

    private string WriteExport(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"credible-import-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed class StubPicker : IExportFilePicker
    {
        public string? PickExportFile() => null;
        public string? PickExportFolder() => null;
    }

    private static string MinimalMarkup() =>
        """
        <html><body><form action="./client_printview.aspx?client_id=21864"><table>
          <tr><td class="shc" colspan="4"><b>CONSUMER INFO</b></td></tr>
          <tr><td class="lc"><b>First Name</b></td><td class="vc">CREDIBLE</td></tr>
        </table></form></body></html>
        """;

    private static string Markup(string diagnosis = "(F84.0) Autistic disorder") =>
        $$"""
        <html><body><form action="./client_printview.aspx?client_id=21864"><table>
          <tr><td class="shc" colspan="4"><b>CONSUMER INFO</b></td></tr>
          <tr>
            <td class="lc"><b>First Name</b></td><td class="vc">CREDIBLE</td>
            <td class="lc"><b>Last Name</b></td><td class="vc">TEST</td>
          </tr>
          <tr>
            <td class="lc"><b>MaineCare ID</b></td><td class="vc">12345678A</td>
            <td class="lc"><b>DOB</b></td><td class="vc">01/02/1990</td>
          </tr>
          <tr>
            <td class="lc"><b>SSN</b></td><td class="vc">000001800</td>
            <td class="lc"><b>Consumer is Own Guardian?</b></td><td class="vc">YES</td>
          </tr>
          <tr><td class="lc"><b>Consumer ID</b></td><td class="vc">21864</td></tr>
          <tr><td class="hc" colspan="4"><b>Consumer Address</b></td></tr>
          <tr>
            <td class="lc"><b>address1</b></td><td class="vc">1 Choice Hotels Circle</td>
            <td class="lc"><b>City</b></td><td class="vc">Alexander</td>
          </tr>
          <tr>
            <td class="lc"><b>State</b></td><td class="vc">MD</td>
            <td class="lc"><b>Zip</b></td><td class="vc">20850</td>
          </tr>
          <tr><td class="hc" colspan="4"><b>Consumer Contact Info</b></td></tr>
          <tr>
            <td class="lc"><b>Home Phone</b></td><td class="vc">3016529500</td>
            <td class="lc"><b>Consumer Email</b></td><td class="vc">demo@credibleinc.test</td>
          </tr>
          <tr><td class="hc" colspan="4"><b>Consumer Guardian #1</b></td></tr>
          <tr>
            <td class="lc"><b>Guardian First Name</b></td><td class="vc">Bob</td>
            <td class="lc"><b>Guardian Last Name</b></td><td class="vc">Jones</td>
          </tr>
          <tr><td class="hc" colspan="4"><b>Consumer Demograpics</b></td></tr>
          <tr><td class="lc"><b>Gender</b></td><td class="vc">Male</td></tr>
          <tr><td class="hc" colspan="4"><b>Medical</b></td></tr>
          <tr><td class="lc2"><b>Primary Diagnosis</b></td><td class="vc2">{{diagnosis}}</td></tr>
        </table></form></body></html>
        """;
}
