using System.Windows;

namespace Sati.Views;

public partial class IncorrectPasswordDialog : Window
{
    public IncorrectPasswordDialog() => InitializeComponent();

    private void TryAgain_Click(object sender, RoutedEventArgs e) => Close();
}
