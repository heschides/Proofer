using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Security;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Authorization on the desktop-local user-management writes.
///
/// <para>
/// On local Production there is no API behind <see cref="UserService"/>. Before these
/// existed the only thing standing between a supervisor and a self-minted administrator
/// was <c>CanAssignExpandedPermissions</c>, a view-model boolean bound to a checkbox —
/// finding 5 of the 2026-08-30 pass in API_SECURITY_AUDIT.md. Every test here therefore
/// calls the service directly, with no view model involved, because a view model is
/// exactly what must not be the control.
/// </para>
/// </summary>
public sealed class LocalUserManagementAuthorizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<SatiContext> _factory;

    private const int AgencyId = 1;
    private const int OtherAgencyId = 2;
    private const string StrongPassword = "correct-horse-battery-staple";

    public LocalUserManagementAuthorizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<SatiContext>().UseSqlite(_connection).Options;
        using (var context = new SatiContext(options))
            context.Database.EnsureCreated();
        _factory = new ContextFactory(options);
    }

    public void Dispose() => _connection.Dispose();

    private UserService NewService() => new(_factory, new PasswordHasher());

    private async Task<User> SeedAsync(
        int id, string username, UserRole role, int agencyId = AgencyId, int? supervisorId = null)
    {
        var user = User.Create(id, username, username, string.Empty, string.Empty,
            role, supervisorId, agencyId);
        await using var context = _factory.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static User NewUser(
        UserPermissions permissions, int? supervisorId, int agencyId = AgencyId, string username = "new-user")
    {
        var user = User.Create(0, username, username, string.Empty, string.Empty,
            UserRole.CaseManager, supervisorId, agencyId);
        user.Permissions = permissions;
        return user;
    }

    private static SecureString Secure(string value)
    {
        var secure = new SecureString();
        foreach (var character in value)
            secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }

    // ---- Creation ----

    [Fact]
    public async Task ASupervisorCannotCreateAnAdministrator()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().CreateAsync(
                supervisor.ToAgencyActor(),
                NewUser(UserPermissions.AllAgencyPermissions, supervisor.Id),
                Secure(StrongPassword)));

        await using var context = _factory.CreateDbContext();
        Assert.False(await context.Users.AnyAsync(user => user.Username == "new-user"));
    }

    [Fact]
    public async Task ASupervisorCannotCreateABillerOrAnAgencyWideReviewer()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        var service = NewService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(
                supervisor.ToAgencyActor(),
                NewUser(UserPermissions.CaseManagement | UserPermissions.Billing, supervisor.Id),
                Secure(StrongPassword)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(
                supervisor.ToAgencyActor(),
                NewUser(
                    UserPermissions.CaseManagement | UserPermissions.AgencyWideSupervision,
                    supervisor.Id),
                Secure(StrongPassword)));
    }

    [Fact]
    public async Task ASupervisorCannotAssignACreatedUserToSomebodyElse()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        await SeedAsync(23, "other-supervisor", UserRole.Supervisor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().CreateAsync(
                supervisor.ToAgencyActor(),
                NewUser(UserPermissions.CaseManagement, supervisorId: 23),
                Secure(StrongPassword)));
    }

    [Fact]
    public async Task ACaseManagerCannotCreateUsersAtAll()
    {
        var caseManager = await SeedAsync(12, "case-manager", UserRole.CaseManager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().CreateAsync(
                caseManager.ToAgencyActor(),
                NewUser(UserPermissions.CaseManagement, caseManager.Id),
                Secure(StrongPassword)));
    }

    [Fact]
    public async Task ASupervisorMayStillCreateTheirOwnCaseManager()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);

        var created = await NewService().CreateAsync(
            supervisor.ToAgencyActor(),
            NewUser(UserPermissions.CaseManagement, supervisor.Id),
            Secure(StrongPassword));

        Assert.Equal(UserPermissions.CaseManagement, created.Permissions);
        Assert.Equal(UserRole.CaseManager, created.Role);
        Assert.Equal(AgencyId, created.AgencyId);
    }

    // A caller-supplied actor is matched against the database, never believed. Claiming a
    // permission set the row does not carry is the desktop form of forging a token claim.
    [Fact]
    public async Task AFabricatedActorPermissionSetIsRefused()
    {
        var caseManager = await SeedAsync(12, "case-manager", UserRole.CaseManager);
        var forged = new AgencyActor(
            caseManager.Id, caseManager.AgencyId, UserPermissions.AllAgencyPermissions);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().CreateAsync(
                forged,
                NewUser(UserPermissions.AllAgencyPermissions, null),
                Secure(StrongPassword)));
    }

    [Fact]
    public async Task TheCreatedUsersAgencyComesFromTheActorNotTheRequest()
    {
        var admin = await SeedAsync(11, "admin", UserRole.Admin);

        var created = await NewService().CreateAsync(
            admin.ToAgencyActor(),
            NewUser(UserPermissions.CaseManagement, null, agencyId: OtherAgencyId),
            Secure(StrongPassword));

        Assert.Equal(AgencyId, created.AgencyId);
    }

    // ---- Update ----

    [Fact]
    public async Task ASupervisorCannotPromoteThemselves()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        supervisor.Permissions = UserPermissions.AllAgencyPermissions;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateAsync(
                new AgencyActor(13, AgencyId, UserPermissions.CaseManagement | UserPermissions.Supervision),
                supervisor));

        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.SingleAsync(user => user.Id == 13);
        Assert.False(UserPermissionRules.HasAdminPermissions(stored.Permissions));
    }

    [Fact]
    public async Task ASupervisorCannotPromoteTheirOwnCaseManager()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        var caseManager = await SeedAsync(12, "case-manager", UserRole.CaseManager, supervisorId: 13);
        caseManager.Permissions = UserPermissions.CaseManagement | UserPermissions.Administration;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateAsync(supervisor.ToAgencyActor(), caseManager));

        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.SingleAsync(user => user.Id == 12);
        Assert.Equal(UserPermissions.CaseManagement, stored.Permissions);
    }

    [Fact]
    public async Task ASupervisorCannotEditSomebodyElsesCaseManager()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        await SeedAsync(23, "other-supervisor", UserRole.Supervisor);
        var stranger = await SeedAsync(22, "stranger", UserRole.CaseManager, supervisorId: 23);
        stranger.Email = "renamed@example.org";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateAsync(supervisor.ToAgencyActor(), stranger));
    }

    [Fact]
    public async Task AnAdministratorCannotEditAUserInAnotherAgency()
    {
        var admin = await SeedAsync(11, "admin", UserRole.Admin);
        var foreigner = await SeedAsync(22, "foreign", UserRole.CaseManager, OtherAgencyId);
        foreigner.Email = "renamed@example.org";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateAsync(admin.ToAgencyActor(), foreigner));
    }

    // PlatformOperator is Sati's cross-tenant identity, not an agency user. It must be
    // neither editable through agency user management nor producible by it.
    [Fact]
    public async Task ThePlatformOperatorIdentityIsNeitherEditableNorMintable()
    {
        var admin = await SeedAsync(11, "admin", UserRole.Admin);
        var operatorUser = await SeedAsync(31, "operator", UserRole.PlatformOperator);
        operatorUser.Permissions = UserPermissions.AllAgencyPermissions;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateAsync(admin.ToAgencyActor(), operatorUser));

        var minted = NewUser(UserPermissions.AllAgencyPermissions, null, username: "minted");
        minted.Role = UserRole.PlatformOperator;
        var created = await NewService().CreateAsync(
            admin.ToAgencyActor(), minted, Secure(StrongPassword));

        Assert.Equal(UserRole.Admin, created.Role);
    }

    // ---- Self-service profile ----

    [Fact]
    public async Task ACaseManagerMayEditTheirOwnContactDetailsWithoutUserManagement()
    {
        var caseManager = await SeedAsync(12, "case-manager", UserRole.CaseManager);
        caseManager.Email = "cm@example.org";
        caseManager.Phone = "2075550100";

        await NewService().UpdateOwnContactDetailsAsync(caseManager.ToAgencyActor(), caseManager);

        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.SingleAsync(user => user.Id == 12);
        Assert.Equal("cm@example.org", stored.Email);
    }

    // The self-service route takes a whole User, so it has to ignore everything except the
    // two contact fields rather than copy what it is handed.
    [Fact]
    public async Task TheSelfServiceProfileRouteCannotChangePermissions()
    {
        var caseManager = await SeedAsync(12, "case-manager", UserRole.CaseManager);
        caseManager.Email = "cm@example.org";
        caseManager.Permissions = UserPermissions.AllAgencyPermissions;

        await NewService().UpdateOwnContactDetailsAsync(
            new AgencyActor(12, AgencyId, UserPermissions.CaseManagement), caseManager);

        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.SingleAsync(user => user.Id == 12);
        Assert.Equal(UserPermissions.CaseManagement, stored.Permissions);
        Assert.Equal("cm@example.org", stored.Email);
    }

    [Fact]
    public async Task TheSelfServiceProfileRouteCannotEditSomebodyElse()
    {
        await SeedAsync(12, "case-manager", UserRole.CaseManager);
        var victim = await SeedAsync(14, "victim", UserRole.CaseManager);
        victim.Email = "attacker@example.org";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().UpdateOwnContactDetailsAsync(
                new AgencyActor(12, AgencyId, UserPermissions.CaseManagement), victim));
    }

    // ---- Password reset ----

    [Fact]
    public async Task ASupervisorCannotResetTheirOwnSupervisorsPassword()
    {
        var supervisor = await SeedAsync(13, "supervisor", UserRole.Supervisor);
        var admin = await SeedAsync(11, "admin", UserRole.Admin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            NewService().ResetPasswordAsync(
                supervisor.ToAgencyActor(), admin, Secure(StrongPassword)));
    }

    [Fact]
    public async Task AnAdministratorResetPersistsAUsableReplacementPassword()
    {
        var admin = await SeedAsync(11, "admin", UserRole.Admin);
        var target = await SeedAsync(12, "target", UserRole.CaseManager);

        await NewService().ResetPasswordAsync(
            admin.ToAgencyActor(), target, Secure(StrongPassword));

        await using var context = _factory.CreateDbContext();
        var stored = await context.Users.AsNoTracking().SingleAsync(user => user.Id == target.Id);
        Assert.True(new PasswordHasher().Verify(Secure(StrongPassword), stored.PasswordHash, stored.Salt));
    }

    private sealed class ContextFactory(DbContextOptions<SatiContext> options)
        : IDbContextFactory<SatiContext>
    {
        public SatiContext CreateDbContext() => new(options);
    }
}
