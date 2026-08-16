using System.Reflection;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What the sign-in screen is allowed to offer someone who has not signed in.
///
/// Until 2026-08-15 it offered "Create an account", enabled for every local
/// (non-API) environment — which meant Production. Anyone who could launch Sati
/// could mint themselves a CaseManager account with no credentials, no approval,
/// and no record of who authorised it, choosing their own supervisor from a
/// dropdown built by enumerating every staff account in the database.
///
/// These tests are reflection-based on purpose. The defect was not a wrong value
/// in a property; it was the EXISTENCE of a reachable command on an
/// unauthenticated surface. Asserting that a flag is false would have passed
/// against the vulnerable design, because the command was still there and still
/// bindable behind a hidden button.
/// </summary>
public sealed class SignInSurfaceTests
{
    [Fact]
    public void TheSignInScreenExposesNoAccountCreationCommand()
    {
        var members = typeof(LoginWindowViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(member => member.Name)
            .ToList();

        Assert.DoesNotContain(members, name =>
            name.Contains("NewUser", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CreateAccount", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Register", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheSignInViewModelCannotReachTheUserDirectory()
    {
        // The supervisor dropdown on the old creation window was populated by
        // listing every user. An unauthenticated screen has no business holding
        // anything that can enumerate staff, so it takes no user service at all.
        var dependencies = typeof(LoginWindowViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain("IUserService", dependencies);
    }

    [Fact]
    public void TheSignInScreenStillOffersSigningIn()
    {
        // Guards the obvious over-correction: removing the account-creation
        // surface must not remove the reason the window exists.
        var members = typeof(LoginWindowViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .ToList();

        Assert.Contains(members, name => name.Contains("Login", StringComparison.Ordinal));
    }
}
