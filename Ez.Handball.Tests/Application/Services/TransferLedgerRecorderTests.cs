using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class TransferLedgerRecorderTests
{
    private readonly Mock<ITransferLedgerRepository> _repo = new();
    private TransferLedgerRecorder Sut() => new(_repo.Object, NullLogger<TransferLedgerRecorder>.Instance);

    private static readonly TransferEntry Sample =
        new("u-1", "p-1", GameFlavor.Fantasy, TransferType.Buy, 1_000, "2025-26", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Record_AppendsToRepository()
    {
        await Sut().RecordAsync(Sample, CancellationToken.None);
        _repo.Verify(r => r.AppendAsync(Sample, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Record_WhenAppendThrows_Swallows()
    {
        _repo.Setup(r => r.AppendAsync(It.IsAny<TransferEntry>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("storage down"));

        // Must not throw.
        await Sut().RecordAsync(Sample, CancellationToken.None);
    }
}
