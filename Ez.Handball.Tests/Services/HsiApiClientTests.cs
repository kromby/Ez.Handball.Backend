using System.Net;
using System.Text;
using Ez.Handball.Ingestion.Services;
using Xunit;

namespace Ez.Handball.Tests.Services;

public class HsiApiClientTests
{
    private static (HsiApiClient client, List<HttpRequestMessage> requests) CreateClient(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpHandler(responseBody, status, requests);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://hsi.is") };
        return (new HsiApiClient(http), requests);
    }

    [Fact]
    public async Task GetTournamentMatchesJsonAsync_ReturnsResponseBody()
    {
        var expected = """{"data":[]}""";
        var (client, _) = CreateClient(expected);

        var result = await client.GetTournamentMatchesJsonAsync("8444");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetMatchDetailsJsonAsync_ReturnsResponseBody()
    {
        var expected = """{"data":{"GAME_ID":"103414"}}""";
        var (client, _) = CreateClient(expected);

        var result = await client.GetMatchDetailsJsonAsync("103414");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetMatchPlayerStatsJsonAsync_ReturnsResponseBody()
    {
        var expected = """{"data":[]}""";
        var (client, _) = CreateClient(expected);

        var result = await client.GetMatchPlayerStatsJsonAsync("103414", "385");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetTournamentMatchesJsonAsync_SetsAcceptHeader()
    {
        var (client, requests) = CreateClient("""{"data":[]}""");

        await client.GetTournamentMatchesJsonAsync("8444");

        var accept = requests[0].Headers.Accept.ToString();
        Assert.Contains("text/html", accept);
    }

    [Fact]
    public async Task GetTournamentMatchesJsonAsync_ThrowsOnNonSuccess()
    {
        var (client, _) = CreateClient("Not Found", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetTournamentMatchesJsonAsync("9999"));
    }

    private class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        private readonly List<HttpRequestMessage> _requests;

        public StubHttpHandler(string body, HttpStatusCode status, List<HttpRequestMessage> requests)
        {
            _body = body;
            _status = status;
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
