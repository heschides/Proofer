namespace Sati.Services;

/// <summary>
/// Asks the user to choose a saved Credible export.
///
/// <para>
/// An interface rather than a dialog call inside the view model, so the import flow stays
/// testable and unaware of Views. The implementation lives in the view layer.
/// </para>
/// </summary>
public interface IExportFilePicker
{
    /// <summary>The chosen file, or null if the user cancelled.</summary>
    string? PickExportFile();

    /// <summary>The chosen folder of saved exports, or null if the user cancelled.</summary>
    string? PickExportFolder();
}
