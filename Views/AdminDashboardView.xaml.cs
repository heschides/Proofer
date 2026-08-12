using Microsoft.Win32;
using Sati.ViewModels.Admin;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views;

public partial class AdminDashboardView : UserControl
{
    public AdminDashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdminDashboardViewModel viewModel)
            return;
        viewModel.PdfReady -= SavePdf;
        viewModel.PdfReady += SavePdf;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AdminDashboardViewModel oldViewModel)
            oldViewModel.PdfReady -= SavePdf;
        if (e.NewValue is AdminDashboardViewModel newViewModel)
            newViewModel.PdfReady += SavePdf;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminDashboardViewModel viewModel)
            viewModel.PdfReady -= SavePdf;
    }

    private async void SavePdf(object? sender, AdminPdfReadyEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Person lifecycle audit",
            FileName = e.SuggestedFileName,
            DefaultExt = ".pdf",
            Filter = "PDF documents (*.pdf)|*.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, e.Content);
            MessageBox.Show(
                "The Person lifecycle audit was saved.",
                "Audit PDF Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
