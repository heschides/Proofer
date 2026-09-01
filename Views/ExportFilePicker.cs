using Microsoft.Win32;
using Sati.Services;

namespace Sati.Views
{
    /// <summary>
    /// The file dialog behind <see cref="IExportFilePicker"/>.
    ///
    /// <para>
    /// The filter offers HTML because that is the only supported input: a printed PDF loses which
    /// value belongs to which field. Picking a PDF anyway is not prevented here — the reader
    /// refuses it by magic bytes and says why, which is the check that also catches a PDF renamed
    /// to .html.
    /// </para>
    /// </summary>
    internal sealed class ExportFilePicker : IExportFilePicker
    {
        public string? PickExportFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a saved Credible client print view",
                Filter = "Saved web pages (*.htm;*.html)|*.htm;*.html|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
