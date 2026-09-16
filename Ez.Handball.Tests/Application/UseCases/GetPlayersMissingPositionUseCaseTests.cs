using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayersMissingPositionUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<ITournamentRepository> _tournaments = new();
    private readonly Mock<IMatchRepository> _matches = new();

    private GetPlayersMissingPositionUseCase CreateSut() => new(_players.Object, _tournaments.Object, _matches.Object);

    private static Player Plr(string id, string clubId, string gender = "karlar") =>
        new(PlayerId: id, Name: $"P{id}", JerseyNumber: null, DateOfBirth: null, Age: null,
            TeamId: $"{clubId}-{gender}", ClubId: clubId, ClubName: null, Gender: gender,
            Position: "", Retired: false);

    private static TournamentStatus Status(string tournamentId, bool active, bool ingest, string gender = "karlar") =>
        new(tournamentId, "Olís deild karla", gender, TournamentType.League, "olis-karla", "Olís deild karla",
            "2026-27", active, ingest, IngestHbStatz: true, Priority: 10);

    private static TournamentMatches Matches(string tournamentId, params (string HomeClubId, string AwayClubId)[] pairs) =>
        new(tournamentId, "Olís deild karla", "2026-27", pairs.Select(p => new MatchListItem(
            MatchId: Guid.NewGuid().ToString(),
            Round: "1",
            Date: DateTimeOffset.UtcNow,
            Venue: "",
            Status: "S",
            Home: new MatchListTeam(TeamId: $"{p.HomeClubId}-karlar", ClubId: p.HomeClubId, ClubName: null, LogoSrc: null, Score: 0),
            Away: new MatchListTeam(TeamId: $"{p.AwayClubId}-karlar", ClubId: p.AwayClubId, ClubName: null, LogoSrc: null, Score: 0),
            HbStatzSyncedAt: null)).ToList());

    [Fact]
    public async Task ExecuteAsync_ClubIdProvided_BypassesActivelyIngestedScoping_ReturnsRepositoryResultDirectly()
    {
        var expected = new List<Player> { Plr("1", "999") }; // "999" isn't in any ingested tournament
        _players
            .Setup(r => r.ListMissingPositionAsync("999", "karlar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync("999", "karlar", CancellationToken.None);

        Assert.Same(expected, result);
        _tournaments.Verify(t => t.ListAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoClubId_FiltersToClubsInActivelyIngestedTournaments()
    {
        _players
            .Setup(r => r.ListMissingPositionAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player> { Plr("1", "385"), Plr("2", "101") }); // 385 ingested, 101 (Fjölnir) not

        _tournaments
            .Setup(t => t.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TournamentStatus>
            {
                Status("9142", active: true, ingest: true),
                Status("8424", active: false, ingest: false) // Grill 66 deild karla — not ingested this season
            });

        _matches
            .Setup(m => m.ListByTournamentAsync("9142", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Matches("9142", ("385", "143")));

        var result = await CreateSut().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Equal(new[] { "1" }, result.Select(p => p.PlayerId).ToArray());
        _matches.Verify(m => m.ListByTournamentAsync("8424", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoClubId_TournamentNotActiveButIngested_IsExcludedFromScoping()
    {
        _players
            .Setup(r => r.ListMissingPositionAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player> { Plr("1", "385") });

        _tournaments
            .Setup(t => t.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TournamentStatus> { Status("8444", active: false, ingest: true) });

        var result = await CreateSut().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Empty(result);
        _matches.Verify(m => m.ListByTournamentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GenderProvided_OnlyScopesToTournamentsMatchingThatGender()
    {
        _players
            .Setup(r => r.ListMissingPositionAsync(null, "kvenna", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player> { Plr("1", "385", "kvenna") });

        _tournaments
            .Setup(t => t.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TournamentStatus>
            {
                Status("9142", active: true, ingest: true, gender: "karlar"), // wrong gender, excluded
                Status("8434", active: true, ingest: false, gender: "kvenna") // not ingested
            });

        var result = await CreateSut().ExecuteAsync(null, "kvenna", CancellationToken.None);

        Assert.Empty(result);
        _matches.Verify(m => m.ListByTournamentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TournamentRepositoryReturnsNullMatches_SkipsItWithoutThrowing()
    {
        _players
            .Setup(r => r.ListMissingPositionAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player> { Plr("1", "385") });

        _tournaments
            .Setup(t => t.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TournamentStatus> { Status("9142", active: true, ingest: true) });

        _matches
            .Setup(m => m.ListByTournamentAsync("9142", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Empty(result);
    }
}
