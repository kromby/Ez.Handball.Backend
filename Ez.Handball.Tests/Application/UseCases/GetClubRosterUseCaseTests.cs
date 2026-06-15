using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetClubRosterUseCaseTests
{
    private readonly Mock<IClubRepository> _clubs = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();

    private GetClubRosterUseCase CreateSut() => new(_clubs.Object, _players.Object, _scope.Object);

    private void ClubExists(string clubId, bool exists = true) =>
        _clubs.Setup(c => c.ExistsAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(exists);

    private void Season(string? label) =>
        _scope.Setup(s => s.ResolveSeasonLabelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(label);

    private void Roster(string clubId, params Player[] players) =>
        _players.Setup(p => p.ListByClubAsync(clubId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(players.ToList());

    private static Player P(string id, string name, string? jersey, string position = "VS", int? age = 25) =>
        new(id, name, jersey, null, age, "385-karlar", "385", "KR", "karlar", position, false);

    [Fact]
    public async Task ExecuteAsync_UnknownClub_ReturnsNotFound()
    {
        ClubExists("999", false);

        var result = await CreateSut().ExecuteAsync("999", default);

        Assert.IsType<GetClubRosterResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersByJerseyNumericThenBlankLast()
    {
        ClubExists("385");
        Season("2025-2026");
        Roster("385",
            P("a", "Zeta", "10"),
            P("b", "Alpha", null),
            P("c", "Beta", "2"));

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        Assert.Equal(new[] { "2", "10", null }, found.Roster.Players.Select(p => p.JerseyNumber).ToArray());
        Assert.Equal("2025-2026", found.Roster.Season);
        Assert.Equal("385", found.Roster.ClubId);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsPlayerFields()
    {
        ClubExists("385");
        Season("2025-2026");
        Roster("385", P("a", "Aron", "23", "MM", 35));

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        var p = Assert.Single(found.Roster.Players);
        Assert.Equal("a", p.PlayerId);
        Assert.Equal("Aron", p.Name);
        Assert.Equal("23", p.JerseyNumber);
        Assert.Equal("MM", p.Position);
        Assert.Equal(35, p.Age);
    }

    [Fact]
    public async Task ExecuteAsync_NoPlayers_ReturnsFoundWithEmptyRoster()
    {
        ClubExists("385");
        Season("2025-2026");
        Roster("385");

        var found = Assert.IsType<GetClubRosterResult.Found>(await CreateSut().ExecuteAsync("385", default));

        Assert.Empty(found.Roster.Players);
    }
}
