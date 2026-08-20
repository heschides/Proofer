using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The mask is the only form of an SSN that any screen, DTO, or log line is allowed
/// to carry, so it lives in <c>Sati.Contracts</c> and is tested once for both the
/// desktop and the API.
/// </summary>
public sealed class SsnMaskTests
{
    [Theory]
    [InlineData("123-45-6789", "123456789")]
    [InlineData("123 45 6789", "123456789")]
    [InlineData("  123456789  ", "123456789")]
    public void Formatting_is_stripped_so_one_number_stores_one_way(string entered, string expected) =>
        Assert.Equal(expected, SsnMask.Normalize(entered));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Nothing_usable_normalizes_to_null(string? entered) =>
        Assert.Null(SsnMask.Normalize(entered));

    /// <summary>
    /// The shapes the Social Security Administration never issues, so a transposed
    /// digit or a placeholder does not reach an official state form.
    /// </summary>
    [Theory]
    [InlineData("000123456")]   // area 000
    [InlineData("666123456")]   // area 666
    [InlineData("900123456")]   // area 900+
    [InlineData("123006789")]   // group 00
    [InlineData("123450000")]   // serial 0000
    [InlineData("12345678")]    // too short
    [InlineData("1234567890")]  // too long
    public void Numbers_that_are_never_issued_are_rejected(string candidate) =>
        Assert.False(SsnMask.IsWellFormed(candidate));

    [Theory]
    [InlineData("123456789")]
    [InlineData("001010001")]
    public void A_structurally_valid_number_is_accepted(string candidate) =>
        Assert.True(SsnMask.IsWellFormed(candidate));

    [Fact]
    public void The_display_form_reveals_only_the_last_four()
    {
        var normalized = SsnMask.Normalize("123-45-6789");
        var lastFour = SsnMask.LastFourOf(normalized);

        Assert.Equal("6789", lastFour);
        Assert.Equal("***-**-6789", SsnMask.Format(lastFour));
    }

    /// <summary>
    /// A consumer with no SSN reads as "Not on file" rather than an empty cell, so a
    /// blank never gets mistaken for a value the screen failed to load.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_number_on_file_says_so(string? lastFour) =>
        Assert.Equal(SsnMask.NotOnFile, SsnMask.Format(lastFour));

    /// <summary>
    /// The mask must never be reversible. Whatever a screen or a log holds, the first
    /// five digits are not recoverable from it.
    /// </summary>
    [Fact]
    public void The_mask_carries_none_of_the_first_five_digits()
    {
        var masked = SsnMask.Format(SsnMask.LastFourOf(SsnMask.Normalize("123456789")));

        Assert.DoesNotContain("12345", masked);
        Assert.StartsWith("***-**-", masked);
    }
}
