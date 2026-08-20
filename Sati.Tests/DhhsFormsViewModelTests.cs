using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class DhhsFormsViewModelTests
{
    [Fact]
    public async Task Generation_sends_only_the_visible_form_choices_and_surfaces_the_pdf()
    {
        var service = new RecordingDhhsFormService(supportsSsnStorage: true);
        var viewModel = new DhhsFormsViewModel(service);
        viewModel.SetPerson(PersonFor(31, "First"));

        var guardianship = viewModel.ActiveConsentGroups
            .SelectMany(group => group.Checks)
            .Single(option => option.FieldName == "Guardianship");
        var otherAuthority = viewModel.ActiveConsentGroups
            .SelectMany(group => group.Text)
            .Single(option => option.FieldName == "Other LA 1");
        guardianship.IsSelected = true;
        otherAuthority.Value = "Court order";

        DhhsPdfReadyEventArgs? ready = null;
        viewModel.PdfReady += (_, args) => ready = args;

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(31, service.GeneratedPersonId);
        Assert.Equal(DhhsFormDefinition.FormKey.AuthorizedRepresentative, service.GeneratedForm);
        Assert.True(service.GeneratedSelections!.Checks!["Guardianship"]);
        Assert.DoesNotContain("Power of Attorney", service.GeneratedSelections.Checks.Keys);
        Assert.Equal("Court order", service.GeneratedSelections.Text!["Other LA 1"]);
        Assert.NotNull(ready);
        Assert.Equal("form.pdf", ready!.SuggestedFileName);
        Assert.Contains("Social Security number", viewModel.BlankFieldsMessage);
    }

    [Fact]
    public void Changing_consumer_clears_every_consent_choice_in_both_forms()
    {
        var viewModel = new DhhsFormsViewModel(new RecordingDhhsFormService(false));
        viewModel.SetPerson(PersonFor(41, "First"));
        viewModel.ActiveConsentGroups[0].Checks[0].IsSelected = true;
        viewModel.ActiveConsentGroups[0].Text[0].Value = "First consumer's choice";

        viewModel.SelectedFormChoice = viewModel.FormChoices[1];
        viewModel.ActiveConsentGroups[0].Checks[0].IsSelected = true;
        viewModel.ActiveConsentGroups[0].Text[0].Value = "First consumer's recipient";

        viewModel.SetPerson(PersonFor(42, "Second"));

        foreach (var form in viewModel.FormChoices)
        {
            viewModel.SelectedFormChoice = form;
            Assert.All(
                viewModel.ActiveConsentGroups.SelectMany(group => group.Checks),
                option => Assert.False(option.IsSelected));
            Assert.All(
                viewModel.ActiveConsentGroups.SelectMany(group => group.Text),
                option => Assert.Equal(string.Empty, option.Value));
        }
    }

    [Fact]
    public async Task Valid_ssn_is_sent_once_but_only_the_mask_reaches_observable_state()
    {
        var service = new RecordingDhhsFormService(true);
        var viewModel = new DhhsFormsViewModel(service);
        viewModel.SetPerson(PersonFor(51, "Ssn"));

        await viewModel.SaveSsnAsync("123-45-6789");

        Assert.Equal(1, service.SsnUpdateCount);
        Assert.Equal("123456789", service.LastSsnUpdate);
        Assert.Equal("***-**-6789", viewModel.SsnMasked);
        Assert.DoesNotContain("12345", viewModel.SsnMasked);
        Assert.DoesNotContain("123456789", viewModel.SsnStatusMessage);
    }

    [Fact]
    public async Task Invalid_ssn_never_reaches_the_service()
    {
        var service = new RecordingDhhsFormService(true);
        var viewModel = new DhhsFormsViewModel(service);
        viewModel.SetPerson(PersonFor(61, "Invalid"));

        await viewModel.SaveSsnAsync("666-12-3456");

        Assert.Equal(0, service.SsnUpdateCount);
        Assert.Equal("Enter a structurally valid nine-digit SSN.", viewModel.SsnStatusMessage);
    }

    [Fact]
    public void Local_production_explains_that_ssn_storage_is_unavailable()
    {
        var viewModel = new DhhsFormsViewModel(new RecordingDhhsFormService(false));
        viewModel.SetPerson(PersonFor(71, "Local"));

        Assert.False(viewModel.CanUpdateSsn);
        Assert.Contains("Local Production", viewModel.SsnStorageExplanation);
        Assert.Equal("Not stored in local Production.", viewModel.SsnStatusMessage);
    }

    private static Person PersonFor(int id, string lastName)
    {
        var person = Person.CreatePerson(
            1,
            "Test",
            lastName,
            string.Empty,
            new DateTime(1980, 1, 1),
            DateTime.Today.AddYears(-1),
            WaiverType.Section21,
            new Settings());
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, id);
        person.AgencyId = 1;
        return person;
    }

    private sealed class RecordingDhhsFormService(bool supportsSsnStorage) : IDhhsFormService
    {
        public bool SupportsSsnStorage { get; } = supportsSsnStorage;

        // A path that can store can also reveal; the cloud path does neither from the
        // desktop's point of view, so one flag drives both here.
        public bool SupportsSsnReveal { get; } = supportsSsnStorage;
        public int SsnRevealCount { get; private set; }
        public int SsnUpdateCount { get; private set; }
        public string? LastSsnUpdate { get; private set; }
        public int? GeneratedPersonId { get; private set; }
        public DhhsFormDefinition.FormKey? GeneratedForm { get; private set; }
        public DhhsFormDefinition.Selections? GeneratedSelections { get; private set; }

        public Task<SsnStatusDto> GetSsnStatusAsync(int personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SsnStatusDto(SsnMask.NotOnFile, false));

        public Task<string> RevealSsnAsync(int personId, CancellationToken cancellationToken = default)
        {
            if (!SupportsSsnReveal)
                throw new InvalidOperationException("This path does not reveal stored numbers.");
            SsnRevealCount++;
            return Task.FromResult("123456789");
        }

        public Task<SsnStatusDto> UpdateSsnAsync(
            int personId,
            string? socialSecurityNumber,
            CancellationToken cancellationToken = default)
        {
            SsnUpdateCount++;
            LastSsnUpdate = socialSecurityNumber;
            return Task.FromResult(new SsnStatusDto(
                SsnMask.Format(SsnMask.LastFourOf(socialSecurityNumber)),
                socialSecurityNumber is not null));
        }

        public Task<DhhsFormResult> GenerateAsync(
            DhhsFormDefinition.FormKey form,
            int personId,
            DhhsFormDefinition.Selections selections,
            CancellationToken cancellationToken = default)
        {
            GeneratedForm = form;
            GeneratedPersonId = personId;
            GeneratedSelections = selections;
            return Task.FromResult(new DhhsFormResult(
                [1, 2, 3],
                "form.pdf",
                ["Individual's SSN"]));
        }
    }
}
