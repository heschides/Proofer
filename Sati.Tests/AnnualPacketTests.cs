using System.IO;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Forms;
using Xunit;

namespace Sati.Tests;

[Collection(PdfRenderingCollection.Name)]
public sealed class AnnualPacketTests
{
    internal static AnnualPacketComposer Composer() => new(new AgencyReleasePdfGenerator(), new DhhsFormFiller(),
        new DocumentTemplatePdfComposer(), new SafetyPlanPdfGenerator());
    internal static PacketRenderInput Input() => new(new(1, "Jordan Example", new DateTime(1987, 4, 12), null,
        "Example Support Services", "12 Main Street, Augusta, ME 04330", "207-555-0100", "Case Manager", "CaseManager"),
        new DateTime(2026, 9, 1), new DateTime(2027, 8, 31), new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
        12, [], null, [], false, "Example Primary Care", "10 Example Street, Augusta, ME 04330", "207-555-0101");

    [Fact]
    public void RecordsRequestRequiresAttestedMedicalReleaseAndLinkedRecipient()
    {
        var composer = Composer(); var input = Input();
        Assert.DoesNotContain(composer.Render(input).Documents, x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        Assert.DoesNotContain(composer.Render(input with { MedicalReleaseAttested = true, ProviderName = null }).Documents,
            x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        var documents = composer.Render(input with { MedicalReleaseAttested = true }).Documents;
        Assert.Contains(documents, x => x.Kind == AnnualDocumentKind.MedicalRecordsRequest);
        Assert.All(documents.Where(x => x.Kind is AnnualDocumentKind.ReleaseAgency or AnnualDocumentKind.ReleaseMedical or
            AnnualDocumentKind.ReleaseDhhs or AnnualDocumentKind.SafetyPlan), x => Assert.Equal(DocumentArtifactOrigin.Draft, x.Origin));
        // Opt-in, synthetic-only preview for repeatable visual QA. Normal tests write no output files.
        if (Environment.GetEnvironmentVariable("SATI_DOCUMENT_QA_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var document in documents) File.WriteAllBytes(Path.Combine(directory, document.FileName), document.Pdf);
        }
    }

    [Fact]
    public void LeapYearCycleUsesOriginalEnrollmentAnniversaryAndInclusiveWindow()
    {
        var effective = new DateTime(2024, 2, 29);
        Assert.Equal(new DateTime(2023, 2, 28), AnnualDocumentCycle.CurrentStart(effective, new DateTime(2024, 2, 28)));
        var cycle = new DateTime(2027, 2, 28);
        var opens = cycle.AddDays(-30);
        Assert.False(AnnualPacketWindow.ForCycle(effective, cycle, opens.AddDays(-1), 30).IsOpen);
        Assert.True(AnnualPacketWindow.ForCycle(effective, cycle, opens, 30).IsOpen);
        Assert.Equal(new DateTime(2028, 2, 28), AnnualPacketWindow.ForCycle(effective, cycle, opens, 30).EndsOn);
        Assert.Equal(new DateTime(2028, 2, 28), AnnualDocumentCycle.EndInclusive(effective, cycle));
        Assert.Equal(cycle, AnnualPacketWindow.SuggestedCycle(effective, opens, 30));
        Assert.Throws<ArgumentException>(() => AnnualPacketWindow.ForCycle(effective, cycle.AddDays(1), opens, 30));
    }

    [Fact]
    public void RecipientInheritsOrganizationContactButPreservesPractitionerName()
    {
        var recipient = RecordsRecipient.Resolve(2, [new(1, null, "Practice", "Address", "Phone"), new(2, 1, "Practitioner", null, "")]);
        Assert.Equal("Practitioner", recipient!.Name); Assert.Equal("Address", recipient.Address); Assert.Equal("Phone", recipient.Phone);
        Assert.Null(RecordsRecipient.Resolve(3, [new(1, null, "Practice", "Address", "Phone")]));
        Assert.Throws<InvalidOperationException>(() => RecordsRecipient.Resolve(1, [new(1, 2, "A", null, null), new(2, 1, "B", null, null)]));
    }

    [Fact]
    public void ReceiptRequiresActualDateOrGoodFaithEffortAndHashChecksBytes()
    {
        var today = DateTime.Today;
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, null, " "), today, today));
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, today.AddDays(-1), null), today, today));
        Assert.NotNull(DocumentAcknowledgmentRules.Validate(new(1, today.AddDays(1), null), today, today));
        Assert.Null(DocumentAcknowledgmentRules.Validate(new(1, null, "Delivery attempted; follow up arranged."), today, today));
        var original = DocumentVerification.FromBytes(1, [1, 2, 3]);
        Assert.True(DocumentVerification.Matches(original.Sha256, 3, original));
        Assert.False(DocumentVerification.Matches(original.Sha256, 3, DocumentVerification.FromBytes(1, [1, 2, 4])));
        Assert.False(DocumentVerification.Matches(null, null, original));
    }

    [Fact]
    public async Task LocalPacketAndReceiptUsePersistedArtifactsAndAudit()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var cycle = DateTime.Today.AddMonths(-2);
        await using (var setup = fixture.Factory.CreateDbContext())
        {
            (await setup.People.SingleAsync(x => x.Id == fixture.PersonOneId)).EffectiveDate = cycle;
            await setup.SaveChangesAsync();
        }
        var session = new SessionService(); session.SetUser(fixture.CaseManagerOne);
        var service = new AnnualDocumentService(fixture.Factory, session, Composer());
        var packet = await service.SavePacketAsync(fixture.PersonOneId, cycle);
        Assert.NotEmpty(packet.Pdf);
        var status = await service.GetStatusAsync(fixture.PersonOneId, cycle);
        var notice = status.Artifacts.Single(x => x.Kind == "PrivacyPractices");
        await service.AcknowledgeAsync(fixture.PersonOneId, new(notice.Id, DateTime.Today, null));
        Assert.Contains(notice.Id, (await service.GetStatusAsync(fixture.PersonOneId, cycle)).AcknowledgedArtifactIds);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.Contains(await db.AuditEvents.ToListAsync(), x => x.Action == "annual-packet.saved");
        var receipt = await db.DocumentAcknowledgments.SingleAsync();
        receipt.GoodFaithEffortReason = "Illegal edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
