using Sati.ViewModels;
using System.Windows;
using System.Windows.Threading;


namespace Sati
{
    public partial class ScratchpadHistoryWindow : Window
    {
        private readonly ScratchpadHistoryViewModel _viewModel;
        private bool _isRestoringCalendarSelection;

        public ScratchpadHistoryWindow(ScratchpadHistoryViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            DataContext = vm;
            _viewModel.CommentSaved += ViewModel_CommentSaved;
        }

        public async Task InitializeAsync()
        {
            await _viewModel.InitializeAsync();
            HistoryCalendar.DisplayDate = _viewModel.InitialDisplayDate;
            HistoryCalendar.SelectedDate = _viewModel.InitialSelectedDate;
        }

        private void HistoryCalendar_SelectedDatesChanged(
            object? sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isRestoringCalendarSelection)
                return;

            // A WPF Calendar in SingleRange mode can briefly clear SelectedDates
            // when mouse/focus moves from the selected range to another control
            // (notably the results ScrollViewer's scrollbar). Do not let that
            // transient state erase the user's range. Defer the check so a real
            // date change has time to add its new selection first.
            if (HistoryCalendar.SelectedDates.Count == 0 &&
                _viewModel.SelectionStart is DateTime selectionStart &&
                _viewModel.SelectionEnd is DateTime selectionEnd)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (HistoryCalendar.SelectedDates.Count > 0)
                        return;

                    _isRestoringCalendarSelection = true;
                    try
                    {
                        HistoryCalendar.SelectedDates.AddRange(selectionStart, selectionEnd);
                    }
                    finally
                    {
                        _isRestoringCalendarSelection = false;
                    }
                }, DispatcherPriority.Input);
                return;
            }

            _viewModel.SetSelectedDates(HistoryCalendar.SelectedDates);
        }

        private void ViewModel_CommentSaved(object? sender, EventArgs e)
        {
            _viewModel.CommentSaved -= ViewModel_CommentSaved;
            Close();
        }
    }
}
