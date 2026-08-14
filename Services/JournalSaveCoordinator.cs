namespace Sati.Services;

internal readonly record struct JournalSaveResult(bool Succeeded, Exception? Error);

/// <summary>
/// Serializes journal writes. A timer autosave and an account-switch flush may be
/// requested together, but the API's optimistic revision check requires them to
/// reach the server one at a time.
/// </summary>
internal sealed class JournalSaveCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<JournalSaveResult> TrySaveAsync(Func<Task> save)
    {
        await _gate.WaitAsync();
        try
        {
            await save();
            return new JournalSaveResult(true, null);
        }
        catch (Exception ex)
        {
            return new JournalSaveResult(false, ex);
        }
        finally
        {
            _gate.Release();
        }
    }
}
