namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Medical provider directory and consumer provider lists";
    public const string ReleaseDate = "August 28, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "The provider directory now describes practices and networks",
            [
                "A medical provider is recorded as an individual clinician, a practice, or a network, and can be affiliated with the organization it belongs to.",
                "Affiliation is one link, so a clinician's practice and network are read from the directory rather than typed twice and allowed to disagree.",
                "The form offers only affiliations Sati will accept, and explains what is missing when there is nothing to choose."
            ]),
        new(
            "A consumer's medical providers, with the practice and network filled in",
            [
                "The consumer profile lists medical providers, with the primary care provider first and past providers kept behind a disclosure.",
                "Choosing a clinician fills in their practice and network from the directory, so correcting the directory once corrects every consumer who names them.",
                "Ending a relationship keeps the entry, so the record can still say who was treating a consumer in a given year.",
                "There is no limit on how many providers a consumer may have."
            ]),
        new(
            "A shared agency directory with room to keep it tidy",
            [
                "The directory has always been agency-wide; any case manager can now add and correct entries, while only an Admin can remove or merge them.",
                "Typing a name that already exists shows a warning rather than blocking the entry, because two organizations can genuinely share a name.",
                "A directory entry can hold several named contacts alongside the organization's general phone line.",
                "An Admin can merge two entries; documents that already named the merged entry keep exactly what they recorded."
            ]),
        new(
            "Still planned before commercial production",
            [
                "The representative-payee billing-department check-release notification, with its own request, approval, release evidence, audit history, concurrency, and idempotency.",
                "Automated retention and legal-hold enforcement; the current operations panel correctly reports PolicyOnly.",
                "Production identity and MFA, external alert routing, backup/restore drills, payer certification, and a controlled cloud Production deployment.",
                "Clearinghouse acceptance, payer enrollment and rate verification, acknowledgments, rejections, remittances, and reconciliation."
            ])
    ];
}
