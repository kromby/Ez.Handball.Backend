using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class TransferCountRecorderTests
{
    private readonly Mock<ITransferCountRepository> _repo = new();
    private readonly Mock<ILogger<TransferCountRecorder>> _logger = new();
    private TransferCountRecorder Sut() => new(_repo.Object, _logger.Object);

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task Record_IncrementsTheRoundCount()
    {
        await Sut().RecordAsync("u-1:fantasy", "4", TransferType.Buy, Now, CancellationToken.None);

        _repo.Verify(r => r.IncrementAsync("u-1:fantasy", "4", TransferType.Buy, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Record_WhenIncrementThrows_DoesNotFailTheTransfer_AndLogsAnError()
    {
        _repo.Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferType>(),
                                          It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("storage down"));

        await Sut().RecordAsync("u-1:fantasy", "4", TransferType.Buy, Now, CancellationToken.None);

        _logger.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
