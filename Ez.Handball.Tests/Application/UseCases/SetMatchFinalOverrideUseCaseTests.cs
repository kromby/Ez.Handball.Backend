using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetMatchFinalOverrideUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IMatchFinalOverrideRepository> _overrides = new();

    private SetMatchFinalOverrideUseCase Sut() => new(_matches.Object, _overrides.Object, () => Now);

    private static MatchInfo Match(string matchId, string tournamentId) => new(
        matchId, tournamentId, "Olís deild karla", "2026-27", Now.AddDays(-6), "N1 höllin", null, "U",
        new MatchTeamInfo("453-karlar", "453", "Valur", new LineScore(14, 19, 33)),
        new MatchTeamInfo("221-karlar", "221", "KA", new LineScore(22, 17, 39)));

    [Fact]
    public async Task UnknownMatch_ReturnsMatchNotFound_AndWritesNothing()
    {
        _matches.Setup(m => m.GetByIdAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((MatchInfo?)null);

        var result = await Sut().ExecuteAsync("nope", true, "admin-1", default);

        Assert.IsType<SetMatchFinalOverrideResult.MatchNotFound>(result);
        _overrides.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_StoresOverrideUnderTheMatchesTournament()
    {
        _matches.Setup(m => m.GetByIdAsync("111453", It.IsAny<CancellationToken>())).ReturnsAsync(Match("111453", "9142"));

        var result = await Sut().ExecuteAsync("111453", true, "admin-1", default);

        Assert.Equal(new SetMatchFinalOverrideResult.Ok("9142", "111453", true), result);
        _overrides.Verify(o => o.SetAsync("9142", "111453", "admin-1", Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clear_RemovesTheOverride()
    {
        _matches.Setup(m => m.GetByIdAsync("111453", It.IsAny<CancellationToken>())).ReturnsAsync(Match("111453", "9142"));

        var result = await Sut().ExecuteAsync("111453", false, "admin-1", default);

        Assert.Equal(new SetMatchFinalOverrideResult.Ok("9142", "111453", false), result);
        _overrides.Verify(o => o.ClearAsync("9142", "111453", It.IsAny<CancellationToken>()), Times.Once);
        _overrides.Verify(o => o.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
