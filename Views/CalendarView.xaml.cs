using System;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for CalendarView.xaml
    /// </summary>
    public partial class CalendarView : UserControl
    {
        public static readonly DependencyProperty MonthColumnCountProperty =
            DependencyProperty.Register(
                nameof(MonthColumnCount),
                typeof(int),
                typeof(CalendarView),
                new PropertyMetadata(4));

        public int MonthColumnCount
        {
            get => (int)GetValue(MonthColumnCountProperty);
            private set => SetValue(MonthColumnCountProperty, value);
        }

        public CalendarView()
        {
            InitializeComponent();
            SizeChanged += CalendarView_SizeChanged;
        }

        private void CalendarView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The detail rail always uses 220 px. Choose a month count from the
            // space that remains so dates and note badges never have to overlap.
            var yearOverviewWidth = Math.Max(0, e.NewSize.Width - 220);
            MonthColumnCount = yearOverviewWidth switch
            {
                >= 1120 => 4,
                >= 780 => 3,
                >= 500 => 2,
                _ => 1
            };
        }
    }
}
