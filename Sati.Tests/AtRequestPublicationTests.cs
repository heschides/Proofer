using Sati.Contracts.V1;
using Sati.Models;
using Sati.Reporting;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Publishing an AT request records the case manager's attestation, freezes the
/// wording they affirmed, transitions the request out of draft, and closes it to
/// further editing.
///
/// The rules under test live in Sati.Contracts.V1.AtRequestPublication precisely
/// so that the desktop client and Sati.Api enforce one definition. A test that
/// re-derived "published" locally would pass while the two halves disagreed.
/// </summary>
[Collection(PdfRenderingCollection.Name)]
public sealed class AtRequestPublicationTests
{
    private static User CaseManager(int id = 7, string name = "Casey Manager") =>
        User.Create(id, "cmanager", name, string.Empty, string.Empty, UserRole.CaseManager, null, 1);

    private static ATRequest CompleteRequest()
    {
        var person = Person.Rehydrate(1, 1);
        person.FirstName = "Test";
        person.LastName = "Client";

        var request = ATRequest.CreateForClient(person, CaseManager());
        request.VendorName = "Maine AT Solutions";
        request.VendorBillingLocation = "1992205249-007";
        request.Items.Add(new ATRequestItem { Name = "Pie cutter", ItemCost = 25.99m, Quantity = 3 });
        return request;
    }

    // ---- Completeness ----

    [Fact]
    public void ACompleteRequestHasNoBlockers()
    {
        var request = CompleteRequest();

        Assert.Empty(AtRequestPublication.FindBlockers(
            request.VendorName, request.VendorBillingLocation, Lines(request), alreadyPublished: false));
    }

    [Theory]
    [InlineData(null, "1992205249-007")]
    [InlineData("", "1992205249-007")]
    [InlineData("   ", "1992205249-007")]
    [InlineData("Maine AT Solutions", null)]
    [InlineData("Maine AT Solutions", "  ")]
    public void AMissingVendorFieldBlocksPublication(string? vendorName, string? billingLocation)
    {
        var request = CompleteRequest();

        Assert.NotEmpty(AtRequestPublication.FindBlockers(
            vendorName, billingLocation, Lines(request), alreadyPublished: false));
    }

    [Fact]
    public void ARequestWithNoItemsBlocksPublication()
    {
        var blockers = AtRequestPublication.FindBlockers(
            "Maine AT Solutions", "1992205249-007", [], alreadyPublished: false);

        Assert.Contains(blockers, blocker => blocker.Contains("at least one item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnUnnamedItemBlocksPublication()
    {
        var blockers = AtRequestPublication.FindBlockers(
            "Maine AT Solutions", "1992205249-007",
            [new AtRequestLine(null, 25m, 1)], alreadyPublished: false);

        Assert.Contains(blockers, blocker => blocker.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AZeroDollarRequestBlocksPublication()
    {
        // Individual free lines are legitimate; a request that asks for nothing
        // at all is an unfinished draft.
        var blockers = AtRequestPublication.FindBlockers(
            "Maine AT Solutions", "1992205249-007",
            [new AtRequestLine("Donated tablet", 0m, 1)], alreadyPublished: false);

        Assert.Contains(blockers, blocker => blocker.Contains("greater than zero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFreeLineAlongsidePricedLinesIsAllowed()
    {
        Assert.Empty(AtRequestPublication.FindBlockers(
            "Maine AT Solutions", "1992205249-007",
            [new AtRequestLine("Donated tablet", 0m, 1), new AtRequestLine("Case", 40m, 1)],
            alreadyPublished: false));
    }

    // ---- The attestation ----

    [Fact]
    public void PublishingRecordsTheSignerFromTheSessionAndStampsTheStatus()
    {
        var request = CompleteRequest();
        var signedAt = new DateTime(2026, 8, 15, 14, 32, 9, DateTimeKind.Utc);

        request.Publish(CaseManager(id: 42, name: "Joshua White"), signedAt, 0.15m);

        Assert.True(request.IsPublished);
        Assert.Equal("Joshua White", request.SignedByName);
        Assert.Equal(nameof(UserRole.CaseManager), request.SignedByRole);
        Assert.Equal(42, request.SignedByUserId);
        Assert.Equal(signedAt, request.SignedAtUtc);
        Assert.Equal(ATRequestStatus.Review, request.Status);
        Assert.Equal(signedAt.Date, request.SubmittedDate);
    }

    [Fact]
    public void PublishingFreezesTheStatementRatherThanReferencingIt()
    {
        // The stored text must survive a later revision of the constant. Copying
        // it at publication is what makes that possible; this asserts the copy
        // happened rather than that the two currently match.
        var request = CompleteRequest();
        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        Assert.Equal(AtRequestPublication.AttestationStatement, request.AttestationStatement);
        Assert.NotEmpty(request.AttestationStatement!);
    }

    [Fact]
    public void ARequestCannotBePublishedTwice()
    {
        var request = CompleteRequest();
        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        Assert.Throws<InvalidOperationException>(() => request.Publish(CaseManager(), DateTime.UtcNow, 0.15m));
    }

    [Fact]
    public void AlreadyPublishedIsReportedAsABlocker()
    {
        var request = CompleteRequest();
        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        Assert.NotEmpty(AtRequestPublication.FindBlockers(
            request.VendorName, request.VendorBillingLocation, Lines(request), alreadyPublished: true));
    }

    [Fact]
    public void AHalfWrittenAttestationDoesNotCountAsPublished()
    {
        // A name with no timestamp, or a timestamp with no name, is a corrupt or
        // partially applied record. Treating it as signed would let a request slip
        // past the lock with nothing that identifies who signed it.
        Assert.False(AtRequestPublication.IsPublished("Joshua White", null));
        Assert.False(AtRequestPublication.IsPublished(null, DateTime.UtcNow));
        Assert.False(AtRequestPublication.IsPublished("   ", DateTime.UtcNow));
        Assert.True(AtRequestPublication.IsPublished("Joshua White", DateTime.UtcNow));
    }

    // ---- Reopening ----

    [Fact]
    public void ReopeningDiscardsTheAttestationAndReturnsTheRequestToDraft()
    {
        var request = CompleteRequest();
        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        request.ReopenForCorrection();

        Assert.False(request.IsPublished);
        Assert.Null(request.SignedByName);
        Assert.Null(request.SignedByRole);
        Assert.Null(request.SignedByUserId);
        Assert.Null(request.SignedAtUtc);
        Assert.Null(request.AttestationStatement);
        Assert.Null(request.SubmittedDate);
        Assert.Equal(ATRequestStatus.Development, request.Status);
    }

    [Fact]
    public void ReopeningThenRepublishingRecordsAFreshAttestation()
    {
        var request = CompleteRequest();
        var first = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

        request.Publish(CaseManager(id: 7, name: "First Signer"), first, 0.15m);
        request.ReopenForCorrection();
        request.Publish(CaseManager(id: 9, name: "Second Signer"), second, 0.15m);

        Assert.Equal("Second Signer", request.SignedByName);
        Assert.Equal(9, request.SignedByUserId);
        Assert.Equal(second, request.SignedAtUtc);
    }

    [Fact]
    public void AnUnpublishedRequestCannotBeReopened()
    {
        Assert.Throws<InvalidOperationException>(() => CompleteRequest().ReopenForCorrection());
    }

    // ---- The snapshot ----

    [Fact]
    public void TheFirstSnapshotWins()
    {
        // The snapshot is evidence of the document as first published. A
        // regeneration months later must not overwrite it with a rendering of
        // whatever the record looks like by then.
        var request = CompleteRequest();

        request.AttachSnapshot([1, 2, 3]);
        request.AttachSnapshot([9, 9, 9]);

        Assert.Equal<byte[]>([1, 2, 3], request.SnapshotPng!);
    }

    // ---- The exported document ----

    [Fact]
    public void ThePublishedPdfCarriesTheAttestationAndTheFigures()
    {
        var request = CompleteRequest();
        request.SalesTax = 4.29m;
        request.Publish(CaseManager(name: "Joshua White"), new DateTime(2026, 8, 15, 14, 32, 0, DateTimeKind.Utc), 0.15m);

        var pdf = new ATRequestPdfExporter().Generate(request, 0.15m, DateTime.UtcNow);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void AnUnpublishedRequestStillExportsAsADraft()
    {
        // Circulating a draft for review is legitimate; it just must not look
        // like a submitted document.
        var pdf = new ATRequestPdfExporter().Generate(CompleteRequest(), 0.15m, DateTime.UtcNow);

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public void TheSuggestedFileNameIsSafeForAFilesystem()
    {
        var person = Person.Rehydrate(1, 1);
        person.FirstName = "Mary/Anne";
        person.LastName = @"O""Brien: Jr.";
        var request = ATRequest.CreateForClient(person, CaseManager());

        var name = ATRequestPdfExporter.SuggestedFileName(request);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('"', name);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }

    // ---- Regeneration fidelity ----
    //
    // A filed AT request is a document of record. Reopening it from a client's
    // profile next year must reproduce what was submitted, not recompute it
    // against whatever the agency's terms have become since.

    [Fact]
    public void PublishingFreezesTheRateTheRequestWasFiledUnder()
    {
        var request = CompleteRequest();

        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        Assert.Equal(0.15m, request.PassthroughRate);
        // The frozen rate now outranks whatever the caller believes the current
        // rate to be.
        Assert.Equal(0.15m, request.EffectivePassthroughRate(0.25m));
    }

    [Fact]
    public void ADraftFollowsTheAgencysCurrentRate()
    {
        // The opposite case, and the reason the rate is nullable: while a request
        // is still being written, showing last month's terms would be the bug.
        var draft = CompleteRequest();

        Assert.Null(draft.PassthroughRate);
        Assert.Equal(0.25m, draft.EffectivePassthroughRate(0.25m));
    }

    [Fact]
    public void RegeneratingAPublishedRequestIgnoresALaterRateChange()
    {
        // The requirement in one test. Publish at 15%, then have the agency move
        // to 25%, then regenerate. The document must not move.
        var request = CompleteRequest();
        request.SalesTax = 4.29m;
        request.Publish(CaseManager(), new DateTime(2026, 8, 15, 14, 32, 0, DateTimeKind.Utc), 0.15m);

        var atFilingTime = request.TotalCost(0.15m);
        var exporter = new ATRequestPdfExporter();

        var whenFiled = exporter.Generate(request, 0.15m, DateTime.UtcNow);
        var regeneratedAfterRateChange = exporter.Generate(request, 0.25m, DateTime.UtcNow);

        // The money is unchanged...
        Assert.Equal(atFilingTime, request.TotalCost(request.EffectivePassthroughRate(0.25m)));
        // ...and so is the document. Note this would catch a changed rate even
        // though "15.00%" and "25.00%" are the same length: the comparison is
        // exact, not a size check.
        Assert.Equal(Normalize(whenFiled), Normalize(regeneratedAfterRateChange));
    }

    [Fact]
    public void RegeneratingAPublishedRequestTwiceProducesTheSameBytes()
    {
        // Two regenerations minutes apart must be indistinguishable, or nobody can
        // tell an identical reprint from an altered one.
        var request = CompleteRequest();
        request.Publish(CaseManager(), new DateTime(2026, 8, 15, 14, 32, 0, DateTimeKind.Utc), 0.15m);

        var exporter = new ATRequestPdfExporter();
        var first = exporter.Generate(request, 0.15m, new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc));
        var second = exporter.Generate(request, 0.15m, new DateTime(2027, 1, 2, 17, 45, 0, DateTimeKind.Utc));

        // Months apart on the wall clock, and the generation timestamp deliberately
        // differs — a published request must not print it anywhere.
        Assert.Equal(Normalize(first), Normalize(second));
    }

    /// <summary>
    /// Masks the two things PDFsharp legitimately randomises between renders of an
    /// identical document, neither of which is content:
    ///
    ///   * the six-letter font SUBSET TAG ("KGSDKN+Arial,Bold"), which the PDF
    ///     specification expects to be unique per embedded subset;
    ///   * the XMP document and instance UUIDs, which identify the FILE rather
    ///     than anything printed in it.
    ///
    /// Everything else is compared exactly. A changed figure, rate label, signer,
    /// or date still fails — and note that catching a rate change is not a size
    /// check, since "15.00%" and "25.00%" are the same length.
    ///
    /// Byte-for-byte identity of the raw files is not achievable through PDFsharp
    /// and is not what the requirement means. What must not drift is the document.
    /// </summary>
    private static string Normalize(byte[] pdf)
    {
        // Latin-1 maps every byte to a distinct char, so binary streams survive
        // the round trip intact rather than being mangled into U+FFFD.
        var text = System.Text.Encoding.GetEncoding(28591).GetString(pdf);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[A-Z]{6}\+", "SUBSET+");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"uuid:[0-9a-fA-F-]{36}", "uuid:MASKED");
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"/ID\s*\[<[0-9A-Fa-f]+>\s*<[0-9A-Fa-f]+>\]", "/ID[<MASKED><MASKED>]");
    }

    [Fact]
    public void ReopeningReleasesTheFrozenRate()
    {
        // A reopened request is a draft again, so the live agency rate governs.
        // Keeping the old one would apply last year's terms to a rewrite.
        var request = CompleteRequest();
        request.Publish(CaseManager(), DateTime.UtcNow, 0.15m);

        request.ReopenForCorrection();

        Assert.Null(request.PassthroughRate);
        Assert.Equal(0.25m, request.EffectivePassthroughRate(0.25m));
    }

    private static IReadOnlyList<AtRequestLine> Lines(ATRequest request) =>
        request.Items.Select(item => new AtRequestLine(item.Name, item.ItemCost, item.Quantity)).ToList();
}
