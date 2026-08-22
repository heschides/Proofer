using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Carika.Services;

internal sealed class CarikaApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public CarikaApiClient(Uri baseAddress)
    {
        if (baseAddress.Scheme != Uri.UriSchemeHttps && !baseAddress.IsLoopback)
            throw new ArgumentException("Carika requires HTTPS except for loopback development.", nameof(baseAddress));
        _http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(username, password), Json, ct);
        var login = await ReadAsync<LoginResponse>(response, ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return login;
    }

    public async Task<IReadOnlyList<PersonDto>> GetCaseloadAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/api/v1/caseload", ct);
        return await ReadAsync<List<PersonDto>>(response, ct);
    }

    public async Task<NoteDto> SaveNoteAsync(SaveNoteRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("/api/v1/notes", request, Json, ct);
        return await ReadAsync<NoteDto>(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                ?? throw new InvalidOperationException("The API returned an empty response.");
        ApiErrorDto? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(Json, ct); } catch { }
        throw new InvalidOperationException(error?.Message ?? $"The API request failed ({(int)response.StatusCode}).");
    }

    public void Dispose()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        _http.Dispose();
    }
}
