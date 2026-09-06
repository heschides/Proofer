using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Sati.Services;
using Sati.ViewModels.Children;

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
        private void ScheduledWorkList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // A Start button is already a single-click action. Letting its second
            // click bubble into the row gesture could ask to open the same item
            // twice while the first navigation is still in flight.
            if (FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;

            if (sender is not ListBox list ||
                ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject)
                    is not ListBoxItem container ||
                container.DataContext is not WorkAgendaItem item ||
                DataContext is not ScratchpadViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenScheduledWorkCommand.CanExecute(item))
                viewModel.OpenScheduledWorkCommand.Execute(item);
            e.Handled = true;
        }

        private static T? FindVisualAncestor<T>(DependencyObject? source)
            where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match)
                    return match;
                source = source switch
                {
                    Visual or Visual3D => VisualTreeHelper.GetParent(source),
                    FrameworkContentElement content => content.Parent,
                    _ => LogicalTreeHelper.GetParent(source)
                };
            }

            return null;
        }

        private void ScheduledWorkList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter ||
                sender is not ListBox { SelectedItem: WorkAgendaItem item } ||
                DataContext is not ScratchpadViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenScheduledWorkCommand.CanExecute(item))
                viewModel.OpenScheduledWorkCommand.Execute(item);
            e.Handled = true;
        }
    }
}
