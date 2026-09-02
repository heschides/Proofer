using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// The Credible import review panel. No code-behind logic — acceptance is a two-way binding
    /// and the commands live on the view model.
    /// </summary>
    public partial class ConsumerImportView : UserControl
    {
        public ConsumerImportView()
        {
            InitializeComponent();
        }
    }
}
