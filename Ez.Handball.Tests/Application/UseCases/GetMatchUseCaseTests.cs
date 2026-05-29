using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMatchUseCaseTests
{
    private readonly Mock<IMatchRepository> _matches = new();

    private GetMatchUseCase CreateSut() => new(_matches.Object);

    private static MatchDetail AnyMatch(string id) => new(
        MatchId: id, TournamentId: "8444", TournamentName: "Olís deild karla", Season: "2025-26",
        Date: DateTimeOffset.UnixEpoch, Venue: "Ásgarður", Attendance: 412, Status: "S",
        HomeTeam: new MatchTeam("385-karlar", "385", "Stjarnan",
            new LineScore(14, 14, 28), Array.Empty<MatchPlayerLine>()),
        AwayTeam: new MatchTeam("390-karlar", "390", "Breiðablik",
            new LineScore(12, 13, 25), Array.Empty<MatchPlayerLine>()));

    [Fact]
    public async Task ExecuteAsync_MatchMissing_ReturnsNotFound()
    {
        _matches.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchDetail?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetMatchResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_MatchExists_ReturnsFoundPassthrough()
    {
        var match = AnyMatch("103414");
        _matches.Setup(r => r.GetByIdAsync("103414", It.IsAny<CancellationToken>()))
                .ReturnsAsync(match);

        var result = await CreateSut().ExecuteAsync("103414", CancellationToken.None);

        var found = Assert.IsType<GetMatchResult.Found>(result);
        Assert.Same(match, found.Match);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _matches.Setup(r => r.GetByIdAsync("103414", cts.Token))
                .ReturnsAsync(AnyMatch("103414"));

        await CreateSut().ExecuteAsync("103414", cts.Token);

        _matches.Verify(r => r.GetByIdAsync("103414", cts.Token), Times.Once);
    }
}
