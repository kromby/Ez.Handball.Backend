using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class TriggerIngestionSyncUseCaseTests
{
    private readonly Mock<IIngestionTrigger> _trigger = new();

    private TriggerIngestionSyncUseCase CreateSut() => new(_trigger.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsWhateverTheTriggerReturns()
    {
        var expected = new SyncTriggerResult(true, 6, Array.Empty<string>(), null);
        _trigger.Setup(t => t.TriggerSyncAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }
}
