using Sati.Services;

namespace Sati.Data.Cloud;

/// <summary>
/// Forwards <see cref="CloudApiClient.SessionEnded"/> to the shell as the transport-free
/// <see cref="ISessionLifetime"/>. Keeping the adapter here rather than letting a window
/// subscribe to the API client directly is what stops the view layer from taking a
/// dependency on the cloud transport.
/// </summary>
public sealed class CloudSessionLifetime : ISessionLifetime
{
    private readonly CloudApiClient _api;

    public CloudSessionLifetime(CloudApiClient api)
    {
        _api = api;
        _api.SessionEnded += (sender, args) => SessionEnded?.Invoke(this, args);
    }

    public event EventHandler? SessionEnded;
}
