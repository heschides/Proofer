using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Sati.Views
{
    /// <summary>
    /// Offers generated PDF bytes to the user through a save dialog.
    ///
    /// Shared because two places now hand out the same document: the AT request
    /// editor after publishing, and the AT requests list on a client's profile
    /// when regenerating a filed one. Duplicating the dialog would let the two
    /// drift into behaving differently for what is, to the user, one action.
    /// </summary>
    internal static class PdfFileSaver
    {
        public static async Task SaveAsync(string title, string suggestedFileName, byte[] content, string savedMessage)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                FileName = suggestedFileName,
                DefaultExt = ".pdf",
                Filter = "PDF documents (*.pdf)|*.pdf",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                await File.WriteAllBytesAsync(dialog.FileName, content);
                MessageBox.Show(savedMessage, "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The PDF could not be saved.\n\n{ex.Message}",
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
