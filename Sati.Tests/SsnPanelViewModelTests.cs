using Sati.Contracts.V1;
using Sati.Data;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The shared SSN panel, used by both the consumer profile and the DHHS forms
/// workspace.
///
/// Most of these are about what the panel stops holding. A revealed Social Security
/// number left in a view model after the case manager moved to another consumer is a
/// screen-sharing accident, and it is the kind of leak no compiler or endpoint test
/// would catch.
/// </summary>
public sealed class SsnPanelViewModelTests
{
    private const int Person = 101;
    private const int OtherPerson = 202;

    [Fact]
    public void SelectingAConsumerShowsTheMaskAndNotTheNumber()
    {
        var panel = new SsnPanelViewModel(new FakeFormService { Masked = "***-**-6789", OnFile = true });

        panel.SetPerson(Person);

        Assert.Equal("***-**-6789", panel.Masked);
        Assert.False(panel.IsRevealed);
        Assert.Empty(panel.Revealed);
    }

    [Fact]
    public async Task ShowingPutsTheNumberOnScreenAndHidingTakesItBack()
    {
        var panel = new SsnPanelViewModel(new FakeFormService { OnFile = true, Plaintext = "123456789" });
        panel.SetPerson(Person);

        await panel.RevealCommand.ExecuteAsync(null);
        Assert.True(panel.IsRevealed);
        Assert.Equal("123456789", panel.Revealed);

        panel.HideCommand.Execute(null);
        Assert.False(panel.IsRevealed);
        Assert.Empty(panel.Revealed);
    }

    /// <summary>
    /// The one that matters most. Moving to another consumer must drop a shown number
    /// immediately — not on the next load, and not merely visually.
    /// </summary>
    [Fact]
    public async Task MovingToAnotherConsumerDropsARevealedNumber()
    {
        var panel = new SsnPanelViewModel(new FakeFormService { OnFile = true, Plaintext = "123456789" });
        panel.SetPerson(Person);
        await panel.RevealCommand.ExecuteAsync(null);

        panel.SetPerson(OtherPerson);

        Assert.False(panel.IsRevealed);
        Assert.Empty(panel.Revealed);
    }

    /// <summary>A half-typed number must not follow selection to a different consumer.</summary>
    [Fact]
    public void MovingToAnotherConsumerDropsAHalfTypedEntry()
    {
        var panel = new SsnPanelViewModel(new FakeFormService());
        panel.SetPerson(Person);
        panel.Entry = "123-45-67";

        panel.SetPerson(OtherPerson);

        Assert.Empty(panel.Entry);
    }

    /// <summary>Storing must not leave the number sitting in a bound control.</summary>
    [Fact]
    public async Task SavingClearsTheEntryBox()
    {
        var service = new FakeFormService();
        var panel = new SsnPanelViewModel(service);
        panel.SetPerson(Person);
        panel.Entry = "123-45-6789";

        await panel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(panel.Entry);
        Assert.Equal("123456789", service.LastSaved);
    }

    [Fact]
    public async Task ANumberThatWasNeverIssuedIsRefusedBeforeItTravels()
    {
        var service = new FakeFormService();
        var panel = new SsnPanelViewModel(service);
        panel.SetPerson(Person);
        panel.Entry = "666-12-3456";

        await panel.SaveCommand.ExecuteAsync(null);

        Assert.Null(service.LastSaved);
        Assert.Contains("not a valid", panel.StatusMessage);
    }

    /// <summary>
    /// A path that cannot reveal must not offer it. The cloud path stores but never
    /// returns plaintext, and a Show button that always failed would be worse than none.
    /// </summary>
    [Fact]
    public void APathThatCannotRevealDoesNotOfferIt()
    {
        var panel = new SsnPanelViewModel(
            new FakeFormService { CanStore = true, CanReveal = false, OnFile = true });

        panel.SetPerson(Person);

        Assert.False(panel.CanReveal);
        Assert.Contains("never reads it back", panel.Explanation);
    }

    [Fact]
    public void APathThatStoresNothingSaysSo()
    {
        var panel = new SsnPanelViewModel(
            new FakeFormService { CanStore = false, CanReveal = false });

        panel.SetPerson(Person);

        Assert.False(panel.SupportsStorage);
        Assert.Contains("does not store", panel.Explanation);
    }

    /// <summary>
    /// A database that moved reports a specific cause. The panel must surface that
    /// sentence rather than swallowing it, because it names the fix.
    /// </summary>
    [Fact]
    public async Task AnUnreadableNumberShowsTheReasonRatherThanFailingSilently()
    {
        var panel = new SsnPanelViewModel(new FakeFormService
        {
            OnFile = true,
            RevealThrows = new InvalidOperationException(
                "This Social Security number was encrypted by a different Windows account."),
        });
        panel.SetPerson(Person);

        await panel.RevealCommand.ExecuteAsync(null);

        Assert.False(panel.IsRevealed);
        Assert.Contains("different Windows account", panel.StatusMessage);
    }

    [Fact]
    public async Task RemovingClearsTheMask()
    {
        var panel = new SsnPanelViewModel(new FakeFormService { Masked = "***-**-6789", OnFile = true });
        panel.SetPerson(Person);

        await panel.ClearCommand.ExecuteAsync(null);

        Assert.False(panel.IsOnFile);
        Assert.Equal(SsnMask.NotOnFile, panel.Masked);
    }

    private sealed class FakeFormService : IDhhsFormService
    {
        public bool CanStore { get; init; } = true;
        public bool CanReveal { get; init; } = true;
        public string Masked { get; init; } = SsnMask.NotOnFile;
        public bool OnFile { get; init; }
        public string Plaintext { get; init; } = "123456789";
        public Exception? RevealThrows { get; init; }
        public string? LastSaved { get; private set; }

        public bool SupportsSsnStorage => CanStore;
        public bool SupportsSsnReveal => CanReveal;

        public Task<SsnStatusDto> GetSsnStatusAsync(int personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SsnStatusDto(Masked, OnFile));

        public Task<string> RevealSsnAsync(int personId, CancellationToken cancellationToken = default) =>
            RevealThrows is not null
                ? Task.FromException<string>(RevealThrows)
                : Task.FromResult(Plaintext);

        public Task<SsnStatusDto> UpdateSsnAsync(
            int personId,
            string? socialSecurityNumber,
            CancellationToken cancellationToken = default)
        {
            LastSaved = socialSecurityNumber;
            return Task.FromResult(socialSecurityNumber is null
                ? new SsnStatusDto(SsnMask.NotOnFile, false)
                : new SsnStatusDto(SsnMask.Format(SsnMask.LastFourOf(socialSecurityNumber)), true));
        }

        public Task<DhhsFormResult> GenerateAsync(
            DhhsFormDefinition.FormKey form,
            int personId,
            DhhsFormDefinition.Selections selections,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DhhsFormResult([1], "form.pdf", []));
    }
}
