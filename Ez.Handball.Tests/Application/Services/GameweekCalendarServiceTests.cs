using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekCalendarServiceTests
{
    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IGameweekLockRepository> _locks = new();
    private DateTimeOffset _now = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private GameweekCalendarService CreateSut() => new(_matches.Object, _locks.Object, new StubTimeProvider(_now));

    private static readonly GameweekConfig Config = new(
        Version: 1, TournamentId: "8444", LockOffsetHours: 1,
        ScoringRuleSetVersion: 1, LineupConstraintsVersion: 1, MatchFinalBufferHours: 3);

    private static MatchListItem M(string id, string round, DateTimeOffset date, string status) =>
        new(id, round, date, Venue: null, status,
            Home: new MatchListTeam($"h{id}", "1", "Home", null, 0),
            Away: new MatchListTeam($"a{id}", "2", "Away", null, 0));

    private void SetupMatches(params MatchListItem[] items) =>
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TournamentMatches("8444", "Olís deild karla", "2025-26", items));

    [Fact]
    public async Task UnknownTournament_ReturnsNull()
    {
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentMatches?)null);

        Assert.Null(await CreateSut().GetCalendarAsync(Config, default));
    }

    [Fact]
    public async Task GroupsByRound_NumbersInSortedOrder_DeadlineIsEarliestMinusOffset()
    {
        var r2a = new DateTimeOffset(2026, 1, 20, 18, 0, 0, TimeSpan.Zero);
        var r1a = new DateTimeOffset(2026, 1, 13, 19, 0, 0, TimeSpan.Zero);
        var r1b = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero); // earliest in round 1
        SetupMatches(
            M("103", "2", r2a, "O"),
            M("101", "1", r1a, "O"),
            M("102", "1", r1b, "O"));

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.NotNull(cal);
        Assert.Equal(2, cal!.Count);
        Assert.Equal(1, cal[0].Number);
        Assert.Equal("1", cal[0].RoundLabel);
        Assert.Equal(r1b.AddHours(-1), cal[0].Deadline);   // earliest throw-off − 1h
        Assert.Equal(2, cal[1].Number);
        Assert.Equal("2", cal[1].RoundLabel);
    }

    [Fact]
    public async Task PinnedDeadline_OverridesDerived()
    {
        var date = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero);
        var pinned = new DateTimeOffset(2026, 1, 13, 15, 30, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", date, "O"));
        _locks.Setup(l => l.GetPinnedDeadlineAsync("8444", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pinned);

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.Equal(pinned, cal![0].Deadline);
    }

    [Fact]
    public async Task Status_Open_When_NowBeforeDeadline()
    {
        SetupMatches(M("101", "1", _now.AddDays(2), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.Open, cal![0].Status);
    }

    [Fact]
    public async Task Status_DeadlineLocked_When_PastDeadline_NoneFinal()
    {
        _now = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", _now.AddMinutes(30), "O")); // deadline = (now+30m)-1h = now-30m → passed
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.DeadlineLocked, cal![0].Status);
    }

    [Fact]
    public async Task Status_InPlay_When_SomeButNotAllFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        SetupMatches(
            M("101", "1", _now.AddDays(-1), "S"),
            M("102", "1", _now.AddHours(-1), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.InPlay, cal![0].Status);
    }

    [Fact]
    public async Task Status_Settled_When_AllFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        SetupMatches(
            M("101", "1", _now.AddDays(-1), "S"),
            M("102", "1", _now.AddDays(-1), "S"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.Settled, cal![0].Status);
    }

    [Fact]
    public async Task NonNumericRounds_SortAfterNumeric()
    {
        SetupMatches(
            M("201", "Undanúrslit", _now.AddDays(30), "O"),
            M("101", "1", _now.AddDays(2), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal("1", cal![0].RoundLabel);
        Assert.Equal("Undanúrslit", cal[1].RoundLabel);
    }

    [Fact]
    public async Task FinishedMatch_WithinBuffer_NotYetFinal_RoundStaysDeadlineLocked()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        // Throw-off 1h ago, status "S" already, but buffer is 3h → date+buffer = now+2h > now → not final.
        // deadline = (now-1h) - 1h lockOffset = now-2h → passed, and 0 final → DeadlineLocked.
        SetupMatches(M("101", "1", _now.AddHours(-1), "S"));

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.False(cal![0].Matches[0].IsFinal);
        Assert.Equal(GameweekStatus.DeadlineLocked, cal[0].Status);
    }

    [Fact]
    public async Task FinishedMatch_AtBufferBoundary_IsFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        // Throw-off exactly 3h ago → date+buffer == now → "<= now" is true → final.
        SetupMatches(M("101", "1", _now.AddHours(-3), "S"));

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.True(cal![0].Matches[0].IsFinal);
        Assert.Equal(GameweekStatus.Settled, cal[0].Status);
    }

    [Fact]
    public async Task FinishedMatch_OneSecondInsideBuffer_NotFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        // Throw-off 3h-minus-1s ago → date+buffer = now+1s > now → not final.
        SetupMatches(M("101", "1", _now.AddHours(-3).AddSeconds(1), "S"));

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.False(cal![0].Matches[0].IsFinal);
    }

    [Fact]
    public async Task InPlay_FlipsToSettled_AsClockPassesLastFixturePlusBuffer()
    {
        // Two finished fixtures in round 1: one well past buffer, one still inside it.
        var early = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);  // throw-off 08:00
        var late = new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero);  // throw-off 11:00

        // now = 12:00 → early+3h=11:00<=now final; late+3h=14:00>now not final → InPlay.
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", early, "S"), M("102", "1", late, "S"));
        var inPlay = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.InPlay, inPlay![0].Status);

        // Advance now to 14:00 → late+3h=14:00<=now → both final → Settled.
        _now = new DateTimeOffset(2026, 2, 1, 14, 0, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", early, "S"), M("102", "1", late, "S"));
        var settled = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.Settled, settled![0].Status);
    }
}
