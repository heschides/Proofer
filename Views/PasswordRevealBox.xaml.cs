using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// A password field with a reveal toggle.
    ///
    /// WPF cannot do this with one control: <see cref="PasswordBox"/> renders its
    /// own masked text internally and has no plain-text mode, so showing the
    /// characters means overlaying a real <see cref="TextBox"/> and swapping which
    /// one is visible.
    ///
    /// THE MASKED BOX IS ALWAYS THE SOURCE OF TRUTH. While revealed, edits in the
    /// plain-text box are written straight back into it. That is deliberate: every
    /// screen using this control reads <see cref="SecurePassword"/> and listens to
    /// <see cref="PasswordChanged"/>, and both keep coming from the PasswordBox
    /// exactly as they did before the toggle existed. Nothing downstream has to
    /// know the field can be revealed.
    ///
    /// ON SECURITY. Sati handles passwords as <see cref="SecureString"/>, and
    /// revealing one necessarily materialises it as an ordinary managed string in
    /// the TextBox for as long as it is shown — a string the runtime may copy and
    /// will not zero on demand. That is the unavoidable cost of the feature, and
    /// it is the same trade every browser and operating system makes for the same
    /// affordance. The exposure is bounded: the plain-text box is cleared the
    /// moment the field is hidden or cleared, and the SecureString path is
    /// untouched. What must NOT follow is a revealed password reaching a log, a
    /// crash dump payload, or an automation property — see the logging rule in
    /// CLAUDE.md.
    /// </summary>
    public partial class PasswordRevealBox : UserControl
    {
        // Guards the two-way copy between the masked and plain-text boxes. Without
        // it, writing one raises the other's change event and the two chase each
        // other.
        private bool _syncing;

        public PasswordRevealBox()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Raised whenever the password changes, from either box. Mirrors
        /// <see cref="PasswordBox.PasswordChanged"/> so call sites keep the same
        /// handler shape.
        /// </summary>
        public event RoutedEventHandler? PasswordChanged;

        /// <summary>
        /// The entered password. Always read from the masked box, which is kept
        /// current whichever field is visible.
        /// </summary>
        public SecureString SecurePassword => Masked.SecurePassword;

        /// <summary>True while the characters are visible.</summary>
        public bool IsRevealed => RevealToggle.IsChecked == true;

        /// <summary>
        /// Empties the field and returns it to masked. Re-masking on clear is the
        /// point: a cleared field is usually a failed sign-in or a window being
        /// reused, and the next person to type should not find the box already
        /// showing what they type.
        /// </summary>
        public void Clear()
        {
            _syncing = true;
            Masked.Clear();
            Revealed.Clear();
            _syncing = false;

            RevealToggle.IsChecked = false;
            PasswordChanged?.Invoke(this, new RoutedEventArgs());
        }

        /// <summary>
        /// Moves keyboard focus into the entry field rather than to the control
        /// itself, so callers that focus this on open put the caret where the user
        /// is about to type.
        /// </summary>
        public new bool Focus() => IsRevealed ? Revealed.Focus() : Masked.Focus();

        private void OnMaskedPasswordChanged(object sender, RoutedEventArgs e)
        {
            // Keep the visible plain-text copy current when something outside this
            // control changes the password while it is revealed.
            if (!_syncing && IsRevealed && Revealed.Text != Masked.Password)
            {
                _syncing = true;
                Revealed.Text = Masked.Password;
                _syncing = false;
            }

            PasswordChanged?.Invoke(this, e);
        }

        private void OnRevealedTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing)
                return;

            // Write back to the source of truth, which raises PasswordChanged for
            // us through OnMaskedPasswordChanged.
            _syncing = true;
            Masked.Password = Revealed.Text;
            _syncing = false;

            PasswordChanged?.Invoke(this, e);
        }

        private void OnRevealToggled(object sender, RoutedEventArgs e)
        {
            var revealed = IsRevealed;

            _syncing = true;
            if (revealed)
            {
                Revealed.Text = Masked.Password;
            }
            else
            {
                // Drop the plain-text copy as soon as it stops being needed rather
                // than leaving it sitting in a TextBox behind a Collapsed flag.
                Masked.Password = Revealed.Text;
                Revealed.Clear();
            }
            _syncing = false;

            Masked.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
            Revealed.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;

            // The button's own label has to describe what pressing it will DO, and
            // it changes meaning each time.
            var action = revealed ? "Hide password" : "Show password";
            AutomationProperties.SetName(RevealToggle, action);
            RevealToggle.ToolTip = action;

            // Focus follows the visible control, and the caret goes to the end so
            // toggling mid-entry does not drop the user back to position zero.
            if (revealed)
            {
                Revealed.Focus();
                Revealed.CaretIndex = Revealed.Text.Length;
            }
            else
            {
                Masked.Focus();
            }
        }
    }
}
