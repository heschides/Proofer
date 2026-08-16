using Sati.ViewModels;
using Sati.ViewModels.Children;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class ClientsView : UserControl
    {
        public ClientsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private NewClientViewModel? _viewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Detach from the outgoing view model before wiring the incoming one,
            // or a re-hosted view accumulates handlers and a single regenerate
            // opens as many save dialogs as the view has been re-bound.
            if (_viewModel is not null)
            {
                _viewModel.AtRequestPdfReady -= SaveAtRequestPdf;
                _viewModel.AtRequestProblem -= ShowAtRequestProblem;
                _viewModel = null;
            }

            if (e.NewValue is NewClientViewModel vm)
            {
                _viewModel = vm;
                vm.AtRequestPdfReady += SaveAtRequestPdf;
                vm.AtRequestProblem += ShowAtRequestProblem;

                vm.ComplianceReviewRequested += (forms) =>
                {
                    var reviewVm = new ComplianceReviewViewModel(forms)
                    {
                        ClientName = $"{vm.FirstName} {vm.LastName}"
                    };

                    var dialog = new ComplianceReviewWindow(reviewVm)
                    {
                        Owner = Application.Current.MainWindow
                    };

                    var confirmed = dialog.ShowDialog() == true;
                    if (confirmed)
                        reviewVm.Commit();

                    return confirmed;
                };
            }
        }

        // Regenerated from the stored record by the view model; the view only owns
        // the file dialog. Same helper the AT request editor uses, so saving a
        // freshly published request and re-saving a filed one behave identically.
        private async void SaveAtRequestPdf(object? sender, ATRequestPdfReadyEventArgs e) =>
            await PdfFileSaver.SaveAsync(
                "Save AT request", e.SuggestedFileName, e.Content, "The AT request was saved.");

        private void ShowAtRequestProblem(object? sender, ATRequestProblemEventArgs e) =>
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Warning);

        private void ClientList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NewClientViewModel vm && vm.SelectedPerson is Person person)
            {
                vm.LoadPersonForEdit(person);
                vm.IsEditMode = true;
            }
        }
    }
}
