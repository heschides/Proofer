using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Services;

namespace Sati.ViewModels;

/// <summary>
/// Decides when Sati covers the screen after a stretch of no user input, and when
/// it uncovers again. Pure state with an injectable clock, so the rule is tested
/// without a timer, a window, or a wall-clock wait.
///
/// The overlay is a privacy screen, not a security control. It is dismissed by any
/// input today. <see cref="RequiresUnlockChallenge"/> is the seam a PIN would use:
/// a future PIN prompt sets it true, and <see cref="TryDismiss"/> becomes the one
/// place that decides whether the screen actually clears. Everything that wakes the
/// session already routes through that method, so adding the challenge does not mean
/// hunting down callers. Nothing here is a substitute for Windows lock.
/// </summary>
public sealed partial class IdleSessionState : ObservableObject
{
    private readonly Func<DateTimeOffset> _clock;

    public IdleSessionState(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        LastActivityUtc = _clock();
    }

    /// <summary>Minutes of no input before the overlay appears. Zero disables it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private int timeoutMinutes = IdleLockPreferenceService.DefaultMinutes;

    [ObservableProperty]
    private bool isOverlayVisible;

    public bool IsEnabled => TimeoutMinutes > IdleLockPreferenceService.DisabledMinutes;

    public DateTimeOffset LastActivityUtc { get; private set; }

    /// <summary>
    /// False today, so any input clears the overlay. A PIN implementation flips
    /// this and supplies its own prompt; <see cref="TryDismiss"/> is the only
    /// place that has to change.
    /// </summary>
    public bool RequiresUnlockChallenge => false;

    /// <summary>
    /// Records input. Returns true when this input was consumed to wake the
    /// session, which tells the caller not to pass it on to the blurred UI
    /// underneath: the keystroke that wakes Sati should not also type into a
    /// note the user cannot currently read.
    /// </summary>
    public bool RegisterActivity()
    {
        LastActivityUtc = _clock();

        if (!IsOverlayVisible)
            return false;

        return TryDismiss();
    }

    /// <summary>Called on a tick. Raises the overlay once the timeout has elapsed.</summary>
    public void Evaluate()
    {
        if (!IsEnabled || IsOverlayVisible)
            return;

        if (_clock() - LastActivityUtc >= TimeSpan.FromMinutes(TimeoutMinutes))
            IsOverlayVisible = true;
    }

    /// <summary>
    /// The single exit from the overlay. Returns false when a challenge is
    /// required and has not been satisfied.
    /// </summary>
    public bool TryDismiss()
    {
        if (RequiresUnlockChallenge)
            return false;

        IsOverlayVisible = false;
        LastActivityUtc = _clock();
        return true;
    }

    /// <summary>Applies a stored or changed preference. Turning it off uncovers at once.</summary>
    public void ApplyTimeout(int minutes)
    {
        TimeoutMinutes = IdleLockPreferenceService.Normalize(minutes);
        LastActivityUtc = _clock();

        if (!IsEnabled && IsOverlayVisible)
            TryDismiss();
    }

    /// <summary>Signing out must never leave the next user looking at a blurred screen.</summary>
    public void Reset()
    {
        IsOverlayVisible = false;
        LastActivityUtc = _clock();
    }
}
