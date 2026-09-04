using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class ConsumerPickerSortTests
{
    [Fact]
    public void WhenDisabledTheListPassesThroughUnchanged()
    {
        // Order must survive exactly as the service returned it — an existing user's
        // combo-box order must not shift just because this preference exists.
        var zed = Named("Zed", "Zephyr");
        var ann = Named("Ann", "Anders");
        var people = new List<Person> { zed, ann };

        var result = CaseManagerDashboardViewModel.ApplyConsumerPickerSort(people, sortByLastName: false);

        Assert.Same(people, result);
        Assert.Equal(["Zed", "Ann"], result.Select(p => p.FirstName));
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
