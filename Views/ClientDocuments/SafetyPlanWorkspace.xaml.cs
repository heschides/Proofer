using System.Windows;
using System.Windows.Controls;
using Sati.Contracts.V1;
using Sati.ViewModels.ClientDocuments;

namespace Sati.Views.ClientDocuments;
public partial class SafetyPlanWorkspace : UserControl
{
    public SafetyPlanWorkspace() => InitializeComponent();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SafetyPlanViewModel old) old.PdfReady -= Save;
        if (e.NewValue is SafetyPlanViewModel current) current.PdfReady += Save;
    }
    private async void Save(AgencyReleaseResult pdf) =>
        await PdfFileSaver.SaveAsync("Save safety plan", pdf.FileName, pdf.Pdf, "Safety plan saved.");
}
