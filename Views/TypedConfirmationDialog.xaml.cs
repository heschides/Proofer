using System.Windows;

namespace Sati.Views
{
    // Destructive-action confirmation that requires typing an exact value rather than a single
    // click. Standard practice for the most consequential actions (rule-3 consumer deletion),
    // and it defeats the muscle memory a click-through ConfirmationDialog builds up. Confirm is
    // disabled until the typed text matches requiredText exactly (ordinal, case-sensitive) —
    // confirmation is evidence of intent, not a security control by itself (CLAUDE.md: UI
    // visibility is not security; the real gates are server/service-side).
    public partial class TypedConfirmationDialog : Window
    {
        private readonly string _requiredText;

        public TypedConfirmationDialog(
            string title, string message, string prompt, string requiredText, string confirmText)
        {
            InitializeComponent();
            _requiredText = requiredText;
            HeadingText.Text = title;
            MessageText.Text = message;
            PromptText.Text = prompt;
            ConfirmButton.Content = confirmText;
        }

        private void ConfirmationTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
            ConfirmButton.IsEnabled = string.Equals(
                ConfirmationTextBox.Text, _requiredText, StringComparison.Ordinal);

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
