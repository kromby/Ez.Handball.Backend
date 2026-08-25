using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class TriggerHbStatzSyncFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();
    private readonly Mock<IHbStatzApiClient> _hbStatzClient = new();

    private TriggerHbStatzSyncFunction CreateSut() =>
        new(_tableWriter.Object, _blobArchiver.Object, _hbStatzClient.Object);

    private static TournamentEntity Tournament(string competitionId = "olis-karla", bool ingestHbStatz = true) => new()
    {
        PartitionKey = "2025-26", RowKey = "9142", CompetitionId = competitionId, IngestHbStatz = ingestHbStatz
    };

    private static MatchEntity Match(string id, DateTimeOffset? date = null) => new()
    {
        PartitionKey = "9142", RowKey = id,
        HomeTeamId = "385-karlar", AwayTeamId = "390-karlar",
        Status = "S", Date = date ?? new DateTimeOffset(2026, 5, 7, 18, 30, 0, TimeSpan.Zero)
    };

    private const string FixturesJson = """
    {
      "fixtures": [
        { "game_id": 12924, "date": "2026-05-07 18:29:48",
          "home": { "name": "Stjarnan" }, "away": { "name": "Breiðablik" },
          "played": true, "has_hbs": true }
      ]
    }
    """;

    private const string GameJson = """
    {
      "players": {
        "home": [ { "player_id": 803, "name": "Arnór Snær Óskarsson", "number": 6, "goals": 9, "shots": 14, "assists": 2, "turnovers": 3, "steals": 0, "blocks": 0, "legal_stops": 2, "grade_total": 8.78 } ],
        "away": []
      }
    }
    """;

    private void SetupTournamentQuery(string filter, params TournamentEntity[] tournaments) =>
        _tableWriter.Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tournaments.ToList());

    private void SetupMatches(params MatchEntity[] matches) =>
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>(
                "Matches", "PartitionKey eq '9142' and Status eq 'S'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches.ToList());

    private void SetupClubs() =>
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "385", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "385", Name = "Stjarnan" });
    // Away club intentionally left unmocked in some tests; set explicitly when needed.

    [Fact]
    public async Task SyncAsync_UnmappedCompetition_SkipsTournamentWithoutFetching()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament(competitionId: "grill66-karla"));

        var result = await CreateSut().SyncAsync(null);

        Assert.Equal(0, result.MatchesChecked);
        _hbStatzClient.Verify(c => c.GetFixturesJsonAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_AlreadySyncedMatch_IsSkipped()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        var synced = Match("103414");
        synced.HbStatzSyncedAt = DateTimeOffset.UtcNow;
        SetupMatches(synced);

        var result = await CreateSut().SyncAsync(null);

        Assert.Equal(0, result.MatchesChecked);
        Assert.Equal(0, result.MatchesSynced);
    }

    [Fact]
    public async Task SyncAsync_FixturesFetchThrows_RecordsTournamentAsFailed()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateSut().SyncAsync(null);

        Assert.Contains("tournament:9142", result.Failed);
        Assert.Equal(0, result.MatchesChecked);
    }

    [Fact]
    public async Task SyncAsync_NoFixtureMatch_ReportsUnmatched()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        SetupMatches(Match("999999", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });

        var result = await CreateSut().SyncAsync(null);

        Assert.Equal(1, result.MatchesChecked);
        Assert.Equal(0, result.MatchesSynced);
        Assert.Contains("999999", result.Unmatched);
    }

    [Fact]
    public async Task SyncAsync_MatchedFixture_ArchivesRawJson_MergesStats_PreservesExistingFields_AndMarksSynced()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _hbStatzClient.Setup(c => c.GetGameJsonAsync(12924, It.IsAny<CancellationToken>())).ReturnsAsync(GameJson);
        var match = Match("103414");
        SetupMatches(match);
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór Snær Óskarsson", JerseyNumber = "6" } });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        // Existing HSÍ-sourced row — the goals/tournament/season fields here must survive.
        var existingStat = new PlayerStatEntity
        {
            PartitionKey = "103414", RowKey = "hsi-1", Goals = 9, YellowCards = 0,
            TournamentId = "9142", Season = "2025-26", TeamId = "385-karlar", ClubName = "Stjarnan"
        };
        _tableWriter.Setup(t => t.GetAsync<PlayerStatEntity>("PlayerStats", "103414", "hsi-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingStat);

        var result = await CreateSut().SyncAsync(null);

        Assert.Equal(1, result.MatchesChecked);
        Assert.Equal(1, result.MatchesSynced);
        Assert.Empty(result.Unmatched);
        Assert.Empty(result.Failed);

        _blobArchiver.Verify(b => b.SaveAsync("hbstatz/matches/103414.json", GameJson, It.IsAny<CancellationToken>()), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(e =>
                e.RowKey == "hsi-1" &&
                e.Goals == 9 && e.TournamentId == "9142" && e.Season == "2025-26" && // preserved
                e.HbStatzAssists == 2 && e.HbStatzGradeTotal == 8.78),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync("Matches",
            It.Is<MatchEntity>(e => e.RowKey == "103414" && e.HbStatzSyncedAt != null),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_UnreconcilablePlayer_IsSkippedWithoutThrowing()
    {
        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _hbStatzClient.Setup(c => c.GetGameJsonAsync(12924, It.IsAny<CancellationToken>())).ReturnsAsync(GameJson);
        SetupMatches(Match("103414"));
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });
        // Roster has nobody matching "Arnór Snær Óskarsson" / #6.
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().SyncAsync(null);

        Assert.Equal(1, result.MatchesSynced); // match itself still counts as synced
        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats", It.IsAny<PlayerStatEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_SpecificTournamentIdParam_ScopesTheQuery()
    {
        SetupTournamentQuery("RowKey eq '9142' and IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"fixtures":[]}""");
        SetupMatches();

        await CreateSut().SyncAsync("9142");

        _tableWriter.Verify(t => t.QueryAsync<TournamentEntity>(
            "Tournaments", "RowKey eq '9142' and IngestHbStatz eq true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_MatchIdScope_ForcesResyncEvenIfAlreadySynced()
    {
        SetupTournamentQuery("RowKey eq '9142' and IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _hbStatzClient.Setup(c => c.GetGameJsonAsync(12924, It.IsAny<CancellationToken>())).ReturnsAsync(GameJson);
        var alreadySynced = Match("103414");
        alreadySynced.HbStatzSyncedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _tableWriter.Setup(t => t.GetAsync<MatchEntity>("Matches", "9142", "103414", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadySynced);
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().SyncAsync("9142", matchId: "103414");

        Assert.Equal(1, result.MatchesChecked);
        Assert.Equal(1, result.MatchesSynced);
        // The default-sweep "Status eq 'S'" scan is never used for a matchId-scoped sync.
        _tableWriter.Verify(t => t.QueryAsync<MatchEntity>(
            "Matches", "PartitionKey eq '9142' and Status eq 'S'", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_MatchIdScope_UnknownMatch_ReturnsZeroChecked()
    {
        SetupTournamentQuery("RowKey eq '9142' and IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _tableWriter.Setup(t => t.GetAsync<MatchEntity>("Matches", "9142", "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchEntity?)null);

        var result = await CreateSut().SyncAsync("9142", matchId: "nope");

        Assert.Equal(0, result.MatchesChecked);
    }

    [Fact]
    public async Task SyncAsync_RoundScope_ForcesResyncForEveryMatchInThatRoundOnly()
    {
        SetupTournamentQuery("RowKey eq '9142' and IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _hbStatzClient.Setup(c => c.GetGameJsonAsync(12924, It.IsAny<CancellationToken>())).ReturnsAsync(GameJson);
        var roundMatch = Match("103414");
        roundMatch.HbStatzSyncedAt = DateTimeOffset.UtcNow.AddDays(-1); // already synced — round scope still re-runs it
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>(
                "Matches", "PartitionKey eq '9142' and Status eq 'S' and Round eq '3'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity> { roundMatch });
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().SyncAsync("9142", round: "3");

        Assert.Equal(1, result.MatchesChecked);
        Assert.Equal(1, result.MatchesSynced);
    }
}
