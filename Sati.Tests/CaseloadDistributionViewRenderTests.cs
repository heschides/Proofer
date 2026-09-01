using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using System.Security;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The distribution screen loaded for real, with a DataContext.
///
/// <para>
/// <c>CaseloadDistributionViewModelTests</c> proves the view model asks for the right moves.
/// It cannot prove the screen reaches it. Every failure mode this file exists for is silent at
/// runtime: a <c>DynamicResource</c> naming a brush that does not exist renders with the default
/// foreground and logs nothing, a mistyped command binding leaves a button that looks wired and
/// does nothing, and a converter key that is not in scope throws only once the binding is
/// evaluated on a realized visual tree.
/// </para>
///
/// <para>
/// The brush case is not hypothetical: this view first referenced <c>DangerBrush</c>, which the
/// themes do not define. Nothing failed — failed rows would simply have rendered in the ordinary
/// text colour, quietly removing the visual distinction between a consumer that moved and one
/// that did not.
/// </para>
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class CaseloadDistributionViewRenderTests
{
    [Fact]
    public void TheActionButtonsReachTheViewModelCommands()
    {
        var model = new CaseloadDistributionViewModel(
            new NoOpPersonService(), new NoOpUserService(), new SessionService());

        Render(model, view =>
        {
            var buttons = WpfUiHarness.Descendants(view).OfType<Button>().ToList();

            var selectAll = buttons.Single(button => Equals(button.Content, "Select all"));
            var clear = buttons.Single(button => Equals(button.Content, "Clear"));
            var move = buttons.Single(button => Equals(button.Content, "Move selected"));

            // Same instance, not merely non-null: a binding resolved to some other command
            // still leaves a button that looks wired.
            Assert.Same(model.SelectAllCommand, selectAll.Command);
            Assert.Same(model.ClearSelectionCommand, clear.Command);
            Assert.Same(model.DistributeCommand, move.Command);
        });
    }

    [Fact]
    public void TheMoveButtonIsInertUntilThereIsSomethingToMove()
    {
        var model = new CaseloadDistributionViewModel(
            new NoOpPersonService(), new NoOpUserService(), new SessionService());

        Render(model, view =>
        {
            var move = WpfUiHarness.Descendants(view).OfType<Button>()
                .Single(button => Equals(button.Content, "Move selected"));

            Assert.False(move.IsEnabled);
        });
    }

    [Fact]
    public void TheTargetSelectorIsReachableByItsAccessibleName()
    {
        var model = new CaseloadDistributionViewModel(
            new NoOpPersonService(), new NoOpUserService(), new SessionService());

        Render(model, view =>
        {
            var selector = WpfUiHarness.FindByAutomationName<ComboBox>(
                view, "Case manager to move the selected consumers to");

            Assert.NotNull(selector);
        });
    }

    private static void Render(CaseloadDistributionViewModel model, Action<CaseloadDistributionView> assert)
    {
        WpfUiHarness.Run(() =>
        {
            var view = new CaseloadDistributionView { DataContext = model };
            WpfUiHarness.Realize(view);
            assert(view);
        });
    }

    private sealed class NoOpPersonService : IPersonService
    {
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            Task.FromResult(new List<PersonSummary>());
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId, int targetUserId, int expectedRevision) => throw new NotSupportedException();
        public Task<Person> AddPersonAsync(Person person) => throw new NotSupportedException();
        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<string?> GetJournalAsync(int personId) => throw new NotSupportedException();
        public Task SaveJournalAsync(int personId, string? journal) => throw new NotSupportedException();
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpUserService : IUserService
    {
        public Task<List<User>> GetAllAsync() =>
            Task.FromResult(new List<User>());
        public Task<User> CreateAsync(
            AgencyActor actor, User user, SecureString p) =>
            throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => throw new NotSupportedException();
        public Task<User> CreateFirstAdministratorAsync(
            User user, SecureString p) => throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) =>
            throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(
            AgencyActor actor, User user) => throw new NotSupportedException();
        public Task ResetPasswordAsync(
            AgencyActor actor, User user, SecureString p) =>
            throw new NotSupportedException();
        public Task ChangePasswordAsync(
            User user, SecureString current, SecureString next) =>
            throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) =>
            throw new NotSupportedException();
    }
}
