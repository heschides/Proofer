using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class ScratchpadView : UserControl
    {
        public ScratchpadView()
        {
            InitializeComponent();
        }

        private void AgendaBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var timestamp = DateTime.Now.ToString("h:mm tt");
                var divider = $"\n\n ─── {timestamp} ───────────────────\n\n";

                var box = (TextBox)sender;
                var caretIndex = box.CaretIndex;
                box.Text = box.Text.Insert(caretIndex, divider);
                box.CaretIndex = caretIndex + divider.Length;

                e.Handled = true;
            }
        }
    }
}
