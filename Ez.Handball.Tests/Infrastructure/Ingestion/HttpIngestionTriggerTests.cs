using System.Net;
using Ez.Handball.Infrastructure.Ingestion;

namespace Ez.Handball.Tests.Infrastructure.Ingestion;

public class HttpIngestionTriggerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");
    }

    private static HttpIngestionTrigger CreateSut(HttpMessageHandler handler, string? functionKey = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7071") };
        return new HttpIngestionTrigger(client, new IngestionSettings("http://localhost:7071", functionKey));
    }

    [Fact]
    public async Task TriggerSyncAsync_PostsToApiSync_AndParsesTheResult()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"synced":6,"failed":["8437"]}""")
        });

        var result = await CreateSut(handler).TriggerSyncAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(6, result.Synced);
        Assert.Equal(new[] { "8437" }, result.Failed);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://localhost:7071/api/sync", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerSyncAsync_WithFunctionKey_SendsItAsAHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"synced":0,"failed":[]}""")
        });

        await CreateSut(handler, functionKey: "secret-key").TriggerSyncAsync(CancellationToken.None);

        Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("x-functions-key").Single());
    }

    [Fact]
    public async Task TriggerSyncAsync_WithoutFunctionKey_OmitsTheHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"synced":0,"failed":[]}""")
        });

        await CreateSut(handler).TriggerSyncAsync(CancellationToken.None);

        Assert.False(handler.LastRequest!.Headers.Contains("x-functions-key"));
    }

    [Fact]
    public async Task TriggerSyncAsync_NonSuccessStatus_ReturnsFailureWithStatusCode()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await CreateSut(handler).TriggerSyncAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ingestion_returned_401", result.Error);
    }

    [Fact]
    public async Task TriggerSyncAsync_ConnectionRefused_ReturnsUnreachable()
    {
        var result = await CreateSut(new ThrowingHandler()).TriggerSyncAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ingestion_unreachable", result.Error);
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_PostsToApiHbstatzSync_AndParsesTheResult()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"matchesChecked":5,"matchesSynced":4,"unmatched":["999"],"failed":[]}""")
        });

        var result = await CreateSut(handler).TriggerHbStatzSyncAsync(null, null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(5, result.MatchesChecked);
        Assert.Equal(4, result.MatchesSynced);
        Assert.Equal(new[] { "999" }, result.Unmatched);
        Assert.Equal("http://localhost:7071/api/hbstatz/sync", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_WithTournamentId_AppendsQueryParam()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"matchesChecked":0,"matchesSynced":0,"unmatched":[],"failed":[]}""")
        });

        await CreateSut(handler).TriggerHbStatzSyncAsync("9142", null, null, CancellationToken.None);

        Assert.Equal("http://localhost:7071/api/hbstatz/sync?tournamentId=9142", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_WithRound_AppendsBothQueryParams()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"matchesChecked":0,"matchesSynced":0,"unmatched":[],"failed":[]}""")
        });

        await CreateSut(handler).TriggerHbStatzSyncAsync("9142", "3", null, CancellationToken.None);

        Assert.Equal("http://localhost:7071/api/hbstatz/sync?tournamentId=9142&round=3", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_WithMatchId_AppendsBothQueryParams()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"matchesChecked":0,"matchesSynced":0,"unmatched":[],"failed":[]}""")
        });

        await CreateSut(handler).TriggerHbStatzSyncAsync("9142", null, "103414", CancellationToken.None);

        Assert.Equal("http://localhost:7071/api/hbstatz/sync?tournamentId=9142&matchId=103414", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_NonSuccessStatus_ReturnsFailureWithStatusCode()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await CreateSut(handler).TriggerHbStatzSyncAsync(null, null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ingestion_returned_401", result.Error);
    }

    [Fact]
    public async Task TriggerHbStatzSyncAsync_ConnectionRefused_ReturnsUnreachable()
    {
        var result = await CreateSut(new ThrowingHandler()).TriggerHbStatzSyncAsync(null, null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ingestion_unreachable", result.Error);
    }
}
