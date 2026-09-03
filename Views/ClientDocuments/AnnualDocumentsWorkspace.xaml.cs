using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sati.Contracts.V1;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;
public partial class AnnualDocumentsWorkspace : UserControl
{
    public AnnualDocumentsWorkspace() => InitializeComponent();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AnnualDocumentsViewModel old) { old.FileReady -= Save; old.ChooseVerificationFileAsync = null; }
        if (e.NewValue is AnnualDocumentsViewModel current) { current.FileReady += Save; current.ChooseVerificationFileAsync = Choose; }
    }
    private async void Save(AgencyReleaseResult result)
    {
        if (!result.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        { await PdfFileSaver.SaveAsync("Save privacy notice", result.FileName, result.Pdf, "Privacy notice saved."); return; }
        var dialog = new SaveFileDialog { Title = "Save annual documents", FileName = result.FileName, DefaultExt = ".zip",
            Filter = "ZIP archives (*.zip)|*.zip", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        try { await File.WriteAllBytesAsync(dialog.FileName, result.Pdf); }
        catch (Exception) { MessageBox.Show("The packet could not be saved. Choose a writable location and try again."); }
    }
    private async Task<(string Hash, long Length)?> Choose()
    {
        var dialog = new OpenFileDialog { Title = "Verify a saved document", Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return null;
        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            var size = stream.Length; var hash = await SHA256.HashDataAsync(stream);
            return (Convert.ToHexString(hash), size);
        }
        catch (Exception) { MessageBox.Show("The file could not be read."); return null; }
    }
}
