using System.IdentityModel.Tokens.Jwt;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.Security;
using Ez.Handball.Shared.Entities;
using Xunit;

namespace Ez.Handball.Tests.Infrastructure;

public class GameClockTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "TestClockConfig";
    private TableServiceClient _serviceClient = null!;
    private TableClient _table = null!;

    public async Task InitializeAsync()
    {
        _serviceClient = new TableServiceClient(ConnectionString);
        _table = _serviceClient.GetTableClient(TableName);
        await _table.CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _table.DeleteAsync();

    private GameClock Clock(bool enabled) => new(enabled, _serviceClient, TableName);

    private async Task SetOverrideAsync(string? value)
    {
        if (value is null)
        {
            try { await _table.DeleteEntityAsync(GameClock.OverrideGroup, GameClock.OverrideKey); }
            catch (RequestFailedException e) when (e.Status == 404) { /* already absent */ }
            return;
        }
        await _table.UpsertEntityAsync(new ConfigEntity
        {
            PartitionKey = GameClock.OverrideGroup,
            RowKey = GameClock.OverrideKey,
            Value = value
        });
    }

    private static void AssertNearWallClock(DateTimeOffset actual) =>
        Assert.True((DateTimeOffset.UtcNow - actual).Duration() < TimeSpan.FromMinutes(1),
            $"Expected ~wall clock, got {actual:o}");

    [Fact]
    public async Task GetUtcNow_FlagOff_ReturnsWallClock_AndIgnoresOverrideRow()
    {
        await SetOverrideAsync("2000-01-01T00:00:00Z");

        AssertNearWallClock(Clock(enabled: false).GetUtcNow());
    }

    [Fact]
    public async Task GetUtcNow_FlagOn_WithValidRow_ReturnsVirtualNow()
    {
        await SetOverrideAsync("2025-09-01T17:00:00Z");

        var now = Clock(enabled: true).GetUtcNow();

        Assert.Equal(new DateTimeOffset(2025, 9, 1, 17, 0, 0, TimeSpan.Zero), now);
    }

    [Fact]
    public async Task GetUtcNow_FlagOn_NoRow_FallsBackToWallClock()
    {
        await SetOverrideAsync(null);

        AssertNearWallClock(Clock(enabled: true).GetUtcNow());
    }

    [Fact]
    public async Task GetUtcNow_FlagOn_GarbageRow_FallsBackToWallClock()
    {
        await SetOverrideAsync("not-a-date");

        AssertNearWallClock(Clock(enabled: true).GetUtcNow());
    }

    [Fact]
    public async Task OverrideMovesGameClock_ButNotJwtExpiry()
    {
        await SetOverrideAsync("2025-09-01T17:00:00Z");
        var gameClock = Clock(enabled: true);

        // Auth keeps the wall-clock Func — the same delegate AddAuthInfrastructure registers.
        var settings = new JwtSettings(
            SigningKey: "this-is-a-test-signing-key-32-bytes-long!!",
            Issuer: "ez-handball", Audience: "ez-handball-web",
            AccessTokenMinutes: 15, RefreshTokenDays: 30, EmailTokenHours: 24);
        var jwt = new JwtTokenService(settings, () => DateTimeOffset.UtcNow);
        var token = jwt.CreateAccessToken(new UserEntity
        {
            RowKey = "u1", Email = "a@b.is", EmailVerified = true, DisplayName = "A"
        });

        // Game time travelled to 2025; token expiry is still ~15 min from real now.
        Assert.Equal(2025, gameClock.GetUtcNow().Year);
        DateTime expiry = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo; // UTC DateTime
        DateTime expectedExpiry = DateTime.UtcNow.AddMinutes(15);
        Assert.True((expectedExpiry - expiry).Duration() < TimeSpan.FromMinutes(1),
            $"JWT expiry {expiry:o} should track wall clock, not the 2025 game clock");
    }
}
