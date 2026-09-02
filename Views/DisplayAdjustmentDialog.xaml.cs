using Sati.Services;
using System.Windows;

namespace Sati.Views;

public partial class DisplayAdjustmentDialog : Window
{
    public string Notice { get; }

    public DisplayAdjustmentDialog(DisplayLayoutProfile profile)
    {
        InitializeComponent();
        Notice =
            $"Sati detected a {profile.PixelWidth:N0} × {profile.PixelHeight:N0} display, " +
            "which is smaller than 1080p in at least one direction. Sati will use its " +
            "compact display mode for this non-standard format. Optional panels will start " +
            "collapsed, spacing will be reduced, and scrollbars will appear when content " +
            "does not fit. You can reopen the collapsed panels at any time.\n\n" +
            "For the best experience, a 1920 × 1080 (1080p) display or higher is recommended.";
        DataContext = this;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => Close();
}
