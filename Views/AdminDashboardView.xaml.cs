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
        viewModel.CsvReady -= SaveCsv;
        viewModel.CsvReady += SaveCsv;
        viewModel.TestConsumerDeletionConfirmationRequested -= ConfirmTestConsumerDeletion;
        viewModel.TestConsumerDeletionConfirmationRequested += ConfirmTestConsumerDeletion;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AdminDashboardViewModel oldViewModel)
        {
            oldViewModel.PdfReady -= SavePdf;
            oldViewModel.CsvReady -= SaveCsv;
            oldViewModel.TestConsumerDeletionConfirmationRequested -= ConfirmTestConsumerDeletion;
        }
        if (e.NewValue is AdminDashboardViewModel newViewModel)
        {
            newViewModel.PdfReady += SavePdf;
            newViewModel.CsvReady += SaveCsv;
            newViewModel.TestConsumerDeletionConfirmationRequested += ConfirmTestConsumerDeletion;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminDashboardViewModel viewModel)
        {
            viewModel.PdfReady -= SavePdf;
            viewModel.CsvReady -= SaveCsv;
            viewModel.TestConsumerDeletionConfirmationRequested -= ConfirmTestConsumerDeletion;
        }
    }

    private void ConfirmTestConsumerDeletion(
        object? sender,
        AdminTestConsumerDeletionConfirmationEventArgs e)
    {
        var displayName = string.IsNullOrWhiteSpace(e.DisplayName)
            ? $"consumer #{e.PersonId}"
            : e.DisplayName;
        var dialog = new ConfirmationDialog(
            $"Delete {displayName}?",
            e.Message,
            "Delete",
            isDestructive: true)
        {
            Owner = Window.GetWindow(this)
        };
        e.Confirmed = dialog.ShowDialog() == true;
    }

    private async void SaveCsv(object? sender, AdminCsvReadyEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save audit activity export",
            FileName = e.SuggestedFileName,
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, e.Content);
            MessageBox.Show(
                "The audit activity export was saved.",
                "Audit Export Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The CSV file could not be saved.\n\n{ex.Message}",
                "Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
