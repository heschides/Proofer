using Sati.ViewModels.ClientDocuments;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views.ClientDocuments;

public partial class DhhsFormsWorkspace : UserControl
{
    private DhhsFormsViewModel? _viewModel;

    public DhhsFormsWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PdfReady -= SavePdf;
            _viewModel.Problem -= ShowProblem;
        }

        _viewModel = e.NewValue as DhhsFormsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PdfReady += SavePdf;
            _viewModel.Problem += ShowProblem;
        }
    }

    private async void SavePdf(object? sender, DhhsPdfReadyEventArgs e) =>
        await PdfFileSaver.SaveAsync(
            "Save official DHHS form",
            e.SuggestedFileName,
            e.Content,
            "The official DHHS form was saved. Store and transmit it only through agency-approved protected locations.");

    private void ShowProblem(object? sender, DhhsProblemEventArgs e) =>
        MessageBox.Show(
            Window.GetWindow(this),
            e.Message,
            e.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private async void SaveSsn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        try
        {
            await _viewModel.SaveSsnAsync(SsnPasswordBox.Password);
        }
        finally
        {
            // PasswordBox is intentionally not bound. Remove plaintext from the UI
            // immediately after the single explicit send attempt.
            SsnPasswordBox.Clear();
        }
    }

    private async void ClearSsn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            "Remove the encrypted SSN for this consumer? The generated Appointment form will leave that box blank.",
            "Remove Encrypted SSN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            await _viewModel.ClearSsnAsync();
    }
}
