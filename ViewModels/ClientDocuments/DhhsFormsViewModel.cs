using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>
/// Presentation state for filling the two official Maine DHHS PDFs. It knows only
/// the form service seam: Demo fills server-side while local Production fills on
/// the workstation, and the screen behaves the same in either case.
/// </summary>
public partial class DhhsFormsViewModel : ObservableObject
{
    private readonly IDhhsFormService _formService;
    private readonly IReadOnlyDictionary<DhhsFormDefinition.FormKey, IReadOnlyList<DhhsConsentGroup>> _groups;
    private int? _personId;
    private int _personVersion;

    public DhhsFormsViewModel(IDhhsFormService formService)
    {
        _formService = formService;
        _groups = CreateConsentGroups();
        FormChoices =
        [
            new(
                DhhsFormDefinition.FormKey.AuthorizedRepresentative,
                "Appointment of Authorized Representative",
                "Assigns the signed-in case manager as this consumer's representative. Profile and agency details are filled automatically."),
            new(
                DhhsFormDefinition.FormKey.AuthorizationToRelease,
                "Authorization to Release/Obtain Information",
                "Records only the disclosure choices the consumer explicitly directs you to enter. Signatures remain blank."),
        ];
        selectedFormChoice = FormChoices[0];
        activeConsentGroups = _groups[selectedFormChoice.Key];
        ssnMasked = SsnMask.NotOnFile;
    }

    public IReadOnlyList<DhhsFormChoice> FormChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormDescription))]
    [NotifyPropertyChangedFor(nameof(IsAuthorizedRepresentativeForm))]
    [NotifyPropertyChangedFor(nameof(ShowSsnPanel))]
    private DhhsFormChoice selectedFormChoice;

    [ObservableProperty]
    private IReadOnlyList<DhhsConsentGroup> activeConsentGroups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerson))]
    private string personName = "Select a consumer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanUpdateSsn))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string blankFieldsMessage = string.Empty;

    [ObservableProperty]
    private string ssnMasked;

    [ObservableProperty]
    private string ssnStatusMessage = string.Empty;

    public bool HasPerson => _personId.HasValue;
    public bool IsAuthorizedRepresentativeForm =>
        SelectedFormChoice.Key == DhhsFormDefinition.FormKey.AuthorizedRepresentative;
    public bool ShowSsnPanel => IsAuthorizedRepresentativeForm;
    public bool SupportsSsnStorage => _formService.SupportsSsnStorage;
    public bool CanGenerate => HasPerson && !IsBusy;
    public bool CanUpdateSsn => HasPerson && SupportsSsnStorage && !IsBusy;
    public string FormDescription => SelectedFormChoice.Description;
    public string SsnStorageExplanation => SupportsSsnStorage
        ? "Demo stores only an encrypted envelope. Sati can display the mask, but never reads the number back to this workstation."
        : "Local Production does not store SSNs. This field stays blank on the generated form for hand-completion.";

    public event EventHandler<DhhsPdfReadyEventArgs>? PdfReady;
    public event EventHandler<DhhsProblemEventArgs>? Problem;

    public void SelectForm(DhhsFormDefinition.FormKey key)
    {
        var choice = FormChoices.FirstOrDefault(candidate => candidate.Key == key);
        if (choice is not null)
            SelectedFormChoice = choice;
    }

    partial void OnSelectedFormChoiceChanged(DhhsFormChoice value)
    {
        ActiveConsentGroups = _groups[value.Key];
        StatusMessage = string.Empty;
        BlankFieldsMessage = string.Empty;
        OnPropertyChanged(nameof(CanGenerate));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanUpdateSsn));
        GenerateCommand.NotifyCanExecuteChanged();
        ClearSelectionsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Moves the workspace to a consumer. All moment-of-signing choices are cleared
    /// so consent entered for one person can never follow selection to another.
    /// </summary>
    public void SetPerson(Person? person)
    {
        _personVersion++;
        _personId = person?.Id;
        PersonName = person?.FullName ?? "Select a consumer";
        StatusMessage = string.Empty;
        BlankFieldsMessage = string.Empty;
        SsnMasked = SsnMask.NotOnFile;
        SsnStatusMessage = person is null
            ? "Select a consumer to view SSN status."
            : SupportsSsnStorage
                ? "Loading encrypted SSN status..."
                : "Not stored in local Production.";
        ClearAllSelections();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanUpdateSsn));
        GenerateCommand.NotifyCanExecuteChanged();

        if (person is not null && SupportsSsnStorage)
            _ = LoadSsnStatusAsync(person.Id, _personVersion);
    }

    private async Task LoadSsnStatusAsync(int personId, int version)
    {
        try
        {
            var status = await _formService.GetSsnStatusAsync(personId);
            if (version != _personVersion || _personId != personId)
                return;

            SsnMasked = status.Masked;
            SsnStatusMessage = status.IsOnFile
                ? "An encrypted SSN is on file."
                : "No SSN is on file.";
        }
        catch (Exception ex)
        {
            if (version != _personVersion || _personId != personId)
                return;

            SsnStatusMessage = $"SSN status could not be loaded. {ex.Message}";
        }
    }

    /// <summary>
    /// Called by the PasswordBox-owning view. The value is normalized, sent once,
    /// and never assigned to observable state.
    /// </summary>
    public async Task SaveSsnAsync(string? entered)
    {
        if (!CanUpdateSsn || _personId is not int personId)
            return;

        var normalized = SsnMask.Normalize(entered);
        if (!SsnMask.IsWellFormed(normalized))
        {
            SsnStatusMessage = "Enter a structurally valid nine-digit SSN.";
            return;
        }

        await UpdateSsnAsync(personId, normalized, "The encrypted SSN was updated.");
    }

    public async Task ClearSsnAsync()
    {
        if (!CanUpdateSsn || _personId is not int personId)
            return;

        await UpdateSsnAsync(personId, null, "The encrypted SSN was removed.");
    }

    private async Task UpdateSsnAsync(int personId, string? value, string successMessage)
    {
        var version = _personVersion;
        IsBusy = true;
        SsnStatusMessage = "Updating encrypted SSN...";
        try
        {
            var status = await _formService.UpdateSsnAsync(personId, value);
            if (version != _personVersion || _personId != personId)
                return;

            SsnMasked = status.Masked;
            SsnStatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            if (version != _personVersion || _personId != personId)
                return;

            SsnStatusMessage = $"The SSN could not be updated. {ex.Message}";
            Problem?.Invoke(this, new DhhsProblemEventArgs("SSN Not Updated", SsnStatusMessage));
        }
        finally
        {
            if (version == _personVersion)
                IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_personId is not int personId)
            return;

        var version = _personVersion;
        IsBusy = true;
        StatusMessage = "Filling the official DHHS PDF...";
        BlankFieldsMessage = string.Empty;
        try
        {
            var checks = ActiveConsentGroups
                .SelectMany(group => group.Checks)
                .Where(option => option.IsSelected)
                .ToDictionary(option => option.FieldName, _ => true, StringComparer.Ordinal);
            var text = ActiveConsentGroups
                .SelectMany(group => group.Text)
                .Where(option => !string.IsNullOrWhiteSpace(option.Value))
                .ToDictionary(option => option.FieldName, option => option.Value.Trim(), StringComparer.Ordinal);

            var result = await _formService.GenerateAsync(
                SelectedFormChoice.Key,
                personId,
                new DhhsFormDefinition.Selections(checks, text));

            if (version != _personVersion || _personId != personId)
                return;

            StatusMessage = "The official PDF is ready to save. Signatures and dates remain blank.";
            BlankFieldsMessage = result.BlankFields.Count == 0
                ? "All available profile fields were filled."
                : "Complete these profile fields by hand: " +
                  string.Join(", ", result.BlankFields.Select(FriendlyBlankField)) + ".";
            PdfReady?.Invoke(this, new DhhsPdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception ex)
        {
            if (version != _personVersion || _personId != personId)
                return;

            StatusMessage = "The DHHS form could not be generated.";
            Problem?.Invoke(this, new DhhsProblemEventArgs(
                "Form Not Generated",
                $"The official DHHS form could not be generated.\n\n{ex.Message}"));
        }
        finally
        {
            if (version == _personVersion)
                IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearSelections))]
    private void ClearSelections()
    {
        ClearAllSelections();
        StatusMessage = "The consent selections were cleared.";
    }

    private bool CanClearSelections() => !IsBusy;

    private void ClearAllSelections()
    {
        foreach (var group in _groups.Values.SelectMany(value => value).Distinct())
        {
            foreach (var option in group.Checks)
                option.IsSelected = false;
            foreach (var option in group.Text)
                option.Value = string.Empty;
        }
    }

    private static string FriendlyBlankField(string name) => name switch
    {
        "Individual's SSN" => "Social Security number",
        "AR Name" => "representative name",
        "AR Address" => "representative address",
        "AR Telephone Number" => "representative phone",
        "AR Email Address" => "representative email",
        "Individual's Address" or "Home Address TownCity State Zip Code" => "consumer address",
        "Telephone Email address of individualpersonal representative optional" => "consumer phone or email",
        "Individual's DOB" or "Date of Birth" => "date of birth",
        "Individual's Name" or "Individuals Name" => "consumer name",
        _ => name,
    };

    private static IReadOnlyDictionary<DhhsFormDefinition.FormKey, IReadOnlyList<DhhsConsentGroup>> CreateConsentGroups()
    {
        static DhhsConsentCheckOption C(string field, string label) => new(field, label);
        static DhhsConsentTextOption T(string field, string label, string hint = "") => new(field, label, hint);
        static DhhsConsentGroup G(
            string title,
            string description,
            DhhsConsentCheckOption[] checks,
            DhhsConsentTextOption[]? text = null) =>
            new(title, description, checks, text ?? []);

        return new Dictionary<DhhsFormDefinition.FormKey, IReadOnlyList<DhhsConsentGroup>>
        {
            [DhhsFormDefinition.FormKey.AuthorizedRepresentative] =
            [
                G("Existing legal authority", "Check only existing authority and attach the documentation the form requests.",
                [
                    C("Guardianship", "Guardianship"),
                    C("Power of Attorney", "Power of Attorney"),
                    C("Advanced Healthcare Directive", "Advance Healthcare Directive"),
                    C("Other Legal Authority", "Other legal authority"),
                ],
                [T("Other LA 1", "Describe other legal authority")]),
                G("Authority the consumer is granting", "Each selected item is an authority the consumer is choosing to delegate to the signed-in case manager.",
                [
                    C("Sign and submit app", "Sign and submit an application, including electronically"),
                    C("Sign and submit review", "Sign and submit a recertification, including electronically"),
                    C("Receive copies", "Receive notices and other written DHHS communications"),
                    C("Obtain FS benefits", "Obtain SNAP benefits for the household"),
                    C("Represent at a Fair Hearing", "Represent the consumer at a fair hearing"),
                    C("Act on my behalf", "Act on the consumer's behalf in all other DHHS matters"),
                    C("AR Other", "Other delegated task"),
                ],
                [T("Other AR 1", "Describe the other delegated task")]),
            ],
            [DhhsFormDefinition.FormKey.AuthorizationToRelease] =
            [
                G("DHHS offices", "Choose every office that should help with this request.",
                [
                    C("undefined", "Office of MaineCare Services"),
                    C("undefined_2", "Office of Behavioral Health"),
                    C("Office for Family Independence and Medical Review Team", "Office for Family Independence and Medical Review Team"),
                    C("Office of Child and Family Services", "Office of Child and Family Services"),
                    C("Maine Center for Disease Control and Prevention", "Maine Center for Disease Control and Prevention"),
                    C("Office of Aging and Disability Services", "Office of Aging and Disability Services"),
                    C("Dorothea Dix Psychiatric Center", "Dorothea Dix Psychiatric Center"),
                    C("Division of Administrative Hearings", "Division of Administrative Hearings"),
                    C("Riverview Psychiatric Center", "Riverview Psychiatric Center"),
                    C("Division of Licensing and Certification", "Division of Licensing and Certification"),
                    C("Other", "Other office - first line"),
                    C("Other_3", "Other office - second line"),
                ],
                [
                    T("Other_2", "First other office"),
                    T("Other_4", "Second other office"),
                ]),
                G("Direction and recipient", "Identify whether DHHS is sending or obtaining information and name the party on the other end.",
                [
                    C("ReleaseSend my information to", "Release / send my information to"),
                    C("ObtainGet my information from", "Obtain / get my information from"),
                ],
                [
                    T("Name of Individual", "Recipient individual"),
                    T("Organization", "Recipient organization"),
                    T("Address CityState Zip Code", "Recipient address, city/state, and ZIP"),
                    T("Text2", "Recipient telephone"),
                    T("Telephone Email address optional", "Recipient email address"),
                ]),
                G("Purpose of disclosure", "Choose the purpose stated by the consumer.",
                [
                    C("undefined_3", "Personal request"),
                    C("undefined_4", "Coordinate or manage care"),
                    C("For a legal matter including testimony", "Legal matter, including testimony"),
                    C("To see whether I qualify for insurance coverage services or benefits", "Determine eligibility for coverage, services, or benefits"),
                    C("undefined_5", "Other purpose"),
                ],
                [T("Other_5", "Describe the other purpose")]),
                G("Email delivery", "Complete only if the consumer accepts the email risks stated on the official form.", [],
                [
                    T("information by email INITIALHERE", "Consumer initials accepting email risk"),
                    T("Please print the email address where you want your information sent", "Email delivery address"),
                ]),
                G("General records", "Choose each general category the consumer permits.",
                [
                    C("All health information from the offices checked", "All health information from the selected offices"),
                    C("Insurance Claims or encounter data information", "Insurance claims or encounter data"),
                    C("Financial information including billing payment", "Financial information, including billing, payment, income, banking, and assets"),
                    C("Limit to the following dates or types of information", "Limit to specified dates or types"),
                    C("Other_6", "Other general information"),
                ],
                [
                    T("2024", "Dates or types to include"),
                    T("undefined_6", "Describe other general information"),
                ]),
                G("Drug or alcohol records", "These selections carry the special substance-use disclosure rules printed on the form.",
                [
                    C("Include all drugalcohol information in the release", "Include all drug/alcohol information"),
                    C("Include only the specific drugalcohol records checked", "Include only the specific records selected below"),
                    C("Diagnosis and treatment", "Diagnosis and treatment"),
                    C("Clinical notes and discharge summaries", "Clinical notes and discharge summaries"),
                    C("DrugAlcohol history or summary", "Drug/alcohol history or summary"),
                    C("Payment or claims information", "Payment or claims information"),
                    C("Living situation and social supports", "Living situation and social supports"),
                    C("Medication dosages or supplies", "Medication, dosages, or supplies"),
                    C("Lab results", "Lab results"),
                    C("Other_7", "Other drug/alcohol information"),
                ],
                [T("undefined_7", "Describe other drug/alcohol information")]),
                G("Mental/behavioral health and HIV/AIDS", "Choose these special permissions separately; the official form explains the protections and possible effects.",
                [
                    C("Include this information in the release", "Include mental/behavioral health information"),
                    C("I want to review my mental healthbehavioral health", "Consumer wants to review the mental/behavioral health record before release"),
                    C("Include this information in the release_2", "Include HIV/AIDS status or test results"),
                ]),
                G("Expiration", "The form expires in one year unless the consumer gives an earlier date.", [],
                [T("This form expires one year from the date below unless I write an earlier date here", "Earlier expiration date", "MM/DD/YYYY")]),
            ],
        };
    }
}

public sealed record DhhsFormChoice(
    DhhsFormDefinition.FormKey Key,
    string DisplayName,
    string Description);

public sealed record DhhsConsentGroup(
    string Title,
    string Description,
    IReadOnlyList<DhhsConsentCheckOption> Checks,
    IReadOnlyList<DhhsConsentTextOption> Text);

public partial class DhhsConsentCheckOption(string fieldName, string label) : ObservableObject
{
    public string FieldName { get; } = fieldName;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool isSelected;
}

public partial class DhhsConsentTextOption(string fieldName, string label, string hint) : ObservableObject
{
    public string FieldName { get; } = fieldName;
    public string Label { get; } = label;
    public string Hint { get; } = hint;

    [ObservableProperty]
    private string value = string.Empty;
}

public sealed class DhhsPdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class DhhsProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
