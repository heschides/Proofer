using Sati.Data;
using System.Windows;

namespace Sati.Views;

public partial class DataEnvironmentWindow : Window
{
    public SatiDataEnvironment? SelectedEnvironment { get; private set; }

    public DataEnvironmentWindow()
    {
        InitializeComponent();
    }

    private void ProductionButton_Click(object sender, RoutedEventArgs e) =>
        CompleteSelection(SatiDataEnvironment.Production);

    private void DemoButton_Click(object sender, RoutedEventArgs e) =>
        CompleteSelection(SatiDataEnvironment.Demo);

    private void CompleteSelection(SatiDataEnvironment environment)
    {
        SelectedEnvironment = environment;
        DialogResult = true;
    }
}
