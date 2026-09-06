using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Xunit;

namespace Sati.Tests;

public sealed class ChatClientTests
{
    [Theory]
    [InlineData("http://localhost:9000")]
    [InlineData("ws://example.test")]
    public void SocketRejectsInsecureSources(string uri) => Assert.Throws<InvalidOperationException>(() => CloudApiClient.ChatSocketAddress(new(uri)));

    [Fact]
    public void SocketUsesSecureFixedPathWithoutQueryStringCredentials()
    {
        var address = CloudApiClient.ChatSocketAddress(new("https://demo.example.test:443"));
        Assert.Equal("wss", address.Scheme);
        Assert.Equal("/api/v1/chat/stream", address.AbsolutePath);
        Assert.Empty(address.Query);
    }

    [Fact]
    public async Task PassiveChatReadsDoNotRenewAnExpiringSession()
    {
        var handler = new RecordingHandler();
        var api = new CloudApiClient(new HttpClient(handler) { BaseAddress = new("https://demo.example.test") });
        api.SetAccessToken("near-expiry-test-token", DateTimeOffset.UtcNow.AddMinutes(1));
        var service = new CloudChatService(api);
        await service.GetHistoryAsync(7, long.MaxValue);
        await service.GetMessagesAsync(7, 42);
        Assert.DoesNotContain("/api/v1/auth/renew", handler.Paths);
        Assert.All(handler.Authorization, value => Assert.Equal("Bearer near-expiry-test-token", value));
        Assert.False(api.HasSessionEnded);
    }

    [Fact]
    public async Task ServiceUsesDurableCursorAndSameSendIdentityAndCurrentBearer()
    {
        var handler = new RecordingHandler();
        var api = new CloudApiClient(new HttpClient(handler) { BaseAddress = new("https://demo.example.test") });
        var service = new CloudChatService(api);
        api.SetAccessToken("first-test-token");
        await service.GetMessagesAsync(7, 42);
        Assert.Equal("/api/v1/chat/rooms/7/messages?afterSequence=42&take=100", handler.Paths[0]);
        api.SetAccessToken("renewed-test-token");
        var request = new PostChatMessageRequest(4, Guid.NewGuid(), "Synthetic message");
        await service.PostMessageAsync(7, request);
        await service.PostMessageAsync(7, request);
        Assert.Equal("Bearer first-test-token", handler.Authorization[0]);
        Assert.Equal("Bearer renewed-test-token", handler.Authorization[1]);
        Assert.Equal(handler.Bodies[1], handler.Bodies[2]);
        Assert.Contains(request.ClientMessageId.ToString(), handler.Bodies[1]);
        Assert.DoesNotContain("test-token", handler.Paths[1]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string?> Authorization { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Authorization.Add(request.Headers.Authorization?.ToString());
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            object result = request.RequestUri.AbsolutePath == "/api/v1/auth/renew"
                ? new SessionRenewalResponse("unexpected-renewal", DateTimeOffset.UtcNow.AddMinutes(30))
                : request.Method == HttpMethod.Get
                ? new ChatPageDto([], 42, false, 4)
                : ChatViewModelTests.Message(7, "Synthetic message");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
        }
    }
}
