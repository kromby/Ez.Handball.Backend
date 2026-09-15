using Ez.Handball.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Services;

public class SettlementTriggerTests
{
    private readonly Mock<IHttpClientFactory> _httpFactory = new();

    private SettlementTrigger CreateSut() =>
        new(_httpFactory.Object, NullLogger<SettlementTrigger>.Instance);

    [Fact]
    public async Task PokeAsync_NoBaseUrlConfigured_SkipsWithoutCreatingAClient()
    {
        Environment.SetEnvironmentVariable("Settlement__ApiBaseUrl", null);

        await CreateSut().PokeAsync("103414");

        _httpFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PokeAsync_BaseUrlConfigured_CreatesAClientAndCompletes()
    {
        Environment.SetEnvironmentVariable("Settlement__ApiBaseUrl", "https://api.example.test");
        try
        {
            _httpFactory.Setup(f => f.CreateClient(string.Empty)).Returns(new HttpClient());

            await CreateSut().PokeAsync("103414");

            _httpFactory.Verify(f => f.CreateClient(string.Empty), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Settlement__ApiBaseUrl", null);
        }
    }
}
