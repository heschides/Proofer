using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Xunit;

namespace Sati.Tests;

[Collection(PdfRenderingCollection.Name)]
public sealed class DocumentTemplateTests
{
    [Fact]
    public void DefaultIsValidButUnknownOrMalformedTokensAreRejected()
    {
        Assert.Empty(DocumentTemplateRules.Validate(AnnualDocumentKind.PrivacyPractices,
            SatiDefaultDocumentTemplates.PrivacyPracticesBody));
        Assert.Contains("tokens", DocumentTemplateRules.Validate(
            AnnualDocumentKind.PrivacyPractices, "{{consumer.ssn}}").Keys);
        Assert.Contains("syntax", DocumentTemplateRules.Validate(
            AnnualDocumentKind.PrivacyPractices, "{{agency.name}").Keys);
    }

    [Fact]
    public void ResolutionPrefersOwnAgencyThenDefaultAndIgnoresOtherAgencies()
    {
        var now = DateTime.UtcNow;
        DocumentTemplateFact[] facts =
        [
            new(1, null, "PrivacyPractices", 4, now, null),
            new(2, 1, "PrivacyPractices", 1, now, null),
            new(3, 2, "PrivacyPractices", 99, now, null),
            new(4, 1, "PrivacyPractices", 2, now, now)
        ];
        Assert.Equal(2, DocumentTemplateResolution.Resolve(1, AnnualDocumentKind.PrivacyPractices, facts)!.Id);
        Assert.Equal(1, DocumentTemplateResolution.Resolve(3, AnnualDocumentKind.PrivacyPractices, facts)!.Id);
    }

    [Fact]
    public void ComposerSupportsBlocksAndReportsOnlyMissingTokenNames()
    {
        var result = new DocumentTemplatePdfComposer().Generate(AnnualDocumentKind.PrivacyPractices,
            "# Notice\n{{agency.name}}\n## Details\n- First item\n| Name | Phone |\n|---|---|\n| {{consumer.full_name}} | {{agency.phone}} |\n[[PAGE_BREAK]]\nEnd.",
            Context() with { AgencyPhone = null }, DateTime.UtcNow);
        using var pdf = PdfReader.Open(new MemoryStream(result.Pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        Assert.Equal(["agency.phone"], result.BlankFields);
    }

    [Fact]
    public async Task LocalPublishedTemplateCannotBeEditedInPlace()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using var db = fixture.Factory.CreateDbContext();
        var template = await db.DocumentTemplates.SingleAsync(t => t.Id == 1);
        db.Entry(template).Property(t => t.Body).CurrentValue = "Changed without publication";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LocalPrivacyGenerationCitesTheSeededDefault()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            var person = await setup.People.SingleAsync(candidate => candidate.Id == fixture.PersonOneId);
            person.EffectiveDate = DateTime.Today.AddMonths(-2);
            await setup.SaveChangesAsync();
        }
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var service = new DocumentTemplateService(fixture.Factory, session, new DocumentTemplatePdfComposer());
        var result = await service.GeneratePrivacyPracticesAsync(fixture.PersonOneId);
        Assert.NotEmpty(result.Pdf);
        await using var db = fixture.Factory.CreateDbContext();
        var artifact = await db.DocumentArtifacts.SingleAsync(a => a.PersonId == fixture.PersonOneId);
        Assert.Equal("SatiDefault", artifact.TemplateOwner);
        Assert.Equal(1, artifact.TemplateVersion);
    }

    internal static DocumentTemplateRenderContext Context() => new(
        "Example Support Services", "12 Main Street, Augusta, ME 04330", "207-555-0100",
        "Jordan Example", new DateTime(1987, 4, 12), new DateTime(2026, 9, 1), new DateTime(2027, 8, 31),
        "Case Manager", "CaseManager");
}
