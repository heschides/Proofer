using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// Distribution list for a supervisor's own caseload. No code-behind logic: selection
    /// changes reach the view model through the row's own SelectionChanged event rather than
    /// a Checked handler here.
    /// </summary>
    public partial class CaseloadDistributionView : UserControl
    {
        public CaseloadDistributionView()
        {
            InitializeComponent();
        }
    }
}
