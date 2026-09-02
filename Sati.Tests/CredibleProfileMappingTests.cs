using Sati.Contracts.V1;
using System.Globalization;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The Credible label-to-Sati-field mapping.
///
/// <para>
/// The fixture below reproduces the structure of a real print view — the four-column
/// label/value rows, the non-breaking-space padding, empty cells, and the misspelled
/// <c>Consumer Demograpics</c> banner — with fabricated values. It is deliberately small enough
/// to read against the field-map table in CREDIBLE_IMPORT_DESIGN.md.
/// </para>
///
/// <para>
/// Most of these tests are about what the mapper refuses to do. A field it leaves blank costs a
/// reviewer thirty seconds; a field it fills with a plausible wrong value survives review
/// precisely because it looks right, and lands in a claim.
/// </para>
/// </summary>
public sealed class CredibleProfileMappingTests
{
    // ---- The happy path ----

    [Fact]
    public void ItMapsTheDemographicFieldsFromAWellFormedExport()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        Assert.Equal("CREDIBLE", Value(draft, CredibleFields.FirstName));
        Assert.Equal("TEST", Value(draft, CredibleFields.LastName));
        Assert.Equal("1990-01-02", Value(draft, CredibleFields.BirthDate));
        Assert.Equal("12345678A", Value(draft, CredibleFields.MaineCareId));
        Assert.Equal("21864", Value(draft, CredibleFields.CredibleClientId));
        Assert.Equal("1 Choice Hotels Circle", Value(draft, CredibleFields.BillingStreet));
        Assert.Equal("Alexander", Value(draft, CredibleFields.BillingCity));
        Assert.Equal("MD", Value(draft, CredibleFields.BillingState));
        Assert.Equal("20850", Value(draft, CredibleFields.BillingZip));
        Assert.Equal("3016529500", Value(draft, CredibleFields.PhoneNumber));
        Assert.Equal("Male", Value(draft, CredibleFields.Gender));
    }

    // The field the browser's PDF text layer silently filled with the SSN. Worth its own
    // assertion: this is the exact mis-pairing the HTML path exists to make impossible.
    [Fact]
    public void TheMaineCareIdComesFromTheMaineCareCellAndNotItsNeighbour()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        Assert.Equal("12345678A", Value(draft, CredibleFields.MaineCareId));
        Assert.Equal("000001800", Value(draft, CredibleFields.Ssn));
    }

    [Fact]
    public void CellPaddedWithNonBreakingSpacesIsTrimmed()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        // The fixture's first name is " CREDIBLE " as the markup writes it.
        Assert.Equal("CREDIBLE", Value(draft, CredibleFields.FirstName));
    }

    // ---- The two traps ----

    // "Consumer is Own Guardian? = YES" means the consumer has NO guardian. A straight copy
    // sets HasGuardian backwards on every single imported record, and nothing downstream would
    // look wrong enough to notice.
    [Theory]
    [InlineData("YES", "false")]
    [InlineData("NO", "true")]
    public void OwnGuardianIsInvertedIntoHasGuardian(string exported, string expected)
    {
        var document = Fixture(ownGuardian: exported);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        Assert.Equal(expected, Value(draft, CredibleFields.HasGuardian));
    }

    [Fact]
    public void TheDiagnosisCodeIsTakenOutOfTheCompositeCell()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        Assert.Equal("F84.0", Value(draft, CredibleFields.DiagnosisCode));
    }

    // Person.DiagnosisCode reaches an 837P claim. Prose in that column is a rejected claim,
    // so an unrecognizable diagnosis is reported rather than stored.
    [Theory]
    [InlineData("Autistic disorder")]
    [InlineData("(see attached) something")]
    [InlineData("Diagnosis pending")]
    public void ADiagnosisWithNoRecognizableCodeIsReportedRatherThanStored(string exported)
    {
        var document = Fixture(diagnosis: exported);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        var drafted = Field(draft, CredibleFields.DiagnosisCode);
        Assert.Equal(CredibleFieldStatus.Unreadable, drafted.Status);
        Assert.Null(drafted.Value);

        // The reviewer still sees what was there, or they cannot judge it.
        Assert.Equal(exported, drafted.RawValue);
    }

    [Fact]
    public void ABareDiagnosisCodeIsAccepted()
    {
        var draft = CredibleProfileMapping.Map(
            Fixture(diagnosis: "F84.0"), CredibleLayoutProfile.Default);

        Assert.Equal("F84.0", Value(draft, CredibleFields.DiagnosisCode));
    }

    // ---- Dates ----

    // The formats are listed explicitly rather than left to DateTime.Parse, so a workstation
    // set to a day-first culture cannot read 04/03/1990 as 3 April. A birth date wrong by
    // months looks entirely ordinary on screen.
    [Fact]
    public void DatesAreReadTheSameWayRegardlessOfTheMachinesCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.Equal("1990-04-03", CredibleProfileMapping.ParseDate("04/03/1990"));

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("1990-04-03", CredibleProfileMapping.ParseDate("04/03/1990"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("4/26/2021", "2021-04-26")]
    [InlineData("01/02/1990", "1990-01-02")]
    public void CredibleWritesDatesBothPaddedAndUnpadded(string exported, string expected) =>
        Assert.Equal(expected, CredibleProfileMapping.ParseDate(exported));

    [Theory]
    [InlineData("1876")]
    [InlineData("not a date")]
    [InlineData("13/45/1990")]
    public void AValueThatIsNotADateIsReportedRatherThanInvented(string exported)
    {
        var draft = CredibleProfileMapping.Map(
            Fixture(birthDate: exported), CredibleLayoutProfile.Default);

        Assert.Equal(CredibleFieldStatus.Unreadable, Field(draft, CredibleFields.BirthDate).Status);
    }

    // ---- Gender ----

    // Unknown is a real Sati answer meaning "not recorded". Falling back to it for any value
    // the mapper has not been taught would launder a mapping gap into a clinical fact.
    [Fact]
    public void AnUnrecognizedGenderIsReportedRatherThanCalledUnknown()
    {
        var draft = CredibleProfileMapping.Map(
            Fixture(gender: "Genderqueer"), CredibleLayoutProfile.Default);

        var drafted = Field(draft, CredibleFields.Gender);
        Assert.Equal(CredibleFieldStatus.Unreadable, drafted.Status);
        Assert.Null(drafted.Value);
    }

    [Theory]
    [InlineData("Male", "Male")]
    [InlineData("female", "Female")]
    [InlineData("Non-Binary", "NonBinary")]
    [InlineData("Unknown", "Unknown")]
    public void KnownGendersMapOntoSatisVocabulary(string exported, string expected) =>
        Assert.Equal(expected, CredibleProfileMapping.ParseGender(exported));

    // ---- Absence, in its three distinct forms ----

    [Fact]
    public void AnEmptyCellIsBlankRatherThanMissing()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        // The fixture carries Guardian Last Name as an empty cell.
        Assert.Equal(CredibleFieldStatus.Blank, Field(draft, CredibleFields.GuardianLastName).Status);
    }

    // "Hide Empty Profile Fields" drops the label row entirely, which is why the operator must
    // leave it off. Distinguishing this from Blank is what makes that detectable.
    [Fact]
    public void ADroppedLabelIsReportedAsMissingRatherThanBlank()
    {
        var document = new ClientExportDocument("21864", [
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField("First Name", "CREDIBLE")
                // Last Name's row is not here at all.
            ])
        ]);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        Assert.Equal(CredibleFieldStatus.LabelMissing, Field(draft, CredibleFields.LastName).Status);
        Assert.Equal(CredibleFieldStatus.Mapped, Field(draft, CredibleFields.FirstName).Status);
    }

    [Fact]
    public void AnUntickedSectionIsReportedOnceRatherThanPerField()
    {
        var document = new ClientExportDocument("21864", [
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField("First Name", "CREDIBLE")
            ])
        ]);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        Assert.Contains("Consumer Address", draft.MissingSections);
        Assert.Contains("Medical", draft.MissingSections);
        Assert.Equal(draft.MissingSections.Distinct().Count(), draft.MissingSections.Count);

        Assert.Equal(
            CredibleFieldStatus.SectionMissing,
            Field(draft, CredibleFields.BillingCity).Status);
    }

    // The single most important property of this mapper. A label the export does not carry must
    // produce nothing, never the cell that happened to sit next to where it would have been.
    [Fact]
    public void AMissingLabelNeverPicksUpANeighbouringValue()
    {
        var document = new ClientExportDocument("21864", [
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField("Saddleback ID", "12345678A"),
                new ClientExportField("SSN", "000001800")
                // MaineCare ID's row is absent, and its real value sits above and below.
            ])
        ]);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        var maineCare = Field(draft, CredibleFields.MaineCareId);
        Assert.Equal(CredibleFieldStatus.LabelMissing, maineCare.Status);
        Assert.Null(maineCare.Value);
        Assert.Null(maineCare.RawValue);
    }

    // ---- Provenance and drift ----

    [Fact]
    public void EveryDraftedFieldSaysWhichSectionAndLabelProducedIt()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        var gender = Field(draft, CredibleFields.Gender);
        Assert.Equal("Consumer Demograpics", gender.Section);
        Assert.Equal("Gender", gender.Label);
    }

    // Not an error — most of the export is deliberately ignored — but the signal that says a
    // Credible layout has moved on from the profile.
    [Fact]
    public void LabelsTheProfileDoesNotMapAreReportedForReview()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        Assert.Contains("CONSUMER INFO / Status", draft.UnmappedLabels);
        Assert.DoesNotContain("CONSUMER INFO / First Name", draft.UnmappedLabels);
    }

    // Credible repeats CONSUMER EPISODE INFO once per episode — 31 times in the real test
    // export. None are mapped today, but a profile that mapped a repeated section must not
    // silently take whichever copy came last.
    [Fact]
    public void ARepeatedSectionKeepsItsFirstOccurrence()
    {
        var document = new ClientExportDocument("21864", [
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField("First Name", "FIRST")
            ]),
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField("First Name", "SECOND")
            ])
        ]);

        var draft = CredibleProfileMapping.Map(document, CredibleLayoutProfile.Default);

        Assert.Equal("FIRST", Value(draft, CredibleFields.FirstName));
    }

    [Fact]
    public void TheCredibleClientIdIsCarriedOntoTheDraft()
    {
        var draft = CredibleProfileMapping.Map(Fixture(), CredibleLayoutProfile.Default);

        Assert.Equal("21864", draft.CredibleClientId);
    }

    // ---- Fixture ----

    private static string? Value(CredibleProfileDraft draft, string satiField) =>
        Field(draft, satiField).Value;

    private static CredibleFieldDraft Field(CredibleProfileDraft draft, string satiField) =>
        draft.Fields.Single(drafted => drafted.SatiField == satiField);

    /// <summary>
    /// A compact stand-in for a real print view. Values are Credible's own demo client, which is
    /// fabricated; the padding reproduces the markup, where labels and values are wrapped in
    /// non-breaking spaces.
    /// </summary>
    private static ClientExportDocument Fixture(
        string ownGuardian = "YES",
        string diagnosis = "(F84.0) Autistic disorder",
        string birthDate = "01/02/1990",
        string gender = "Male") =>
        new("21864", [
            new ClientExportSection("CONSUMER INFO", [
                new ClientExportField(" First Name", " CREDIBLE "),
                new ClientExportField(" Last Name", " TEST "),
                new ClientExportField(" Status", " ACTIVE "),
                new ClientExportField(" Saddleback ID", " "),
                new ClientExportField(" MaineCare ID", " 12345678A "),
                new ClientExportField(" SSN", " 000001800 "),
                new ClientExportField(" Consumer is Own Guardian?", $" {ownGuardian} "),
                new ClientExportField(" Consumer ID", " 21864 "),
                new ClientExportField(" DOB", $" {birthDate} ")
            ]),
            new ClientExportSection("Consumer Address", [
                new ClientExportField(" address1", " 1 Choice Hotels Circle "),
                new ClientExportField(" City", " Alexander "),
                new ClientExportField(" Zip", " 20850 "),
                new ClientExportField(" State", " MD ")
            ]),
            new ClientExportSection("Consumer Contact Info", [
                new ClientExportField(" Home Phone", " 3016529500 "),
                new ClientExportField(" Consumer Email", " demo@credibleinc.test ")
            ]),
            new ClientExportSection("Consumer Guardian #1", [
                new ClientExportField(" Guardian First Name", " Bob "),
                new ClientExportField(" Guardian Last Name", " ")
            ]),
            new ClientExportSection("Consumer Demograpics", [
                new ClientExportField(" Age", " 36 "),
                new ClientExportField(" Gender", $" {gender} ")
            ]),
            new ClientExportSection("Medical", [
                new ClientExportField(" Primary Diagnosis", $" {diagnosis} ")
            ])
        ]);
}
