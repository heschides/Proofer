using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace Sati.ViewModels.Supervisor
{
    /// <summary>What the dry run decided about one file.</summary>
    public enum BulkImportDisposition
    {
        /// <summary>Parsed, not already held, and ready to create.</summary>
        Ready,

        /// <summary>The agency already holds this Credible id. Reported, never merged.</summary>
        AlreadyImported,

        /// <summary>Refused or unreadable — a procedure error, not a client problem.</summary>
        Refused,

        /// <summary>Parsed, but with no name to create a consumer from.</summary>
        Incomplete
    }

    /// <summary>One file in the folder, and what became of it.</summary>
    public partial class BulkImportRowViewModel(
        string fileName,
        BulkImportDisposition disposition,
        string detail,
        AcceptedImportDraftValues? values) : ObservableObject
    {
        /// <summary>
        /// The file's name only, never its path.
        ///
        /// <para>
        /// Displayed because an operator has to be able to find the file they need to re-save.
        /// It is still PHI — <c>Smith_John.htm</c> names a client — so it is shown and never
        /// recorded: no audit metadata, no log line, no import manifest.
        /// </para>
        /// </summary>
        public string FileName { get; } = fileName;

        public BulkImportDisposition Disposition { get; } = disposition;
        public string Detail { get; } = detail;
        public AcceptedImportDraftValues? Values { get; } = values;

        [ObservableProperty] private string? outcome;
        [ObservableProperty] private bool failed;

        public bool IsReady => Disposition == BulkImportDisposition.Ready;
        public bool HasOutcome => !string.IsNullOrWhiteSpace(Outcome);

        public string DispositionText => Disposition switch
        {
            BulkImportDisposition.Ready => "Ready to import",
            BulkImportDisposition.AlreadyImported => "Already in Sati",
            BulkImportDisposition.Incomplete => "Not enough to create a consumer",
            _ => "Cannot be read"
        };

        public string AutomationDescription =>
            $"{FileName}. {DispositionText}. {Detail}{(HasOutcome ? $" {Outcome}" : string.Empty)}";

        public void RecordOutcome(string message, bool failed)
        {
            Outcome = message;
            Failed = failed;
            OnPropertyChanged(nameof(HasOutcome));
            OnPropertyChanged(nameof(AutomationDescription));
        }
    }

    /// <summary>
    /// The mapped values one file yielded.
    ///
    /// <para>
    /// Carries no SSN. Bulk import cannot write one — the value is encrypted against the
    /// consumer's id, which does not exist until the record is created — so the number is read
    /// past and dropped rather than held on a view model with no use for it.
    /// </para>
    /// </summary>
    public sealed record AcceptedImportDraftValues(
        IReadOnlyDictionary<string, string> Values,
        string? CredibleClientId);

    /// <summary>
    /// Bulk import of a folder of saved Credible print views, for agency onboarding.
    ///
    /// <para>
    /// Two phases, never one. The dry run parses every file and reports what it found — how many
    /// are ready, how many the agency already holds, and which files cannot be read — and writes
    /// nothing. Only then can the operator commit. Onboarding is the moment an agency's whole
    /// caseload lands in Sati, and a mistake made silently there is the most expensive kind.
    /// </para>
    ///
    /// <para>
    /// Files are parsed one at a time and dropped. Managed strings cannot be reliably zeroed, so
    /// the mitigation is lifetime rather than scrubbing: a 400-file batch never holds 400 parsed
    /// exports at once.
    /// </para>
    ///
    /// <para>
    /// Nothing is uploaded. The parse happens here, and each consumer is created through the
    /// ordinary single-record path — one validation, one audit event, one version each. There is
    /// deliberately no bulk endpoint: it would be a second way to create a consumer.
    /// </para>
    /// </summary>
    public partial class CaseloadImportViewModel : ObservableObject
    {
        private readonly IClientExportReader _reader;
        private readonly IExportFilePicker _picker;
        private readonly IPersonService _personService;
        private readonly ISessionService _sessionService;

        public CaseloadImportViewModel(
            IClientExportReader reader,
            IExportFilePicker picker,
            IPersonService personService,
            ISessionService sessionService)
        {
            _reader = reader;
            _picker = picker;
            _personService = personService;
            _sessionService = sessionService;
        }

        /// <summary>Raised after at least one consumer was created.</summary>
        public event Action? ConsumersImported;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private string progressMessage = string.Empty;
        [ObservableProperty] private bool hasDryRun;

        public ObservableCollection<BulkImportRowViewModel> Rows { get; } = [];

        public int ReadyCount => Rows.Count(row => row.IsReady);
        public int AlreadyImportedCount =>
            Rows.Count(row => row.Disposition == BulkImportDisposition.AlreadyImported);
        public int ProblemCount => Rows.Count(row =>
            row.Disposition is BulkImportDisposition.Refused or BulkImportDisposition.Incomplete);

        public bool HasRows => Rows.Count > 0;
        public bool CanCommit => !IsBusy && HasDryRun && ReadyCount > 0;

        partial void OnIsBusyChanged(bool value) => NotifyState();
        partial void OnHasDryRunChanged(bool value) => NotifyState();

        private void NotifyState()
        {
            OnPropertyChanged(nameof(ReadyCount));
            OnPropertyChanged(nameof(AlreadyImportedCount));
            OnPropertyChanged(nameof(ProblemCount));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(CanCommit));
            CommitCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private async Task ChooseFolder()
        {
            var folder = _picker.PickExportFolder();
            if (folder is null)
                return;

            await DryRunAsync(folder);
        }

        /// <summary>
        /// Parses every export in the folder and reports what it found. Writes nothing.
        /// </summary>
        public async Task DryRunAsync(string folderPath)
        {
            IsBusy = true;
            HasDryRun = false;
            Rows.Clear();
            try
            {
                string[] files;
                try
                {
                    files = Directory
                        .EnumerateFiles(folderPath)
                        .Where(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                                       path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    StatusMessage = "That folder could not be read.";
                    return;
                }

                if (files.Length == 0)
                {
                    StatusMessage =
                        "No saved web pages in that folder. Credible print views must be saved as " +
                        "web pages — a printed PDF loses which value belongs to which field.";
                    return;
                }

                var parsed = new List<BulkImportRowViewModel>(files.Length);
                var index = 0;
                foreach (var path in files)
                {
                    index++;
                    ProgressMessage = $"Reading {index} of {files.Length}…";
                    parsed.Add(await ParseAsync(path));
                }

                // One lookup for the whole batch rather than one per file: 400 round trips to
                // answer the same question is the difference between a usable dry run and a
                // coffee break.
                var dedupeWarning = await MarkAlreadyImportedAsync(parsed);

                foreach (var row in parsed)
                    Rows.Add(row);

                HasDryRun = true;

                // The warning is appended rather than assigned, because the summary that follows
                // would otherwise overwrite it — which would turn "we could not check for
                // duplicates" into silence, and silence into 400 duplicate clinical records.
                StatusMessage = dedupeWarning is null
                    ? SummarizeDryRun(files.Length)
                    : $"{dedupeWarning} {SummarizeDryRun(files.Length)}";
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                NotifyState();
            }
        }

        /// <summary>
        /// Creates the ready consumers, one ordinary create each.
        ///
        /// <para>
        /// Sequential and per-record. Each create is independently validated, audited and
        /// versioned, and each can fail on its own; a batch that succeeded or failed as a unit
        /// would either abandon good records or hide the one that did not happen.
        /// </para>
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCommit))]
        private async Task Commit()
        {
            var actor = _sessionService.CurrentUser;
            if (actor is null)
                return;

            var ready = Rows.Where(row => row.IsReady && !row.HasOutcome).ToList();
            if (ready.Count == 0)
                return;

            IsBusy = true;
            var created = 0;
            var failed = 0;
            try
            {
                var index = 0;
                foreach (var row in ready)
                {
                    index++;
                    ProgressMessage = $"Creating {index} of {ready.Count}…";
                    try
                    {
                        await _personService.AddPersonAsync(BuildPerson(row.Values!, actor));
                        row.RecordOutcome("Created.", failed: false);
                        created++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // The service's own message. Neither path puts consumer content in it.
                        row.RecordOutcome(exception.Message, failed: true);
                        failed++;
                    }
                }
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
            }

            StatusMessage = SummarizeCommit(created, failed);
            if (created > 0)
                ConsumersImported?.Invoke();

            NotifyState();
        }

        // ---- internals ----

        private async Task<BulkImportRowViewModel> ParseAsync(string path)
        {
            var name = Path.GetFileName(path);
            var result = await _reader.ReadAsync(path);
            if (!result.Succeeded)
                return new BulkImportRowViewModel(name, BulkImportDisposition.Refused, result.Describe(), null);

            var draft = CredibleProfileMapping.Map(result.Document!, CredibleLayoutProfile.Default);
            var values = draft.Fields
                .Where(field => field.Status == CredibleFieldStatus.Mapped && field.Value is not null)
                .ToDictionary(field => field.SatiField, field => field.Value!, StringComparer.Ordinal);

            // Read past and dropped: nothing here can write it.
            values.Remove(CredibleFields.Ssn);
            values.TryGetValue(CredibleFields.CredibleClientId, out var clientId);
            clientId ??= draft.CredibleClientId;

            // A consumer with no name cannot be created and must not be guessed at. Reported so
            // the operator knows the file was seen and skipped, rather than silently absent.
            if (!values.ContainsKey(CredibleFields.FirstName) ||
                !values.ContainsKey(CredibleFields.LastName))
            {
                return new BulkImportRowViewModel(
                    name,
                    BulkImportDisposition.Incomplete,
                    "The export has no first and last name. Check the print options were all ticked.",
                    null);
            }

            var problems = draft.Problems.Count();
            var detail = problems == 0
                ? $"{values.Count} fields."
                : $"{values.Count} fields; {problems} could not be read.";

            return new BulkImportRowViewModel(
                name,
                BulkImportDisposition.Ready,
                detail,
                new AcceptedImportDraftValues(values, clientId));
        }

        /// <summary>
        /// Re-dispositions the rows the agency already holds.
        ///
        /// <para>
        /// A match is reported and skipped, never merged. Merging into an existing clinical
        /// record on the strength of an identifier match is not recoverable if the match is wrong.
        /// </para>
        /// </summary>
        private async Task<string?> MarkAlreadyImportedAsync(List<BulkImportRowViewModel> rows)
        {
            var ids = rows
                .Where(row => row.IsReady && row.Values?.CredibleClientId is not null)
                .Select(row => row.Values!.CredibleClientId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
                return null;

            IReadOnlyList<CredibleClientMatchDto> matches;
            try
            {
                matches = await _personService.FindCredibleMatchesAsync(ids);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Failing the dedupe check must not silently become "nothing is a duplicate".
                return "Sati could not check which consumers are already imported -- review the " +
                       "list before importing, or some may be created twice.";
            }

            var byId = matches.ToDictionary(
                match => match.CredibleClientId, match => match.OwnerDisplayName,
                StringComparer.Ordinal);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var id = row.Values?.CredibleClientId;
                if (id is null || !byId.TryGetValue(id, out var owner))
                    continue;

                rows[index] = new BulkImportRowViewModel(
                    row.FileName,
                    BulkImportDisposition.AlreadyImported,
                    owner is null
                        ? "Already imported into this agency."
                        : $"Already on {owner}'s caseload.",
                    null);
            }

            return null;
        }

        private static Person BuildPerson(AcceptedImportDraftValues draft, User actor)
        {
            var values = draft.Values;

            // No effective date, deliberately: it would generate a full compliance cycle for
            // every consumer in the batch on the caseload-load path. It is set at distribution,
            // by somebody who knows the case. See CREDIBLE_IMPORT_DESIGN.md.
            var person = Person.CreatePerson(
                actor.Id,
                values.GetValueOrDefault(CredibleFields.FirstName, string.Empty),
                values.GetValueOrDefault(CredibleFields.LastName, string.Empty),
                "Imported from a Credible export. Biography not yet written.",
                ParseDate(values.GetValueOrDefault(CredibleFields.BirthDate)),
                null,
                WaiverType.None,
                new Settings());

            person.MaineCareId = values.GetValueOrDefault(CredibleFields.MaineCareId);
            person.DiagnosisCode = values.GetValueOrDefault(CredibleFields.DiagnosisCode);
            person.PhoneNumber = values.GetValueOrDefault(CredibleFields.PhoneNumber);
            person.Email = values.GetValueOrDefault(CredibleFields.Email);
            person.BillingStreet = values.GetValueOrDefault(CredibleFields.BillingStreet);
            person.BillingCity = values.GetValueOrDefault(CredibleFields.BillingCity);
            person.BillingState = values.GetValueOrDefault(CredibleFields.BillingState);
            person.BillingZip = values.GetValueOrDefault(CredibleFields.BillingZip);
            person.CredibleClientId = draft.CredibleClientId;

            if (values.TryGetValue(CredibleFields.Gender, out var gender) &&
                Enum.TryParse<Gender>(gender, out var parsedGender))
            {
                person.Gender = parsedGender;
            }

            if (values.TryGetValue(CredibleFields.HasGuardian, out var hasGuardian))
                person.HasGuardian = string.Equals(hasGuardian, "true", StringComparison.Ordinal);

            var guardian = string.Join(' ', new[]
            {
                values.GetValueOrDefault(CredibleFields.GuardianFirstName),
                values.GetValueOrDefault(CredibleFields.GuardianLastName)
            }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
            if (guardian.Length > 0)
                person.GuardianName = guardian;

            return person;
        }

        private static DateTime ParseDate(string? isoDate) =>
            DateTime.TryParseExact(
                isoDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed
                : default;

        private string SummarizeDryRun(int fileCount)
        {
            var parts = new List<string> { $"{fileCount} files read." };
            parts.Add(ReadyCount == 1 ? "1 ready to import." : $"{ReadyCount} ready to import.");

            if (AlreadyImportedCount > 0)
            {
                parts.Add(AlreadyImportedCount == 1
                    ? "1 already in Sati and will be skipped."
                    : $"{AlreadyImportedCount} already in Sati and will be skipped.");
            }

            if (ProblemCount > 0)
            {
                parts.Add(ProblemCount == 1
                    ? "1 could not be used — see the list."
                    : $"{ProblemCount} could not be used — see the list.");
            }

            parts.Add("Nothing has been saved yet.");
            return string.Join(' ', parts);
        }

        private static string SummarizeCommit(int created, int failed)
        {
            if (failed == 0)
            {
                return created == 1
                    ? "1 consumer created on your caseload."
                    : $"{created} consumers created on your caseload.";
            }

            if (created == 0)
                return $"{failed} could not be created. See the messages beside them.";

            return $"{created} created; {failed} could not be. See the messages beside them.";
        }
    }
}
