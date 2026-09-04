using System.Globalization;
using Sati.Converters;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class PersonNameFormatConverterTests
{
    private readonly PersonNameFormatConverter _converter = new();

    [Fact]
    public void WhenOffFormatsAsFirstLast()
    {
        var person = Named("John", "Doe");

        var result = _converter.Convert([person, false], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("John Doe", result);
    }

    [Fact]
    public void WhenOnFormatsAsLastCommaFirst()
    {
        var person = Named("John", "Doe");

        var result = _converter.Convert([person, true], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Doe, John", result);
    }

    [Fact]
    public void WhenOnAndLastNameIsMissingOmitsTheComma()
    {
        var person = Named("John", null);

        var result = _converter.Convert([person, true], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("John", result);
    }

    [Fact]
    public void WhenTheSecondValueIsUnresolvedFallsBackToFirstLast()
    {
        // The RelativeSource binding that supplies this value crosses a ComboBox popup boundary,
        // which is not guaranteed to resolve for every dropdown item in every WPF rendering path.
        // Failing this open to the ordinary format — rather than throwing — is the point of this
        // test: a display-only feature must never turn an unresolved binding into a crash.
        var person = Named("John", "Doe");

        var result = _converter.Convert(
            [person, System.Windows.DependencyProperty.UnsetValue],
            typeof(string),
            null,
            CultureInfo.InvariantCulture);

        Assert.Equal("John Doe", result);
    }

    private static Person Named(string? firstName, string? lastName)
    {
        var person = Person.Rehydrate(1, userId: 1);
        person.FirstName = firstName;
        person.LastName = lastName;
        return person;
    }
}
