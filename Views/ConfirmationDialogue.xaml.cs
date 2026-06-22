using System.Windows;
using System.Windows.Media;

namespace Sati.Views
{
    // Reusable yes/no confirmation. Returns true ONLY on an explicit click of the
    // action button. Three deliberate safety choices: Cancel is IsCancel, so Esc and
    // the Cancel click both return false; neither button is IsDefault, so pressing
    // Enter does nothing — a destructive action can't be confirmed by reflex; and
    // FocusManager parks initial focus on Cancel. Construct, set Owner, ShowDialog().
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(string title, string message, string confirmText, bool isDestructive = false)
        {
            InitializeComponent();
            HeadingText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;

            // One style, two moods: deep red (#7A3B3B) for destructive, warm accent
            // (#C87941) otherwise. Set in code because the dialog is built in code anyway.
            ConfirmButton.Background = isDestructive
                ? new SolidColorBrush(Color.FromRgb(0x7A, 0x3B, 0x3B))
                : new SolidColorBrush(Color.FromRgb(0xC8, 0x79, 0x41));
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
