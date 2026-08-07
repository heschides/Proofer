using CommunityToolkit.Mvvm.ComponentModel;

namespace Sati.ViewModels.Children
{
    // Display wrapper around one ReviewItem. The item's dates have private
    // setters and are only written through ReviewItemService, so this VM holds
    // the entity read-only and calls Refresh() after the service returns an
    // updated copy. Keeps the single-write-path discipline intact through the
    // whole UI layer.
    public partial class ReviewCellViewModel : ObservableObject
    {
        public ReviewItem Item { get; private set; }

        // Invoked when a date is edited in the UI. The parent VM supplies this and
        // routes it through IReviewItemService, so this VM can request a write
        // without being able to perform one — the model's private setters remain
        // reachable only from the service layer.
        private readonly Func<ReviewCellViewModel, ReviewStage, DateTime?, Task> _onDateChanged;

        // Parallel to _onDateChanged, but for the appointment on Medical/Dental
        // cells. Carries both current appointment values (date, provider) so the
        // parent VM upserts-or-removes the single Appointment in one call. Same
        // discipline: this VM requests a write, the service performs it.
        private readonly Func<ReviewCellViewModel, DateTime?, string?, Task> _onAppointmentChanged;

        // Suppresses the persist callback while Refresh() is repopulating from a
        // freshly saved entity. Without it, setting the backing values would fire
        // a save for the change that just came back from the database.
        private bool _isRefreshing;

        public ReviewCellViewModel(ReviewItem item,
                                           Func<ReviewCellViewModel, ReviewStage, DateTime?, Task> onDateChanged,
                                           Func<ReviewCellViewModel, DateTime?, string?, Task> onAppointmentChanged)
        {
            Item = item;
            _onDateChanged = onDateChanged;
            _onAppointmentChanged = onAppointmentChanged;
        }

        public int ReviewItemId => Item.Id;
        public int Quarter => Item.Quarter;
        public ReviewCategory Category => Item.Category;
        public int SlotIndex => Item.SlotIndex;
        public ReviewStage Stage => Item.Stage;

        // Two-way bindable. The getter reads the entity — never a separate copy —
        // so these can't drift from what's stored. The setter compares first and
        // no-ops on an unchanged value, because WPF DatePickers re-set on focus
        // changes and every one of those would otherwise be a database write.
        public DateTime? RequestedDate
        {
            get => Item.RequestedDate;
            set => SetStage(ReviewStage.Requested, value, Item.RequestedDate);
        }

        public DateTime? ReceivedDate
        {
            get => Item.ReceivedDate;
            set => SetStage(ReviewStage.Received, value, Item.ReceivedDate);
        }

        public DateTime? LoggedDate
        {
            get => Item.LoggedDate;
            set => SetStage(ReviewStage.Logged, value, Item.LoggedDate);
        }

        // True only for the two categories that carry an appointment. The detail
        // pane binds the appointment inputs' visibility to this, so Goals,
        // Employment, and the note reviews never show them.
        public bool IsAppointmentCategory =>
            Category is ReviewCategory.Medical or ReviewCategory.Dental;

        // Two-way bindable appointment fields, reading the entity's optional
        // Appointment nav — never a separate copy, so they can't drift from what's
        // stored. Either setter routes BOTH current values through the single
        // callback: the date is the anchor (clearing it removes the appointment),
        // the provider optional. Null nav reads as null/empty — the "none yet" state.
        public DateTime? AppointmentDate
        {
            get => Item.Appointment?.Date;
            set => SetAppointment(value, Item.Appointment?.ProviderName);
        }

        public string? ProviderName
        {
            get => Item.Appointment?.ProviderName;
            set => SetAppointment(Item.Appointment?.Date, value);
        }

        // Mirrors SetStage: no-op while refreshing, no-op when nothing changed (WPF
        // re-sets on focus change), else fire-and-forget the persist. Blank/whitespace
        // provider counts as equal to null, so an empty box never creates an
        // appointment on its own — only a date does.
        private void SetAppointment(DateTime? date, string? provider)
        {
            if (_isRefreshing)
                return;

            var curDate = Item.Appointment?.Date;
            var curProvider = Item.Appointment?.ProviderName;

            var providerUnchanged = string.Equals(
                (provider ?? string.Empty).Trim(),
                curProvider ?? string.Empty,
                StringComparison.Ordinal);

            if (date?.Date == curDate?.Date && providerUnchanged)
                return;

            _ = _onAppointmentChanged(this, date?.Date, provider);
        }

        // Fire-and-forget on purpose: this is a property setter, which can't be
        // async, and WPF has no await point in a binding write. Exceptions inside
        // the callback surface through App's DispatcherUnhandledException handler
        // rather than being swallowed.
        private void SetStage(ReviewStage stage, DateTime? incoming, DateTime? current)
        {
            if (_isRefreshing)
                return;

            if (incoming?.Date == current?.Date)
                return;

            _ = _onDateChanged(this, stage, incoming?.Date);
        }

        // Note-review slots beyond the first get a numeric suffix so two
        // community arrangements in the same cycle are distinguishable in the
        // detail list. Which real provider each refers to is the case
        // manager's knowledge, not Sati's.
        public string DisplayName =>
            IsNoteReview && SlotIndex > 0
                ? $"{ReviewItem.CategoryDisplayName(Category)} {SlotIndex + 1}"
                : ReviewItem.CategoryDisplayName(Category);

        public bool IsNoteReview =>
            Category is ReviewCategory.NoteReviewHome or ReviewCategory.NoteReviewCommunity;

        // Short label for the compact caseload grid — the strip has room for a
        // letter or two, not a word.
        public string ShortLabel => Category switch
        {
            ReviewCategory.Employment => "Job",
            ReviewCategory.Medical => "Medical",
            ReviewCategory.Dental => "Dental",
            ReviewCategory.GoalReview => "Goals",
            ReviewCategory.NoteReviewHome => "Home",
            ReviewCategory.NoteReviewCommunity => "Community",
            _ => "?"
        };

        public string Tooltip =>
            $"{DisplayName}\n"
            + $"Requested: {FormatDate(RequestedDate)}\n"
            + $"Received: {FormatDate(ReceivedDate)}\n"
            + $"Logged: {FormatDate(LoggedDate)}";

        private static string FormatDate(DateTime? date) =>
            date?.ToString("MM/dd/yy") ?? "—";

        // Called after the service persists a change. Replacing the entity
        // reference rather than mutating in place keeps this VM incapable of
        // writing to the model.
        public void Refresh(ReviewItem updated)
        {
            _isRefreshing = true;
            Item = updated;
            _isRefreshing = false;

            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(RequestedDate));
            OnPropertyChanged(nameof(ReceivedDate));
            OnPropertyChanged(nameof(LoggedDate));
            OnPropertyChanged(nameof(AppointmentDate));
            OnPropertyChanged(nameof(ProviderName));
            OnPropertyChanged(nameof(Tooltip));
        }
    }
}