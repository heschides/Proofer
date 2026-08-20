using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;

namespace Sati.ViewModels.Children;

/// <summary>
/// The one owner of how a consumer's Social Security number is displayed, stored,
/// revealed, and cleared.
///
/// It exists because two screens need it — the consumer profile, where a case manager
/// naturally records demographics, and the DHHS forms workspace, where the Appointment
/// form asks for it. A second implementation of "how do we show and store an SSN"
/// would be exactly the duplication CLAUDE.md forbids, and the two copies would drift
/// on the details that matter: whether plaintext is ever held, when it is cleared, and
/// what the environment is allowed to do.
///
/// The number is never held in a bound property except while deliberately revealed,
/// and any change of consumer clears it. A revealed SSN sitting in a view model after
/// the case manager has moved on is a screen-sharing accident waiting to happen.
/// </summary>
public partial class SsnPanelViewModel(IDhhsFormService formService) : ObservableObject
{
    // Selection-driven loads race: click three consumers quickly and the slowest
    // response would otherwise publish its mask over the newest one's.
    private readonly LatestRequestTracker _loads = new();
    private int? _personId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerson))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    private bool hasLoadedPerson;

    [ObservableProperty]
    private string masked = SsnMask.NotOnFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    private bool isOnFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    /// <summary>
    /// The plaintext, only while deliberately shown. Empty at every other moment,
    /// including immediately after the consumer changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRevealed))]
    private string revealed = string.Empty;

    /// <summary>What the case manager typed. Cleared as soon as it has been stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string entry = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    private bool isBusy;

    public bool HasPerson => HasLoadedPerson;
    public bool SupportsStorage => formService.SupportsSsnStorage;
    public bool SupportsReveal => formService.SupportsSsnReveal;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsRevealed => !string.IsNullOrEmpty(Revealed);
    public bool CanSave => HasPerson && SupportsStorage && !IsBusy && !string.IsNullOrWhiteSpace(Entry);
    public bool CanReveal => HasPerson && SupportsReveal && IsOnFile && !IsBusy;

    /// <summary>
    /// Says what this environment does with the number, because the two paths differ
    /// in ways a case manager would otherwise discover by surprise.
    /// </summary>
    public string Explanation => SupportsReveal
        ? "Stored encrypted with this Windows account's key. It can be read back here, and it will not open on another computer or under another Windows login."
        : SupportsStorage
            ? "Stored encrypted by the server. Sati can show the mask and print the number on an official form, but never reads it back to this workstation."
            : "This environment does not store Social Security numbers. The box stays blank on the generated form for hand-completion.";

    /// <summary>
    /// Moves the panel to a consumer, or to nothing. Always clears any revealed
    /// number and any half-typed entry first — neither belongs to the next consumer.
    /// </summary>
    public void SetPerson(int? personId)
    {
        var request = _loads.Begin();

        Revealed = string.Empty;
        Entry = string.Empty;
        Masked = SsnMask.NotOnFile;
        IsOnFile = false;
        _personId = personId;
        HasLoadedPerson = personId.HasValue;
        StatusMessage = string.Empty;

        if (personId is int id && SupportsStorage)
            _ = LoadAsync(id, request);
    }

    private async Task LoadAsync(int personId, int request)
    {
        try
        {
            var status = await formService.GetSsnStatusAsync(personId);
            if (!_loads.IsCurrent(request))
                return;

            Masked = status.Masked;
            IsOnFile = status.IsOnFile;
        }
        catch (Exception failure)
        {
            if (_loads.IsCurrent(request))
                StatusMessage = $"The number on file could not be read. {failure.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave || _personId is not int personId)
            return;

        // Shape-checked before it travels, so an obvious slip is caught while the
        // case manager still has the source in front of them.
        var normalized = SsnMask.Normalize(Entry);
        if (!SsnMask.IsWellFormed(normalized))
        {
            StatusMessage = "That is not a valid nine-digit Social Security number.";
            return;
        }

        await RunAsync(async () =>
        {
            var status = await formService.UpdateSsnAsync(personId, normalized);
            Masked = status.Masked;
            IsOnFile = status.IsOnFile;
            // Never left sitting in a bound control after it has been stored.
            Entry = string.Empty;
            Revealed = string.Empty;
            StatusMessage = "Saved.";
        });
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_personId is not int personId || !SupportsStorage || IsBusy)
            return;

        await RunAsync(async () =>
        {
            var status = await formService.UpdateSsnAsync(personId, null);
            Masked = status.Masked;
            IsOnFile = status.IsOnFile;
            Entry = string.Empty;
            Revealed = string.Empty;
            StatusMessage = "Removed.";
        });
    }

    /// <summary>
    /// Shows the number, for reading to the Social Security Administration or
    /// transcribing. Recorded as a disclosure by the service that produced it.
    /// </summary>
    [RelayCommand]
    private async Task RevealAsync()
    {
        if (!CanReveal || _personId is not int personId)
            return;

        await RunAsync(async () =>
        {
            Revealed = await formService.RevealSsnAsync(personId);
            StatusMessage = "Shown. Hide it when you are finished.";
        });
    }

    [RelayCommand]
    private void Hide()
    {
        Revealed = string.Empty;
        StatusMessage = string.Empty;
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception failure)
        {
            // The message is the point: a number that cannot be read here because the
            // database moved says exactly that, rather than surfacing as a bare error.
            StatusMessage = failure.Message;
            Revealed = string.Empty;
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
            RevealCommand.NotifyCanExecuteChanged();
        }
    }
}
