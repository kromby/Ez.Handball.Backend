using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class TriggerHbStatzSyncUseCaseTests
{
    private readonly Mock<IIngestionTrigger> _trigger = new();

    private TriggerHbStatzSyncUseCase CreateSut() => new(_trigger.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsWhateverTheTriggerReturns()
    {
        var expected = new HbStatzSyncTriggerResult(true, 5, 4, new List<string> { "999" }, Array.Empty<string>(), null);
        _trigger.Setup(t => t.TriggerHbStatzSyncAsync("9142", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync("9142", null, null, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_NullTournamentId_PassesNullThrough()
    {
        var expected = new HbStatzSyncTriggerResult(true, 0, 0, Array.Empty<string>(), Array.Empty<string>(), null);
        _trigger.Setup(t => t.TriggerHbStatzSyncAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        await CreateSut().ExecuteAsync(null, null, null, CancellationToken.None);

        _trigger.Verify(t => t.TriggerHbStatzSyncAsync(null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PassesRoundAndMatchIdThrough()
    {
        var expected = new HbStatzSyncTriggerResult(true, 1, 1, Array.Empty<string>(), Array.Empty<string>(), null);
        _trigger.Setup(t => t.TriggerHbStatzSyncAsync("9142", "3", "103414", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync("9142", "3", "103414", CancellationToken.None);

        Assert.Same(expected, result);
    }
}
