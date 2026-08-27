using Sati.Contracts.V1;

namespace Sati.ViewModels.ClientDocuments;

public enum ClientDocumentHubMode
{
    AuthorizedRepresentative,
    Releases
}

/// <summary>
/// A dashboard-level doorway into the existing per-client document workspaces.
/// It owns no form logic; both destinations share the exact view models used on
/// the Clients page.
/// </summary>
public sealed class ClientDocumentHubViewModel
{
    public ClientDocumentHubViewModel(
        NewClientViewModel clients,
        ClientDocumentHubMode mode)
    {
        Clients = clients;
        Mode = mode;
    }

    public NewClientViewModel Clients { get; }
    public ClientDocumentHubMode Mode { get; }
    public bool IsAuthorizedRepresentative =>
        Mode == ClientDocumentHubMode.AuthorizedRepresentative;
    public bool IsReleases => Mode == ClientDocumentHubMode.Releases;
    public string Title => IsAuthorizedRepresentative
        ? "DHHS Authorized Representative"
        : "Releases";
    public string Description => IsAuthorizedRepresentative
        ? "Prepare Maine DHHS's Appointment of Authorized Representative form for the selected consumer."
        : "Prepare either the official Maine DHHS release or Sati's agency release for the selected consumer.";

    public void Prepare()
    {
        var key = IsAuthorizedRepresentative
            ? DhhsFormDefinition.FormKey.AuthorizedRepresentative
            : DhhsFormDefinition.FormKey.AuthorizationToRelease;
        Clients.DhhsForms.SelectForm(key);
    }
}
