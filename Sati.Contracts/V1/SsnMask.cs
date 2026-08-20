namespace Sati.Contracts.V1;

/// <summary>
/// Sole owner of how a Social Security number is displayed and how it is checked
/// before storage, shared by <c>Sati.Api</c> and the desktop so the two cannot mask
/// the same value two different ways.
///
/// A plaintext SSN exists in exactly one place in Sati: inside the API process,
/// during an audited form-fill. Everywhere else — every DTO, every screen, every
/// log line, every error report — carries the mask instead. That is why the mask is
/// computed from a stored last-four rather than by decrypting: a list of fifty
/// consumers must not cost fifty Key Vault unwraps, and more importantly, nothing
/// outside the form-fill path should have a reason to decrypt at all.
///
/// The last four digits are stored in the clear, deliberately. They are what the
/// mask displays, they cannot reconstruct the number, and keeping them out of the
/// ciphertext is what lets the read path stay both fast and plaintext-free.
/// </summary>
public static class SsnMask
{
    /// <summary>What a masked number looks like when the last four are known.</summary>
    private const string MaskPrefix = "***-**-";

    /// <summary>Shown when a consumer has no SSN on file, rather than an empty cell.</summary>
    public const string NotOnFile = "Not on file";

    /// <summary>
    /// The display form. <paramref name="lastFour"/> is the stored plaintext tail,
    /// or null when the consumer has no SSN.
    /// </summary>
    public static string Format(string? lastFour) =>
        string.IsNullOrWhiteSpace(lastFour) ? NotOnFile : MaskPrefix + lastFour.Trim();

    /// <summary>
    /// Strips formatting so "123-45-6789" and "123456789" store identically.
    /// Returns null when nothing usable remains.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>
    /// True when <paramref name="normalized"/> is a structurally valid SSN.
    ///
    /// This rejects the shapes the Social Security Administration never issues, so a
    /// transposed digit or a placeholder does not reach an official form: nine digits,
    /// area not 000/666/900-999, group not 00, serial not 0000. It is a shape check,
    /// not proof the number belongs to the consumer — nothing local can establish that.
    /// </summary>
    public static bool IsWellFormed(string? normalized)
    {
        if (normalized is null || normalized.Length != 9 || !normalized.All(char.IsAsciiDigit))
            return false;

        var area = int.Parse(normalized[..3]);
        var group = int.Parse(normalized.Substring(3, 2));
        var serial = int.Parse(normalized[5..]);

        return area is not 0 and not 666 && area < 900 && group != 0 && serial != 0;
    }

    /// <summary>The tail to store in the clear for display, or null.</summary>
    public static string? LastFourOf(string? normalized) =>
        normalized is { Length: 9 } ? normalized[5..] : null;
}
