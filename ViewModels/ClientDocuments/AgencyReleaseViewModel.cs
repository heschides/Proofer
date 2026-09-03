using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.ViewModels.ClientDocuments;

public partial class AgencyReleaseViewModel : ObservableObject
{
    private readonly IAgencyReleaseService _service;
    private int? _personId;
    private int _personVersion;

    public AgencyReleaseViewModel(IAgencyReleaseService service)
    {
        _service = service;
        ReleaseKindChoices =
        [
            new(AnnualDocumentKind.ReleaseAgency, "Agency release"),
            new(AnnualDocumentKind.ReleaseMedical, "Medical release")
        ];
        selectedReleaseKind = ReleaseKindChoices[0];
        YesNoChoices = [new("Yes", true), new("No", false)];
        ScopeChoices =
        [
            new("One-time disclosure", AgencyReleaseScope.OneTime),
            new("Multiple disclosures", AgencyReleaseScope.Multiple),
        ];
        ContactTypeChoices =
        [
            "Home support",
            "Community support",
            "Shared living",
            "Healthcare provider",
            "Service provider",
            "Education",
            "Family / guardian",
            "Other",
        ];
        InformationCategories = AgencyReleaseInformation.All
            .Select(value => new AgencyReleaseCategoryOption(value, AgencyReleaseInformation.DisplayName(value)))
            .ToList();
        ResetInputs();
    }

    public IReadOnlyList<YesNoChoice> YesNoChoices { get; }
    public IReadOnlyList<ReleaseKindChoice> ReleaseKindChoices { get; }
    public IReadOnlyList<AgencyReleaseScopeChoice> ScopeChoices { get; }
    public IReadOnlyList<string> ContactTypeChoices { get; }
    public IReadOnlyList<AgencyReleaseCategoryOption> InformationCategories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceTitle))]
    [NotifyPropertyChangedFor(nameof(WorkspaceDescription))]
    [NotifyPropertyChangedFor(nameof(GenerateButtonText))]
    private ReleaseKindChoice selectedReleaseKind;

    [ObservableProperty]
    private string personName = "Select a consumer";

    [ObservableProperty]
    private YesNoChoice? authorizationChoice;

    [ObservableProperty]
    private string? contactType;

    [ObservableProperty]
    private string contactName = string.Empty;

    [ObservableProperty]
    private string relationship = string.Empty;

    [ObservableProperty]
    private string contactAddress = string.Empty;

    [ObservableProperty]
    private string contactCity = string.Empty;

    [ObservableProperty]
    private string contactState = "ME";

    [ObservableProperty]
    private string contactFax = string.Empty;

    [ObservableProperty]
    private string contactPhone = string.Empty;

    [ObservableProperty]
    private string contactEmail = string.Empty;

    [ObservableProperty]
    private string otherInformation = string.Empty;

    [ObservableProperty]
    private DateTime? startDate;

    [ObservableProperty]
    private DateTime? expirationDate;

    [ObservableProperty]
    private AgencyReleaseScopeChoice? selectedScope;

    [ObservableProperty]
    private YesNoChoice? drugAlcoholChoice;

    [ObservableProperty]
    private YesNoChoice? mentalHealthChoice;

    [ObservableProperty]
    private YesNoChoice? hivAidsChoice;

    [ObservableProperty]
    private YesNoChoice? releaseWithoutReviewChoice;

    [ObservableProperty]
    private bool isRevocation;

    [ObservableProperty]
    private DateTime? revokedOn;

    [ObservableProperty]
    private bool didObtainRoi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    private bool isBusy;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public bool HasPerson => _personId.HasValue;
    public bool CanGenerate => HasPerson && !IsBusy;
    public string WorkspaceTitle => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "MEDICAL RELEASE OF INFORMATION"
        : "AGENCY RELEASE OF INFORMATION";
    public string WorkspaceDescription => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "Prepare Sati's medical release for a healthcare recipient. Consumer identity, guardian, agency, and case-manager details come from the signed-in record."
        : "Prepare Sati's agency release to disclose or obtain information. Consumer identity, guardian, agency, and case-manager details come from the signed-in record.";
    public string GenerateButtonText => SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
        ? "Generate medical release PDF"
        : "Generate agency release PDF";

    public event EventHandler<AgencyReleasePdfReadyEventArgs>? PdfReady;
    public event EventHandler<AgencyReleaseProblemEventArgs>? Problem;
    public event Func<AgencyReleaseAttestationEventArgs, bool>? AttestationRequested;

    partial void OnIsBusyChanged(bool value)
    {
        GenerateCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedScopeChanged(AgencyReleaseScopeChoice? value)
    {
        if (StartDate is not DateTime start || value is null)
            return;
        ExpirationDate = value.Value == AgencyReleaseScope.OneTime
            ? start.AddDays(90)
            : start.AddYears(1);
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        if (value is not DateTime start || SelectedScope is null)
            return;
        ExpirationDate = SelectedScope.Value == AgencyReleaseScope.OneTime
            ? start.AddDays(90)
            : start.AddYears(1);
    }

    partial void OnIsRevocationChanged(bool value)
    {
        if (value && RevokedOn is null)
            RevokedOn = DateTime.Today;
    }

    public void SetPerson(Person? person)
    {
        _personVersion++;
        _personId = person?.Id;
        PersonName = person?.FullName ?? "Select a consumer";
        ResetInputs();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_personId is not int personId)
            return;

        var request = BuildRequest();
        var errors = AgencyReleaseRules.Validate(request);
        if (errors.Count > 0)
        {
            ValidationMessage = string.Join(
                Environment.NewLine,
                errors.Values.SelectMany(values => values).Select(message => $"• {message}"));
            StatusMessage = "Review the highlighted requirements before generating the release.";
            return;
        }

        if (request.ConfirmedObtainedRoi)
        {
            var confirmed = AttestationRequested?.Invoke(new AgencyReleaseAttestationEventArgs(
                AgencyReleaseRules.StaffAttestation,
                AgencyReleaseRules.AttestationScopeNotice)) == true;
            if (!confirmed)
            {
                StatusMessage = "The staff attestation was not recorded; no PDF was generated.";
                return;
            }
        }

        var version = _personVersion;
        IsBusy = true;
        ValidationMessage = string.Empty;
        var documentName = SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
            ? "medical release"
            : "agency release";
        StatusMessage = $"Preparing the Sati {documentName}...";
        try
        {
            var result = SelectedReleaseKind.Kind == AnnualDocumentKind.ReleaseMedical
                ? await _service.GenerateMedicalAsync(personId, request)
                : await _service.GenerateAsync(personId, request);
            if (version != _personVersion || _personId != personId)
                return;

            StatusMessage = request.ConfirmedObtainedRoi
                ? $"The {documentName} and staff attestation are ready to save. Consumer signature lines remain blank."
                : $"The {documentName} draft is ready to save. No staff attestation was recorded.";
            PdfReady?.Invoke(this, new AgencyReleasePdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception ex)
        {
            if (version != _personVersion || _personId != personId)
                return;
            StatusMessage = $"The {documentName} could not be generated.";
            Problem?.Invoke(this, new AgencyReleaseProblemEventArgs(
                "Release Not Generated",
                $"The {documentName} could not be generated.\n\n{ex.Message}"));
        }
        finally
        {
            if (version == _personVersion)
                IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        ResetInputs();
        StatusMessage = "The agency-release entries were cleared.";
    }

    private bool CanClear() => !IsBusy;

    internal AgencyReleaseRequest BuildRequest() => new(
        AuthorizationChoice?.Value,
        ContactType,
        ContactName,
        Relationship,
        ContactAddress,
        ContactCity,
        ContactState,
        ContactFax,
        ContactPhone,
        ContactEmail,
        InformationCategories.Where(option => option.IsSelected).Select(option => option.Value).ToList(),
        OtherInformation,
        StartDate is DateTime start ? DateOnly.FromDateTime(start) : null,
        ExpirationDate is DateTime expiration ? DateOnly.FromDateTime(expiration) : null,
        SelectedScope?.Value.ToString(),
        DrugAlcoholChoice?.Value,
        MentalHealthChoice?.Value,
        HivAidsChoice?.Value,
        ReleaseWithoutReviewChoice?.Value,
        IsRevocation,
        IsRevocation && RevokedOn is DateTime revoked ? DateOnly.FromDateTime(revoked) : null,
        DidObtainRoi,
        IsDraft: !DidObtainRoi);

    private void ResetInputs()
    {
        AuthorizationChoice = null;
        ContactType = null;
        ContactName = string.Empty;
        Relationship = string.Empty;
        ContactAddress = string.Empty;
        ContactCity = string.Empty;
        ContactState = "ME";
        ContactFax = string.Empty;
        ContactPhone = string.Empty;
        ContactEmail = string.Empty;
        OtherInformation = string.Empty;
        foreach (var option in InformationCategories)
            option.IsSelected = false;
        StartDate = DateTime.Today;
        SelectedScope = null;
        ExpirationDate = null;
        DrugAlcoholChoice = null;
        MentalHealthChoice = null;
        HivAidsChoice = null;
        ReleaseWithoutReviewChoice = null;
        IsRevocation = false;
        RevokedOn = null;
        DidObtainRoi = false;
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
    }
}

public sealed record YesNoChoice(string DisplayName, bool Value);
public sealed record AgencyReleaseScopeChoice(string DisplayName, AgencyReleaseScope Value);
public sealed record ReleaseKindChoice(AnnualDocumentKind Kind, string DisplayName);

public partial class AgencyReleaseCategoryOption(string value, string displayName) : ObservableObject
{
    public string Value { get; } = value;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool isSelected;
}

public sealed class AgencyReleasePdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class AgencyReleaseProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}

public sealed record AgencyReleaseAttestationEventArgs(string Statement, string ScopeNotice);
