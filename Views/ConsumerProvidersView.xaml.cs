using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// The consumer's medical provider list. Its DataContext is a
    /// <see cref="ViewModels.Children.ConsumerProvidersViewModel"/>, supplied by whatever
    /// hosts it — the consumer profile today.
    /// </summary>
    public partial class ConsumerProvidersView : UserControl
    {
        public ConsumerProvidersView()
        {
            InitializeComponent();
        }
    }
}
