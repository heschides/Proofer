using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Security;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// First-run administrator setup.
///
/// The property under test is that the bootstrap window is open EXACTLY ONCE and
/// closes itself. An installation with no administrator cannot be administered;
/// an installation where the bootstrap stays open is one where anyone reaching
/// the database can mint a privileged account at will. Both failures are severe
/// and they pull in opposite directions, so the boundary is worth pinning.
///
/// What is deliberately absent is any test asserting that an administrator is
/// created automatically, because none ever is. A shipped account whose password
/// the software knows would be a backdoor in every install.
/// </summary>
public sealed class AdministratorBootstrapTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<SatiContext> _factory;

    public AdministratorBootstrapTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(_connection).Options;
        // EnsureCreated applies the model's seed data, which already includes the
        // two agencies every Sati database ships with ("Internal", "Sandbox Mode").
        // Adding another would misrepresent what an installation looks like.
        using (var context = new SatiContext(options))
            context.Database.EnsureCreated();

        _factory = new ContextFactory(options);
    }

    public void Dispose() => _connection.Dispose();

    private UserService NewService() => new(_factory, new PasswordHasher());

    // Seeds directly rather than through UserService.CreateAsync, which now requires an
    // authorizing actor. These tests are about whether an administrator EXISTS and which
    // agency the first one joins — not about who may create users. An installation with no
    // administrator has nobody who could authorize a create in the first place, which is
    // the whole premise of first-run setup.
    private async Task SeedAsync(User user)
    {
        await using var context = _factory.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private static SecureString Secure(string value)
    {
        var secure = new SecureString();
        foreach (var character in value)
            secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }

    private const string StrongPassword = "correct-horse-battery-staple";

    private static User NewAdmin(string username = "admin") =>
        User.Create(0, username, "First Administrator", string.Empty, string.Empty,
            UserRole.CaseManager, null, 0);

    // ---- The window opens when there is nobody to administer the install ----

    [Fact]
    public async Task AnEmptyInstallationHasNoAdministrator()
    {
        Assert.False(await NewService().AnyAdministratorExistsAsync());
    }

    [Fact]
    public async Task AnInstallationWithOnlyACaseManagerStillHasNoAdministrator()
    {
        // Exactly the situation that prompted this: a real installation, real
        // work in it, and nobody who can create a supervisor.
        var service = NewService();
        await SeedAsync(
            User.Create(0, "cm", "Case Manager", string.Empty, string.Empty, UserRole.CaseManager, null, 1));

        Assert.False(await service.AnyAdministratorExistsAsync());
    }

    [Fact]
    public async Task ASupervisorDoesNotCountAsAnAdministrator()
    {
        var service = NewService();
        await SeedAsync(
            User.Create(0, "sup", "Supervisor", string.Empty, string.Empty, UserRole.Supervisor, null, 1));

        Assert.False(await service.AnyAdministratorExistsAsync());
    }

    [Fact]
    public async Task APlatformOperatorDoesNotCountAsAnAgencyAdministrator()
    {
        // PlatformOperator is Sati's own cross-tenant telemetry identity. Its
        // presence must not convince an agency it has an administrator.
        var service = NewService();
        await SeedAsync(
            User.Create(0, "operator", "Platform Operator", string.Empty, string.Empty,
                UserRole.PlatformOperator, null, 1));

        Assert.False(await service.AnyAdministratorExistsAsync());
        Assert.False(AdministratorBootstrap.IsAdministrator(User.Create(
            0, "operator", "Platform Operator", string.Empty, string.Empty,
            UserRole.PlatformOperator, null, 1)));
    }

    // ---- Creating one, and the window closing behind it ----

    [Fact]
    public async Task TheFirstAdministratorIsCreatedWithTheChosenPasswordAndTheAdminRole()
    {
        var service = NewService();

        var created = await service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        Assert.Equal(UserRole.Admin, created.Role);
        Assert.Equal(Sati.Contracts.V1.UserPermissions.AllAgencyPermissions, created.Permissions);
        Assert.True(await service.AnyAdministratorExistsAsync());

        // The password is the one that was chosen, and it was hashed, not stored.
        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.SingleAsync(user => user.Username == "admin");
        Assert.NotEqual(StrongPassword, stored.PasswordHash);
        Assert.NotEmpty(stored.Salt);
        Assert.True(new PasswordHasher().Verify(Secure(StrongPassword), stored.PasswordHash, stored.Salt));
    }

    [Fact]
    public async Task TheRoleIsForcedRatherThanTakenFromTheCaller()
    {
        // The caller passes a CaseManager. If the service honoured that, the
        // installation would still have no administrator and the bootstrap would
        // stay open forever.
        var service = NewService();

        var created = await service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        Assert.Equal(UserRole.Admin, created.Role);
    }

    [Fact]
    public async Task ASecondBootstrapIsRefused()
    {
        // THE guard. Without it this is an unauthenticated way to mint privileged
        // accounts on an installation that already has an administrator.
        var service = NewService();
        await service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        await Assert.ThrowsAsync<AdministratorAlreadyExistsException>(
            () => service.CreateFirstAdministratorAsync(NewAdmin("intruder"), Secure(StrongPassword)));

        await using var context = _factory.CreateDbContext();
        Assert.False(await context.Users.AnyAsync(user => user.Username == "intruder"));
    }

    [Fact]
    public async Task AnAdministratorCreatedByAnyOtherRouteAlsoClosesTheWindow()
    {
        // The guard reads the database, not a flag this class set. An admin
        // created through the ordinary path — or by the provisioning script —
        // closes the bootstrap just the same.
        var service = NewService();
        await SeedAsync(
            User.Create(0, "existing", "Existing Admin", string.Empty, string.Empty, UserRole.Admin, null, 1));

        await Assert.ThrowsAsync<AdministratorAlreadyExistsException>(
            () => service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword)));
    }

    [Fact]
    public async Task ADuplicateUsernameIsRefused()
    {
        var service = NewService();
        await SeedAsync(
            User.Create(0, "admin", "Someone Else", string.Empty, string.Empty, UserRole.CaseManager, null, 1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateFirstAdministratorAsync(NewAdmin("admin"), Secure(StrongPassword)));
    }

    [Fact]
    public async Task TheAdministratorJoinsTheAgencyTheExistingUsersAreIn()
    {
        // The point of the resolution rule: an administrator exists to administer
        // the agency that holds the work. Agency 2 here, not the seeded default.
        var service = NewService();
        await SeedAsync(
            User.Create(0, "cm", "Case Manager", string.Empty, string.Empty, UserRole.CaseManager, null, 2));

        var created = await service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        Assert.Equal(2, created.AgencyId);
    }

    [Fact]
    public async Task AnEmptyInstallationFallsBackToThePrimarySeededAgency()
    {
        var created = await NewService().CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        await using var context = _factory.CreateDbContext();
        var lowest = await context.Agencies.OrderBy(agency => agency.Id).Select(a => a.Id).FirstAsync();
        Assert.Equal(lowest, created.AgencyId);
    }

    [Fact]
    public async Task UsersSpreadAcrossAgenciesAreReportedRatherThanGuessedAt()
    {
        var service = NewService();
        await SeedAsync(
            User.Create(0, "one", "One", string.Empty, string.Empty, UserRole.CaseManager, null, 1));
        await SeedAsync(
            User.Create(0, "two", "Two", string.Empty, string.Empty, UserRole.CaseManager, null, 2));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword)));

        Assert.Contains("ambiguous", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APlatformOperatorsAgencyDoesNotDecideWhereTheAdministratorGoes()
    {
        // The cross-tenant identity must not drag the agency answer around.
        var service = NewService();
        await SeedAsync(
            User.Create(0, "cm", "Case Manager", string.Empty, string.Empty, UserRole.CaseManager, null, 2));
        await SeedAsync(
            User.Create(0, "operator", "Operator", string.Empty, string.Empty, UserRole.PlatformOperator, null, 1));

        var created = await service.CreateFirstAdministratorAsync(NewAdmin(), Secure(StrongPassword));

        Assert.Equal(2, created.AgencyId);
    }

    // ---- The password rule ----

    [Fact]
    public void ValidDetailsProduceNoProblems()
    {
        Assert.Empty(AdministratorBootstrap.FindProblems(
            "admin", "First Administrator", StrongPassword.Length, passwordsMatch: true));
    }

    [Fact]
    public void AShortPasswordIsRefused()
    {
        // The bar is deliberately higher than the 8 characters an ordinary
        // self-service change accepts: this account can create every other one.
        var problems = AdministratorBootstrap.FindProblems(
            "admin", "First Administrator",
            AdministratorBootstrap.MinimumPasswordLength - 1, passwordsMatch: true);

        Assert.Contains(problems, problem => problem.Contains("at least", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MismatchedPasswordsAreRefused()
    {
        var problems = AdministratorBootstrap.FindProblems(
            "admin", "First Administrator", StrongPassword.Length, passwordsMatch: false);

        Assert.Contains(problems, problem => problem.Contains("do not match", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]                  // too short
    [InlineData("Admin")]               // uppercase
    [InlineData("has space")]
    [InlineData("has@symbol")]
    public void AMalformedUsernameIsRefused(string username)
    {
        Assert.NotEmpty(AdministratorBootstrap.FindProblems(
            username, "First Administrator", StrongPassword.Length, passwordsMatch: true));
    }

    [Fact]
    public void AMissingDisplayNameIsRefused()
    {
        Assert.NotEmpty(AdministratorBootstrap.FindProblems(
            "admin", "   ", StrongPassword.Length, passwordsMatch: true));
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
