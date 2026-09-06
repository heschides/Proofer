using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sati.Contracts.V1;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;

public partial class SignatureRequestsWorkspace : UserControl
{
    public SignatureRequestsWorkspace() => InitializeComponent();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SignatureRequestsViewModel old)
        { old.SetActive(false); old.FileReady -= Save; old.ClearSensitiveInputs -= ClearPins; old.ChooseFreezePdfAsync = null; }
        ClearPins();
        if (e.NewValue is SignatureRequestsViewModel current)
        { current.FileReady += Save; current.ClearSensitiveInputs += ClearPins; current.ChooseFreezePdfAsync = ChoosePdf; current.SetActive(IsLoaded); }
    }
    private void OnLoaded(object sender, RoutedEventArgs e) { if (DataContext is SignatureRequestsViewModel vm) vm.SetActive(true); }
    private void OnUnloaded(object sender, RoutedEventArgs e) { if (DataContext is SignatureRequestsViewModel vm) vm.SetActive(false); ClearPins(); }
    private void ClearPins() { PinBox?.Clear(); ConfirmPinBox?.Clear(); }
    private async void CreateRequest(object sender, RoutedEventArgs e) => await Submit(false);
    private async void ReplaceRequest(object sender, RoutedEventArgs e) => await Submit(true);
    private async Task Submit(bool replace)
    {
        try { if (DataContext is SignatureRequestsViewModel vm) await vm.SubmitAsync(PinBox.Password, ConfirmPinBox.Password, replace); }
        finally { ClearPins(); }
    }
    private async Task<byte[]?> ChoosePdf()
    {
        var dialog = new OpenFileDialog { Title = "Choose the exact saved PDF to retain for signing", Filter = "PDF documents (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() != true) return null;
        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            if (stream.Length is <= 0 or > SignatureRules.MaximumPdfBytes) { MessageBox.Show("Choose a PDF no larger than 15 MB."); return null; }
            var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes); return bytes;
        }
        catch (Exception) { MessageBox.Show("The PDF could not be read."); return null; }
    }
    private async void Save(AgencyReleaseResult file)
    {
        try { await PdfFileSaver.SaveAsync("Save retained signature document", file.FileName, file.Pdf, "Retained signature document saved."); }
        finally { Array.Clear(file.Pdf); }
    }
}
