using Sati.ViewModels.ClientDocuments;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views.ClientDocuments;

public partial class AgencyReleaseWorkspace : UserControl
{
    private AgencyReleaseViewModel? _viewModel;

    public AgencyReleaseWorkspace() => InitializeComponent();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PdfReady -= SavePdf;
            _viewModel.Problem -= ShowProblem;
            _viewModel.AttestationRequested -= ConfirmAttestation;
        }

        _viewModel = e.NewValue as AgencyReleaseViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PdfReady += SavePdf;
            _viewModel.Problem += ShowProblem;
            _viewModel.AttestationRequested += ConfirmAttestation;
        }
    }

    private async void SavePdf(object? sender, AgencyReleasePdfReadyEventArgs e) =>
        await PdfFileSaver.SaveAsync(
            "Save agency release",
            e.SuggestedFileName,
            e.Content,
            "The agency release was saved. Store and transmit it only through agency-approved protected locations.");

    private void ShowProblem(object? sender, AgencyReleaseProblemEventArgs e) =>
        MessageBox.Show(Window.GetWindow(this), e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool ConfirmAttestation(AgencyReleaseAttestationEventArgs e) =>
        MessageBox.Show(
            Window.GetWindow(this),
            $"{e.Statement}\n\n{e.ScopeNotice}\n\nGenerate the release with this staff attestation?",
            "Confirm Staff Attestation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
