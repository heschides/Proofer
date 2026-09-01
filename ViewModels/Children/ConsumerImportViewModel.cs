using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    /// <summary>
    /// One field on the review list: what the export said, what Sati would store, and where it
    /// came from.
    /// </summary>
    /// <remarks>
    /// Both the raw and the converted value are shown. A reviewer accepting <c>F84.0</c> should
    /// be able to see that it came from <c>(F84.0) Autistic disorder</c> — a field-by-field
    /// acceptance step means nothing if the reviewer cannot see what they are accepting.
    /// </remarks>
    public partial class ImportFieldViewModel : ObservableObject
    {
        public ImportFieldViewModel(CredibleFieldDraft draft, string displayName)
        {
            Draft = draft;
            DisplayName = displayName;
            // Only a converted value can be accepted. Everything else is shown so the reviewer
            // knows what was NOT brought across, which is the point of listing it at all.
            isAccepted = draft.Status == CredibleFieldStatus.Mapped;
        }

        public CredibleFieldDraft Draft { get; }
        public string DisplayName { get; }

        public string SatiField => Draft.SatiField;
        public string? Value => Draft.Value;
        public string? RawValue => Draft.RawValue;
        public string Source => $"{Draft.Section} / {Draft.Label}";
        public bool CanAccept => Draft.Status == CredibleFieldStatus.Mapped;

        // Shown when the converted value differs from what the cell held, so the conversion is
        // visible rather than implied.
        public bool ShowsRawValue =>
            RawValue is not null && !string.Equals(RawValue, Value, StringComparison.Ordinal);

        [ObservableProperty] private bool isAccepted;

        public string StatusText => Draft.Status switch
        {
            CredibleFieldStatus.Mapped => "Found",
            CredibleFieldStatus.Blank => "Empty in the export",
            CredibleFieldStatus.LabelMissing => "Not in this export",
            CredibleFieldStatus.SectionMissing => "Section not exported",
            _ => "Could not be read"
        };

        public bool IsProblem => Draft.Status is CredibleFieldStatus.Unreadable
                                              or CredibleFieldStatus.LabelMissing
                                              or CredibleFieldStatus.SectionMissing;

        /// <summary>Accessible description — status is never carried by colour alone.</summary>
        public string AutomationDescription =>
            $"{DisplayName}. {StatusText}. {(Value is null ? "No value" : Value)}. From {Source}.";
    }

    /// <summary>Everything the reviewer accepted, ready to fill the new-client form.</summary>
    /// <param name="Values">Accepted field values, keyed by <see cref="CredibleFields"/> name.</param>
    /// <param name="Ssn">
    /// Held apart from <paramref name="Values"/> deliberately. The demographic save must not
    /// carry an SSN — it goes through the SSN route, which encrypts it and audits the write
    /// without the value.
    /// </param>
    public sealed record AcceptedImportDraft(
        IReadOnlyDictionary<string, string> Values,
        string? Ssn,
        string? CredibleClientId);

    /// <summary>
    /// Reviewing a Credible export before it fills the new-client form.
    ///
    /// <para>
    /// This never saves. It reads a file, maps it, shows the reviewer every field it found and
    /// every one it did not, and hands the accepted values to the entry form — which submits
    /// through the same path a typed record does. That is what keeps one create path, and it
    /// matters most in local Production, where no server sits between the form and the database.
    /// </para>
    /// </summary>
    public partial class ConsumerImportViewModel : ObservableObject
    {
        private static readonly IReadOnlyDictionary<string, string> FieldNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CredibleFields.FirstName] = "First name",
                [CredibleFields.LastName] = "Last name",
                [CredibleFields.BirthDate] = "Date of birth",
                [CredibleFields.Gender] = "Gender",
                [CredibleFields.CredibleClientId] = "Credible client ID",
                [CredibleFields.MaineCareId] = "MaineCare ID",
                [CredibleFields.Ssn] = "Social Security number",
                [CredibleFields.DiagnosisCode] = "Diagnosis code",
                [CredibleFields.HasGuardian] = "Has a guardian",
                [CredibleFields.GuardianFirstName] = "Guardian first name",
                [CredibleFields.GuardianLastName] = "Guardian last name",
                [CredibleFields.PhoneNumber] = "Phone number",
                [CredibleFields.Email] = "Email",
                [CredibleFields.BillingStreet] = "Street",
                [CredibleFields.BillingCity] = "City",
                [CredibleFields.BillingState] = "State",
                [CredibleFields.BillingZip] = "ZIP",
            };

        private readonly IClientExportReader _reader;
        private readonly IExportFilePicker _picker;
        private CredibleProfileDraft? _draft;

        public ConsumerImportViewModel(IClientExportReader reader, IExportFilePicker picker)
        {
            _reader = reader;
            _picker = picker;
        }

        /// <summary>Raised when the reviewer accepts. The host fills the entry form from this.</summary>
        public event Action<AcceptedImportDraft>? DraftAccepted;

        /// <summary>Raised when the reviewer backs out, so the host can close the panel.</summary>
        public event Action? Cancelled;

        [ObservableProperty] private bool isOpen;
        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private string? refusalMessage;

        public ObservableCollection<ImportFieldViewModel> Fields { get; } = [];

        public bool HasRefusal => !string.IsNullOrWhiteSpace(RefusalMessage);
        public bool HasFields => Fields.Count > 0;
        public int AcceptedCount => Fields.Count(accepted => accepted.IsAccepted);
        public bool CanApply => !IsBusy && AcceptedCount > 0;

        public string AcceptedLabel => AcceptedCount == 1
            ? "1 field will be filled in"
            : $"{AcceptedCount} fields will be filled in";

        partial void OnRefusalMessageChanged(string? value) => OnPropertyChanged(nameof(HasRefusal));
        partial void OnIsBusyChanged(bool value) => NotifyAcceptanceState();

        private void NotifyAcceptanceState()
        {
            OnPropertyChanged(nameof(AcceptedCount));
            OnPropertyChanged(nameof(AcceptedLabel));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(HasFields));
            ApplyCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private async Task ChooseFile()
        {
            var path = _picker.PickExportFile();
            if (path is null)
                return;

            await LoadAsync(path);
        }

        /// <summary>Reads and maps one export. Separated from the picker so tests need no dialog.</summary>
        public async Task LoadAsync(string path)
        {
            IsBusy = true;
            try
            {
                Reset();
                var result = await _reader.ReadAsync(path);
                if (!result.Succeeded)
                {
                    // The reader's own wording, which names the operator's fix rather than the
                    // symptom. Nothing from the file's content reaches this message.
                    RefusalMessage = result.Describe();
                    return;
                }

                _draft = CredibleProfileMapping.Map(result.Document!, CredibleLayoutProfile.Default);
                foreach (var drafted in _draft.Fields)
                {
                    var row = new ImportFieldViewModel(drafted, DisplayNameFor(drafted.SatiField));
                    row.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(ImportFieldViewModel.IsAccepted))
                            NotifyAcceptanceState();
                    };
                    Fields.Add(row);
                }

                StatusMessage = Summarize(_draft);
                IsOpen = true;
            }
            finally
            {
                IsBusy = false;
                NotifyAcceptanceState();
            }
        }

        [RelayCommand]
        private void AcceptAll()
        {
            foreach (var field in Fields.Where(field => field.CanAccept))
                field.IsAccepted = true;
            NotifyAcceptanceState();
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var field in Fields)
                field.IsAccepted = false;
            NotifyAcceptanceState();
        }

        [RelayCommand]
        private void Cancel()
        {
            Reset();
            IsOpen = false;
            Cancelled?.Invoke();
        }

        /// <summary>
        /// Hands the accepted values to the host. Fills the form; does not save.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void Apply()
        {
            var accepted = Fields
                .Where(field => field.IsAccepted && field.Value is not null)
                .ToDictionary(field => field.SatiField, field => field.Value!, StringComparer.Ordinal);

            // Pulled out rather than left in the dictionary: whatever fills the demographic form
            // must never be handed an SSN.
            accepted.Remove(CredibleFields.Ssn, out var ssn);
            accepted.TryGetValue(CredibleFields.CredibleClientId, out var clientId);

            DraftAccepted?.Invoke(new AcceptedImportDraft(
                accepted,
                ssn,
                clientId ?? _draft?.CredibleClientId));

            IsOpen = false;
        }

        private void Reset()
        {
            Fields.Clear();
            _draft = null;
            RefusalMessage = null;
            StatusMessage = string.Empty;
        }

        private static string DisplayNameFor(string satiField) =>
            FieldNames.TryGetValue(satiField, out var name) ? name : satiField;

        private static string Summarize(CredibleProfileDraft draft)
        {
            var found = draft.Fields.Count(field => field.Status == CredibleFieldStatus.Mapped);
            var problems = draft.Problems.Count();

            var summary = found == 1
                ? "1 field found in this export."
                : $"{found} fields found in this export.";

            if (draft.MissingSections.Count > 0)
            {
                summary += $" Sections not in the export: {string.Join(", ", draft.MissingSections)}." +
                           " Check the print options were all ticked.";
            }
            else if (problems > 0)
            {
                summary += $" {problems} could not be read — review them below.";
            }

            return summary;
        }
    }
}
