using System.IO;
using Sati.Services;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class IdleSessionStateTests
{
    private DateTimeOffset _now = new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

    private IdleSessionState Create(int minutes = IdleLockPreferenceService.DefaultMinutes)
    {
        var state = new IdleSessionState(() => _now);
        state.ApplyTimeout(minutes);
        return state;
    }

    [Fact]
    public void TheScreenStaysClearUntilTheConfiguredTimeHasFullyElapsed()
    {
        var state = Create(10);

        _now = _now.AddMinutes(9).AddSeconds(59);
        state.Evaluate();
        Assert.False(state.IsOverlayVisible);

        _now = _now.AddSeconds(1);
        state.Evaluate();
        Assert.True(state.IsOverlayVisible);
    }

    [Fact]
    public void ActivityBeforeTheTimeoutRestartsTheCountdown()
    {
        var state = Create(10);

        _now = _now.AddMinutes(9);
        Assert.False(state.RegisterActivity());

        _now = _now.AddMinutes(9);
        state.Evaluate();
        Assert.False(state.IsOverlayVisible);
    }

    [Fact]
    public void TheWakingInputIsConsumedSoItDoesNotReachTheBlurredScreen()
    {
        var state = Create(10);
        _now = _now.AddMinutes(10);
        state.Evaluate();
        Assert.True(state.IsOverlayVisible);

        // True means "I used this input to wake up, do not deliver it".
        Assert.True(state.RegisterActivity());
        Assert.False(state.IsOverlayVisible);

        // Ordinary input afterwards is delivered normally.
        Assert.False(state.RegisterActivity());
    }

    [Fact]
    public void ZeroMinutesTurnsTheScreenOffCompletely()
    {
        var state = Create(IdleLockPreferenceService.DisabledMinutes);

        Assert.False(state.IsEnabled);

        _now = _now.AddHours(8);
        state.Evaluate();

        Assert.False(state.IsOverlayVisible);
    }

    [Fact]
    public void TurningItOffWhileTheScreenIsUpUncoversImmediately()
    {
        var state = Create(10);
        _now = _now.AddMinutes(10);
        state.Evaluate();
        Assert.True(state.IsOverlayVisible);

        state.ApplyTimeout(IdleLockPreferenceService.DisabledMinutes);

        Assert.False(state.IsOverlayVisible);
    }

    [Fact]
    public void SigningOutNeverLeavesTheNextUserLookingAtABlurredScreen()
    {
        var state = Create(10);
        _now = _now.AddMinutes(10);
        state.Evaluate();
        Assert.True(state.IsOverlayVisible);

        state.Reset();

        Assert.False(state.IsOverlayVisible);
    }

    [Fact]
    public void DismissalRunsThroughTheOneMethodAPinWouldGuard()
    {
        var state = Create(10);
        _now = _now.AddMinutes(10);
        state.Evaluate();

        // No challenge today, so the seam is open and TryDismiss always succeeds.
        Assert.False(state.RequiresUnlockChallenge);
        Assert.True(state.TryDismiss());
        Assert.False(state.IsOverlayVisible);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(100000, IdleLockPreferenceService.MaximumMinutes)]
    public void StoredValuesAreClampedToASupportedRange(int stored, int expected) =>
        Assert.Equal(expected, IdleLockPreferenceService.Normalize(stored));

    [Fact]
    public void EvaluateDoesNotRestartTheOverlayThatIsAlreadyShowing()
    {
        var state = Create(10);
        _now = _now.AddMinutes(10);
        state.Evaluate();

        var changes = 0;
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IdleSessionState.IsOverlayVisible))
                changes++;
        };

        _now = _now.AddMinutes(30);
        state.Evaluate();
        state.Evaluate();

        Assert.Equal(0, changes);
        Assert.True(state.IsOverlayVisible);
    }

    // The suggested-follow-up row was fully built and simply never surfaced. This ties
    // the overlay's binding string to the members it names, so a rename cannot leave
    // ShellWindow bound to a property that no longer exists.
    [Fact]
    public void TheShellBindingPathNamesRealMembers()
    {
        var idle = typeof(Sati.ViewModels.ShellViewModel).GetProperty("Idle");
        Assert.NotNull(idle);
        Assert.Equal(typeof(IdleSessionState), idle!.PropertyType);

        var visible = idle.PropertyType.GetProperty("IsOverlayVisible");
        Assert.NotNull(visible);
        Assert.Equal(typeof(bool), visible!.PropertyType);

        var root = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(IdleSessionStateTests).Assembly.Location)!,
            "..", "..", "..", "..", ".."));
        var shell = File.ReadAllText(Path.Combine(root, "Views", "ShellWindow.xaml"));
        Assert.Contains($"Binding {idle.Name}.{visible.Name}", shell);
    }
}
