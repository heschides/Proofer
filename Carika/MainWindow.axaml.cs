using Avalonia.Controls;
using Carika.ViewModels;

namespace Carika;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(this);
    }
}
