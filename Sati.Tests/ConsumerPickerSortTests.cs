using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerPickerSortTests
{
    [Fact]
    public void WhenDisabledSortsByFirstNameThenLastName()
    {
        // Both states must sort actively and visibly differently from each other. The caseload
        // query already orders by LastName at the database level, so leaving the list untouched
        // when this preference is off made "off" and "on" look nearly identical in practice —
        // exactly the "the setting does not affect the lists" report this test now guards against.
        var zedZephyr = Named("Zed", "Zephyr");
        var annAnders = Named("Ann", "Anders");
        var people = new List<Person> { zedZephyr, annAnders };

        var result = CaseManagerDashboardViewModel.ApplyConsumerPickerSort(people, sortByLastName: false);

        Assert.Equal(["Ann", "Zed"], result.Select(p => p.FirstName));
    }

    [Fact]
    public void WhenEnabledSortsByLastNameThenFirstName()
    {
        var zedSmith = Named("Zed", "Smith");
        var annJones = Named("Ann", "Jones");
        var bobSmith = Named("Bob", "Smith");
        var people = new List<Person> { zedSmith, annJones, bobSmith };

        var result = CaseManagerDashboardViewModel.ApplyConsumerPickerSort(people, sortByLastName: true);

        Assert.Equal(
            [("Ann", "Jones"), ("Bob", "Smith"), ("Zed", "Smith")],
            result.Select(p => (p.FirstName, p.LastName)));
    }

    [Fact]
    public void WhenDisabledSortIsCaseInsensitiveAndTolerantOfMissingNames()
    {
        var lowercase = Named("adam", "Zed");
        var uppercase = Named("ADAM", "Ann");
        var noFirstName = Named(null, "Nolan");
        var people = new List<Person> { uppercase, noFirstName, lowercase };

        var result = CaseManagerDashboardViewModel.ApplyConsumerPickerSort(people, sortByLastName: false);

        // Nulls sort first under StringComparer.OrdinalIgnoreCase; the two "adam" spellings land
        // together regardless of case, ordered by last name.
        Assert.Equal(["Nolan", "Ann", "Zed"], result.Select(p => p.LastName));
    }

    [Fact]
    public void WhenEnabledSortIsCaseInsensitiveAndTolerantOfMissingNames()
    {
        var lowercase = Named("adam", "adams");
        var uppercase = Named("Zoe", "ADAMS");
        var noLastName = Named("Nolan", null);
        var people = new List<Person> { uppercase, noLastName, lowercase };

        var result = CaseManagerDashboardViewModel.ApplyConsumerPickerSort(people, sortByLastName: true);

        // Nulls sort first under StringComparer.OrdinalIgnoreCase; the two "adams"
        // spellings land together regardless of case, ordered by first name.
        Assert.Equal(["Nolan", "adam", "Zoe"], result.Select(p => p.FirstName));
    }

    private static Person Named(string? firstName, string? lastName)
    {
        var person = Person.Rehydrate(Random.Shared.Next(1, int.MaxValue), userId: 1);
        person.FirstName = firstName;
        person.LastName = lastName;
        return person;
    }
}
