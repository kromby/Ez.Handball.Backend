using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class ReparseFunctionTests
{
    private readonly Mock<IBlobArchiver> _blob = new();
    private readonly Mock<IMatchParser> _matchParser = new();
    private readonly Mock<IPlayerParser> _playerParser = new();

    private ReparseFunction CreateSut() =>
        new(_blob.Object, _matchParser.Object, _playerParser.Object);

    private void SetupList(string prefix, params string[] names) =>
        _blob.Setup(b => b.ListAsync(prefix, It.IsAny<CancellationToken>()))
             .Returns(ToAsync(names));

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReparseAsync_ParsesDetailsThenPlayers_AndCounts()
    {
        SetupList("matches/",
            "matches/100/details.json",
            "matches/100/players-385.json",
            "matches/100/players-390.json");
        _blob.Setup(b => b.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("{}");

        var order = new List<string>();
        _matchParser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Callback(() => order.Add("match"))
                    .Returns(Task.CompletedTask);
        _playerParser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Callback(() => order.Add("player"))
                     .Returns(Task.CompletedTask);

        var result = await CreateSut().ReparseAsync(null);

        Assert.Equal(1, result.MatchesReparsed);
        Assert.Equal(2, result.PlayerFilesReparsed);
        Assert.Empty(result.Errors);
        Assert.Equal("match", order[0]);
        Assert.All(order.Skip(1), s => Assert.Equal("player", s));
    }

    [Fact]
    public async Task ReparseAsync_ExtractsMatchIdAndClubIdFromPaths()
    {
        SetupList("matches/",
            "matches/100/details.json",
            "matches/100/players-385.json");
        _blob.Setup(b => b.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("{}");

        await CreateSut().ReparseAsync(null);

        _matchParser.Verify(p => p.ParseAsync("{}", "100", It.IsAny<CancellationToken>()), Times.Once);
        _playerParser.Verify(p => p.ParseAsync("{}", "100", "385", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReparseAsync_WithMatchId_ScopesTheListingPrefix()
    {
        SetupList("matches/100/",
            "matches/100/details.json",
            "matches/100/players-385.json");
        _blob.Setup(b => b.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("{}");

        await CreateSut().ReparseAsync("100");

        _blob.Verify(b => b.ListAsync("matches/100/", It.IsAny<CancellationToken>()), Times.Once);
        _blob.Verify(b => b.ListAsync("matches/", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReparseAsync_ParserThrows_CapturesErrorAndContinues()
    {
        SetupList("matches/",
            "matches/100/details.json",
            "matches/200/details.json",
            "matches/100/players-385.json");
        _blob.Setup(b => b.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("{}");

        _matchParser.Setup(p => p.ParseAsync("{}", "100", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("boom"));
        _matchParser.Setup(p => p.ParseAsync("{}", "200", It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        _playerParser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await CreateSut().ReparseAsync(null);

        Assert.Equal(1, result.MatchesReparsed);
        Assert.Equal(1, result.PlayerFilesReparsed);
        var error = Assert.Single(result.Errors);
        Assert.Equal("matches/100/details.json", error.Blob);
        Assert.Contains("boom", error.Message);
    }
}
