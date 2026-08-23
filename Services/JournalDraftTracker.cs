namespace Sati.Services;

/// <summary>
/// Remembers the last journal text confirmed by persistence for the currently
/// displayed person. Shutdown and selection-change flushes consult this baseline
/// so merely viewing a journal never creates a cloud write.
/// </summary>
internal sealed class JournalDraftTracker
{
    private int? _personId;
    private string _savedContent = string.Empty;

    public void Load(int personId, string? savedContent)
    {
        _personId = personId;
        _savedContent = Normalize(savedContent);
    }

    public void Clear()
    {
        _personId = null;
        _savedContent = string.Empty;
    }

    public bool IsDirty(int personId, string? currentContent) =>
        _personId == personId && Normalize(currentContent) != _savedContent;

    public void MarkSaved(int personId, string? savedContent)
    {
        if (_personId == personId)
            _savedContent = Normalize(savedContent);
    }

    private static string Normalize(string? content) => content ?? string.Empty;
}
