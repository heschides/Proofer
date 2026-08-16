using System.Windows.Controls;

namespace Sati.Views
{
    public partial class StatisticsView : UserControl
    {
        public StatisticsView()
        {
            InitializeComponent();

            // StatisticsViewModel is a singleton, but its view is built by a
            // DataTemplate, so every navigation creates a NEW PlotView bound to the
            // SAME PlotModel. OxyPlot allows a PlotModel to be attached to only one
            // PlotView and throws InvalidOperationException on the second attach, so
            // this instance must release the model as it goes away. Clearing the
            // binding here is harmless: the DataTemplate discards this view rather
            // than reusing it. MonthlyProductivityView and TeamOverviewView do the
            // same for the same reason.
            Unloaded += (_, _) => UnitsChart.Model = null;
        }
    }
}
