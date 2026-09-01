using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sati.Views;

public partial class DailyAgendaWindow : Window
{
    private DailyAgendaViewModel? _viewModel;

    public DailyAgendaWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FindVisualChild<CheckBox>(this)?.Focus();
        DataContextChanged += (_, args) => Attach(args.NewValue as DailyAgendaViewModel);
    }

    private void Attach(DailyAgendaViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }
}
