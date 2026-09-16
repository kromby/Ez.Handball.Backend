using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class TournamentScopeResolverTests
{
    private readonly Mock<ITournamentRepository> _tournaments = new();
    private readonly Mock<ISeasonRepository> _seasons = new();

    private TournamentScopeResolver CreateSut() => new(_tournaments.Object, _seasons.Object);

    private static Tournament T(string id, TournamentType type, string competitionId) =>
        new(id, $"name-{id}", "karlar", type, competitionId, $"comp-{competitionId}");

    private void SetupSeason(string season, params Tournament[] rows) =>
        _tournaments.Setup(r => r.ListBySeasonAsync(season, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows);

    [Fact]
    public async Task TournamentId_ReturnsThatIdOnly_WithoutQueryingRepo()
    {
        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: "8444", competitionId: null, type: null, default);

        Assert.Equal(new[] { "8444" }, ids);
        _tournaments.Verify(r => r.ListBySeasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoScope_ReturnsNull()
    {
        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: null, competitionId: null, type: null, default);

        Assert.Null(ids);
    }

    [Fact]
    public async Task Competition_ReturnsAllPhasesOfThatCompetition()
    {
        SetupSeason("2025-26",
            T("8444", TournamentType.League, "olis-karla"),
            T("8427", TournamentType.Playoffs, "olis-karla"),
            T("8437", TournamentType.Cup, "bikar-karla"));

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Equal(new[] { "8444", "8427" }, ids);
    }

    [Fact]
    public async Task CompetitionAndType_NarrowsToThatPhase()
    {
        SetupSeason("2025-26",
            T("8444", TournamentType.League, "olis-karla"),
            T("8427", TournamentType.Playoffs, "olis-karla"));

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: TournamentType.League, default);

        Assert.Equal(new[] { "8444" }, ids);
    }

    [Fact]
    public async Task TypeAlone_ReturnsAllTournamentsOfThatTypeAcrossCompetitions()
    {
        SetupSeason("2025-26",
            T("8437", TournamentType.Cup, "bikar-karla"),
            T("8436", TournamentType.Cup, "bikar-kvenna"),
            T("8444", TournamentType.League, "olis-karla"));

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: null, competitionId: null, type: TournamentType.Cup, default);

        Assert.Equal(new[] { "8437", "8436" }, ids);
    }

    [Fact]
    public async Task UnknownCompetition_ReturnsEmpty()
    {
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            "2025-26", tournamentId: null, competitionId: "does-not-exist", type: null, default);

        Assert.NotNull(ids);
        Assert.Empty(ids!);
    }

    [Fact]
    public async Task NullSeason_WithCompetition_ResolvesCurrentSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false), new("2025-26", true) });
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Equal(new[] { "8444" }, ids);
    }

    [Fact]
    public async Task NullSeason_NoCurrentSeason_ReturnsEmpty()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });

        var ids = await CreateSut().ResolveTournamentIdsAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.NotNull(ids);
        Assert.Empty(ids!);
    }

    // ── ResolveSeasonLabelAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ResolveSeasonLabelAsync_ExplicitSeason_ReturnedAsIs_NoRepoCall()
    {
        var label = await CreateSut().ResolveSeasonLabelAsync("2025-26", default);

        Assert.Equal("2025-26", label);
        _seasons.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveSeasonLabelAsync_NullSeason_ReturnsCurrentSeasonLabel()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false), new("2025-26", true) });

        var label = await CreateSut().ResolveSeasonLabelAsync(null, default);

        Assert.Equal("2025-26", label);
    }

    [Fact]
    public async Task ResolveSeasonLabelAsync_NullSeason_NoCurrentSeason_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });

        var label = await CreateSut().ResolveSeasonLabelAsync(null, default);

        Assert.Null(label);
    }

    // ── ResolvePreviousSeasonScopeAsync ─────────────────────────────────────────

    [Fact]
    public async Task PreviousScope_ExplicitCompetitionId_ResolvesPreviousSeasonTournaments()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Equal(new[] { "7777" }, scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_ExplicitTournamentId_TranslatesToCompetitionIdOfThatSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: "8444", competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Equal(new[] { "7777" }, scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_UnknownTournamentIdInCurrentSeason_ResolvesLabelButEmptyIds()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: "does-not-exist", competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.NotNull(scope.TournamentIds);
        Assert.Empty(scope.TournamentIds!);
    }

    [Fact]
    public async Task PreviousScope_NoNarrowing_ReturnsNullTournamentIds_WholeSeasonScan()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Null(scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_CurrentSeasonIsOldestTracked_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Null(scope);
    }

    [Fact]
    public async Task PreviousScope_NoCurrentSeasonResolvable_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season>());

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Null(scope);
    }

    [Fact]
    public async Task PreviousScope_NullSeason_ResolvesCurrentSeasonFirst()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
    }
}
