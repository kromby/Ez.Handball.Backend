using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetClubRosterUseCaseTests
{
    private readonly Mock<IClubRepository> _clubs = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IGetPlayerPoolUseCase> _pool = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();

    private GetClubRosterUseCase CreateSut() => new(_clubs.Object, _players.Object, _pool.Object, _scope.Object);

    private void ClubExists(string clubId, bool exists = true) =>
        _clubs.Setup(c => c.ExistsAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(exists);

    private void Season(string? label) =>
        _scope.Setup(s => s.ResolveSeasonLabelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(label);

    private void Pool(string clubId, params PlayerPoolEntry[] entries) =>
        _pool.Setup(p => p.ExecuteAsync(
                    It.Is<PlayerPoolRequest>(r => r.ClubId == clubId), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
             .ReturnsAsync(new PlayerPoolResult.Found(
                 new PlayerPool("Rating", entries.Length, 0, entries.Length, entries.ToList())));

    private void Profile(string id, string? jersey, int? age) =>
        _players.Setup(p => p.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Player(id, "Name " + id, jersey, null, age, "1-karlar", "1", "Old",
                    "karlar", "VS", false));

    private static PlayerPoolEntry E(string id, string name = "N", string position = "VS", int goals = 0) =>
        new(0, id, name, "385", "KR", "karlar", position, 3, goals, 0, 0, 0, 0,
            new PlayerPrice(1_000_000, "ISK"), 10, null);

    [Fact]
    public async Task ExecuteAsync_UnknownClub_ReturnsNotFound()
    {
        ClubExists("999", false);

        var result = await CreateSut().ExecuteAsync("999", default);

        Assert.IsType<GetClubRosterResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_QueriesCurrentSeasonPoolForClub()
    {
        ClubExists("385");
        Season("2026-27");
        Pool("385");

        await CreateSut().ExecuteAsync("385", default);

        _pool.Verify(p => p.ExecuteAsync(
            It.Is<PlayerPoolRequest>(r =>
                r.ClubId == "385" && r.Season == "2026-27" && r.TournamentId == null
                && r.CompetitionId == null && r.Type == null && r.Gender == null
                && r.Position == null && r.Name == null && r.PriceVersion == 1),
            0, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersByJerseyNumericThenBlankLast()
    {
        ClubExists("385");
        Season("2025-2026");
        Pool("385", E("a", "Zeta"), E("b", "Alpha"), E("c", "Beta"));
        Profile("a", "10", 25);
        Profile("b", null, 25);
        Profile("c", "2", 25);

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        Assert.Equal(new[] { "2", "10", null }, found.Roster.Players.Select(p => p.JerseyNumber).ToArray());
        Assert.Equal("2025-2026", found.Roster.Season);
        Assert.Equal("385", found.Roster.ClubId);
    }

    [Fact]
    public async Task ExecuteAsync_CarriesPoolDataPlusJerseyAndAge()
    {
        ClubExists("385");
        Season("2025-2026");
        var entry = E("a", "Aron", "MM", goals: 12) with { Assists = 4, GradeTotal = 7.5 };
        Pool("385", entry);
        Profile("a", "23", 35);

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        var p = Assert.Single(found.Roster.Players);
        Assert.Equal("a", p.PlayerId);
        Assert.Equal("Aron", p.Name);
        Assert.Equal("MM", p.Position);
        Assert.Equal(12, p.Goals);
        Assert.Equal(4, p.Assists);
        Assert.Equal(7.5, p.GradeTotal);
        Assert.Equal(entry.Price, p.Price);
        Assert.Equal("23", p.JerseyNumber);
        Assert.Equal(35, p.Age);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPlayersRow_KeepsEntryWithoutJerseyOrAge()
    {
        ClubExists("385");
        Season("2025-2026");
        Pool("385", E("a", "Aron"));

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        var p = Assert.Single(found.Roster.Players);
        Assert.Null(p.JerseyNumber);
        Assert.Null(p.Age);
    }

    [Fact]
    public async Task ExecuteAsync_NoPlayers_ReturnsFoundWithEmptyRoster()
    {
        ClubExists("385");
        Season("2025-2026");
        Pool("385");

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        Assert.Empty(found.Roster.Players);
    }

    [Fact]
    public async Task ExecuteAsync_PoolRuleSetMissing_ReturnsRuleSetNotFound()
    {
        ClubExists("385");
        Season("2025-2026");
        _pool.Setup(p => p.ExecuteAsync(
                    It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(PlayerPoolResult.RuleSetNotFound.Instance);

        var result = await CreateSut().ExecuteAsync("385", default);

        Assert.IsType<GetClubRosterResult.RuleSetNotFound>(result);
    }
}
