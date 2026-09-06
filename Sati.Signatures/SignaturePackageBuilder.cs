using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Signatures;

/// <summary>Produces a derived copy. The byte-for-byte original remains separately retained.</summary>
public sealed class SignaturePackageBuilder
{
    public byte[] Build(byte[] original, FrozenSignatureDocument frozen, SignatureRequest request,
        SignatureCompletion completion, SignatureSession session, SignatureConsent consent,
        IReadOnlyList<SignatureEvent> events)
    {
        if (original.LongLength != frozen.ByteCount || SignatureSecrets.Hash(original) != frozen.ContentSha256 ||
            original.Length is 0 or > SignatureRules.MaximumPdfBytes)
            throw Integrity();
        if (request.State != "Signed" || completion.RequestId != request.Id || completion.AgencyId != request.AgencyId ||
            frozen.Id != request.FrozenDocumentId || frozen.Id != completion.FrozenDocumentId ||
            frozen.AgencyId != request.AgencyId || frozen.PersonId != request.PersonId ||
            session.Id != completion.SessionId || session.RequestId != request.Id || session.AgencyId != request.AgencyId || session.Purpose != "Signing" ||
            consent.Id != completion.ConsentId || consent.SessionId != session.Id || consent.RequestId != request.Id || consent.AgencyId != request.AgencyId ||
            consent.DisclosureVersion != request.DisclosureVersion || consent.DisclosureText != request.DisclosureText ||
            completion.IntentText != request.IntentText || !SignatureRules.NamesMatch(request.SignerName, completion.TypedSignerName) ||
            request.CompletedAtUtc != completion.SignedAtUtc || session.DocumentReleasedAtUtc is null || session.AccessAcknowledgedAtUtc is null ||
            session.IssuedAtUtc > session.DocumentReleasedAtUtc || session.DocumentReleasedAtUtc > consent.AcceptedAtUtc ||
            session.AccessAcknowledgedAtUtc > completion.SignedAtUtc || consent.AcceptedAtUtc > completion.SignedAtUtc ||
            completion.SignedAtUtc >= session.ExpiresAtUtc || completion.SignedAtUtc >= request.ExpiresAtUtc)
            throw Integrity();
        string[] episodeKinds = ["Authenticated", "DocumentReleased", "ElectronicConsent", "Signed"];
        if (events.Count > 10 || events.Count(x => x.Kind is "PinRejected" or "PinLocked") > 5 ||
            events.Count(x => x.Kind == "Issued") != 1 ||
            events.Any(x => x.Kind == "Issued" && (x.ActorKind != "Staff" || x.ActorUserId != request.IssuedByUserId)) ||
            episodeKinds.Any(kind => events.Count(x => x.Kind == kind) != 1) ||
            events.Any(x => episodeKinds.Contains(x.Kind) && (x.SessionId != session.Id || x.ActorKind != "Signer")) ||
            events.Any(x => x.AgencyId != request.AgencyId || x.RequestId != request.Id ||
                (x.Kind != "Issued" && x.Kind is not ("PinRejected" or "PinLocked") && !episodeKinds.Contains(x.Kind))))
            throw Integrity();
        var signed = events.Single(x => x.Kind == "Signed");
        if (signed.OccurredAtUtc != completion.SignedAtUtc ||
            events.Any(x => x.Sequence > signed.Sequence) ||
            episodeKinds.Where(kind => kind != "Signed").Any(kind => events.Single(x => x.Kind == kind).Sequence >= signed.Sequence))
            throw Integrity();

        var document = new Document();
        document.Info.Title = "Electronic signing evidence - synthetic testing";
        document.Info.Author = "Sati";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(7);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromInch(0.7);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromInch(0.75);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText($"Synthetic signing evidence | Request {request.Id} | Evidence page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        Heading(section, "Electronic signing evidence");
        section.AddParagraph("SYNTHETIC TESTING ONLY - this feature has not been approved for real client signatures.");
        section.AddParagraph("This record describes the electronic actions recorded by Sati. It does not certify legal authority or legal acceptance, and this PDF has no cryptographic digital-signature seal. The preceding pages are a derived copy of the retained original. The original file is preserved separately without changes.");
        Fact(section, "Signer’s typed name", completion.TypedSignerName);
        Fact(section, "Recorded signer role", Capacity(request.SignerCapacity));
        Fact(section, "Completed", Utc(completion.SignedAtUtc));
        Fact(section, "Record references", $"Agency {request.AgencyId}; person {request.PersonId}; request {request.Id}; source document {frozen.DocumentArtifactId}; frozen document {frozen.Id}; completion {completion.Id}.");
        Fact(section, "Original file size", $"{frozen.ByteCount.ToString(CultureInfo.InvariantCulture)} bytes");
        Fact(section, "Original file SHA-256", frozen.ContentSha256);
        Fact(section, "Source preserved", Utc(frozen.StoredAtUtc));
        Fact(section, "Request issued", Utc(request.IssuedAtUtc));
        Fact(section, "Request expiration", Utc(request.ExpiresAtUtc));

        Heading(section, "Authentication and document review");
        section.AddParagraph("The server accepted the request-specific signing code before creating this signing session. The code, link token and session token are excluded from this record. Possession of those credentials alone does not independently establish the signer’s identity or authority.");
        Fact(section, "Signing session reference", session.Id.ToString(CultureInfo.InvariantCulture));
        Fact(section, "Signing session began", Utc(session.IssuedAtUtc));
        Fact(section, "Original document released to the session", Utc(session.DocumentReleasedAtUtc.Value));
        Fact(section, "Signer affirmed ability to access and retain file", Utc(session.AccessAcknowledgedAtUtc.Value));
        Fact(section, "Electronic-record consent accepted", Utc(consent.AcceptedAtUtc));
        Fact(section, "Consent wording version", consent.DisclosureVersion);
        section.AddParagraph("A document release records the server’s response and the signer’s separate acknowledgment. It does not prove that every page was read or understood.");

        Heading(section, "Exact electronic-record disclosure accepted");
        FullText(section, consent.DisclosureText);
        Heading(section, "Exact signing statement accepted");
        FullText(section, completion.IntentText);
        Heading(section, "Selected records for this signing session");
        section.AddParagraph("This selection includes the invitation, any refused signing codes before completion, and this signing session’s authentication, document release, consent and signing decision. Record numbers may have gaps. The complete event history is retained separately by Sati.");
        foreach (var item in events.Where(x => x.Sequence <= signed.Sequence).OrderBy(x => x.Sequence))
            section.AddParagraph($"{item.Sequence.ToString(CultureInfo.InvariantCulture)}. {EventName(item.Kind)} - {Utc(item.OccurredAtUtc)} - {item.ActorKind}");
        section.AddParagraph("Later changes, such as withdrawal of an authorization or access to a copy, are recorded separately. This completion record remains unchanged.");

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var certificateStream = new MemoryStream();
        renderer.PdfDocument.Save(certificateStream, false);
        certificateStream.Position = 0;
        using var originalStream = new MemoryStream(original, writable: false);
        using var source = PdfReader.Open(originalStream, PdfDocumentOpenMode.Import);
        using var certificate = PdfReader.Open(certificateStream, PdfDocumentOpenMode.Import);
        using var packet = new PdfDocument();
        packet.Info.Title = "Signed document and electronic signing evidence - synthetic testing";
        packet.Info.Author = "Sati";
        packet.Info.CreationDate = DateTime.SpecifyKind(completion.SignedAtUtc, DateTimeKind.Utc);
        packet.Info.ModificationDate = packet.Info.CreationDate;
        foreach (var page in source.Pages) packet.AddPage(page);
        foreach (var page in certificate.Pages) packet.AddPage(page);
        using var output = new MemoryStream();
        packet.Save(output, false);
        if (output.Length > SignatureRules.MaximumPdfBytes * 2) throw Integrity();
        return output.ToArray();
    }

    private static void Heading(Section section, string text)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Size = 12;
        paragraph.Format.SpaceBefore = Unit.FromPoint(12);
        paragraph.Format.KeepWithNext = true;
    }
    private static void Fact(Section section, string label, string value)
    {
        var paragraph = section.AddParagraph();
        paragraph.AddFormattedText(label + ": ", TextFormat.Bold);
        paragraph.AddText(value);
    }
    private static void FullText(Section section, string value)
    {
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            section.AddParagraph(line);
    }
    private static string Utc(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    private static string Capacity(string value) => value switch { "Consumer" => "Person signing for themself", "Guardian" => "Guardian", "AuthorizedRepresentative" => "Authorized representative", _ => "Unrecognized role" };
    private static string EventName(string value) => value switch
    {
        "Issued" => "Invitation created", "Authenticated" => "Signing code accepted", "DocumentReleased" => "Document released",
        "ElectronicConsent" => "Electronic-record consent accepted", "Signed" => "Signing statement accepted",
        "PinRejected" => "Signing code refused", "PinLocked" => "Signing code locked", "SessionExtended" => "Signer extended session", _ => value
    };
    private static SignatureWorkflowException Integrity() => new("signature_integrity_failed", "The retained signing evidence could not be verified. Please contact your case manager.", 503);
}
