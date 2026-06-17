# Domain Clock Gated Override Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the fantasy gameweek engine an overridable domain clock (`GameClock : TimeProvider`) that returns a virtual `now` from the `Config` table when a master flag is on, and the real wall clock (zero I/O) when off — without touching auth/JWT time.

**Architecture:** A new `GameClock : TimeProvider` lives in `Ez.Handball.Infrastructure`. When the `Debug:GameClock:OverrideEnabled` flag is off it returns `TimeProvider.System.GetUtcNow()` with no table read (production path). When on it reads a single `Config` row (`debug-clock-v1`/`virtualNow`) synchronously on every call (never cached) and returns it, falling back to the wall clock on a missing/garbage row. `GameweekCalendarService` and `GameweekSnapshotGuard` migrate from the shared `Func<DateTimeOffset>` to `TimeProvider`; auth keeps its `Func<DateTimeOffset>` untouched.

**Tech Stack:** C# / .NET 9, Azure.Data.Tables, xUnit + Moq, Azurite (for the integration tests), `System.TimeProvider`.

**Spec:** `docs/superpowers/specs/2026-06-17-domain-clock-gated-override-design.md`

---

## File Structure

- **Create** `Ez.Handball.Infrastructure/GameClock.cs` — the `TimeProvider` subclass with the gated override read. One responsibility: resolve domain `now`.
- **Create** `Ez.Handball.Tests/TestSupport/StubTimeProvider.cs` — a 5-line fake `TimeProvider` for unit tests of the migrated services.
- **Create** `Ez.Handball.Tests/Infrastructure/GameClockTests.cs` — Azurite integration tests for `GameClock` (flag off/on, fallback paths, auth-independence proof).
- **Modify** `Ez.Handball.Application/Services/GameweekCalendarService.cs` — swap `Func<DateTimeOffset>` → `TimeProvider`.
- **Modify** `Ez.Handball.Application/Services/GameweekSnapshotGuard.cs` — swap `Func<DateTimeOffset>` → `TimeProvider`.
- **Modify** `Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs` — use `StubTimeProvider`.
- **Modify** `Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs` — use `StubTimeProvider`.
- **Modify** `Ez.Handball.Api/Program.cs` — register `GameClock` as the singleton `TimeProvider`.
- **Modify** `Ez.Handball.Api/appsettings.json` — add the master flag, defaulted off.

> **Note on the test project:** `Ez.Handball.Infrastructure.csproj` already declares `<InternalsVisibleTo Include="Ez.Handball.Tests" />`, so the independence test can construct the internal `JwtTokenService`. The `Config` table constant is `Ez.Handball.Infrastructure.Tables.Config` (`"Config"`); `TableServiceClient` is registered as a singleton in `AddTableStorageInfrastructure`.

---

## Task 1: GameClock (the gated domain clock)

**Files:**
- Create: `Ez.Handball.Infrastructure/GameClock.cs`
- Test: `Ez.Handball.Tests/Infrastructure/GameClockTests.cs`

> These are Azurite integration tests — start Azurite first: `azurite --silent --location /tmp/azurite-test &`

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Infrastructure/GameClockTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameClockTests"`
Expected: FAIL — `GameClock` does not exist (compile error).

- [ ] **Step 3: Write the GameClock implementation**

Create `Ez.Handball.Infrastructure/GameClock.cs`:

```csharp
using System.Globalization;
using System.Linq;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure;

// Domain/game clock for the time-shift replay harness (#94). When the master override flag is
// off (production), returns the real wall-clock UTC with no table I/O. When on (debug, non-prod),
// reads a virtual `now` from the Config table on every call — never cached, so moving the date
// takes effect immediately. Auth time stays on the separate Func<DateTimeOffset> wall clock.
public sealed class GameClock : TimeProvider
{
    public const string OverrideGroup = "debug-clock-v1";
    public const string OverrideKey = "virtualNow";

    private readonly bool _overrideEnabled;
    private readonly TableClient _config;

    // configTableName defaults to the shared Config table; overridable for isolated tests.
    public GameClock(bool overrideEnabled, TableServiceClient tableServiceClient, string? configTableName = null)
    {
        _overrideEnabled = overrideEnabled;
        _config = tableServiceClient.GetTableClient(configTableName ?? Tables.Config);
    }

    public override DateTimeOffset GetUtcNow()
    {
        if (!_overrideEnabled) return TimeProvider.System.GetUtcNow();
        return TryReadVirtualNow(out var virtualNow) ? virtualNow : TimeProvider.System.GetUtcNow();
    }

    private bool TryReadVirtualNow(out DateTimeOffset virtualNow)
    {
        virtualNow = default;
        try
        {
            var filter = $"PartitionKey eq '{OverrideGroup}' and RowKey eq '{OverrideKey}'";
            var row = _config.Query<ConfigEntity>(filter: filter, maxPerPage: 1).FirstOrDefault();
            if (row is null) return false;
            return DateTimeOffset.TryParse(
                row.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out virtualNow);
        }
        catch (RequestFailedException)
        {
            return false; // table missing / transient → wall-clock fallback
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameClockTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Infrastructure/GameClock.cs Ez.Handball.Tests/Infrastructure/GameClockTests.cs
git commit -m "feat: add gated domain GameClock (Backend#94)"
```

---

## Task 2: Migrate GameweekCalendarService to TimeProvider

**Files:**
- Create: `Ez.Handball.Tests/TestSupport/StubTimeProvider.cs`
- Modify: `Ez.Handball.Application/Services/GameweekCalendarService.cs:17,20-25,32`
- Test: `Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs:14`

- [ ] **Step 1: Add the test fake**

Create `Ez.Handball.Tests/TestSupport/StubTimeProvider.cs`:

```csharp
namespace Ez.Handball.Tests.TestSupport;

// Minimal fake TimeProvider for unit tests — returns a fixed instant. Avoids a dependency on
// Microsoft.Extensions.TimeProvider.Testing for the tiny surface we use.
internal sealed class StubTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public StubTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}
```

- [ ] **Step 2: Update the test to use it (failing)**

In `Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs`, add the using at the top:

```csharp
using Ez.Handball.Tests.TestSupport;
```

Change the SUT factory on line 14 from:

```csharp
    private GameweekCalendarService CreateSut() => new(_matches.Object, _locks.Object, () => _now);
```

to:

```csharp
    private GameweekCalendarService CreateSut() => new(_matches.Object, _locks.Object, new StubTimeProvider(_now));
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekCalendarServiceTests"`
Expected: FAIL — `GameweekCalendarService` constructor still takes `Func<DateTimeOffset>` (compile error: cannot convert `StubTimeProvider` to `Func<DateTimeOffset>`).

- [ ] **Step 4: Migrate the service**

In `Ez.Handball.Application/Services/GameweekCalendarService.cs`:

Replace the field (line 17):

```csharp
    private readonly Func<DateTimeOffset> _now;
```

with:

```csharp
    private readonly TimeProvider _clock;
```

Replace the constructor (lines 19-25):

```csharp
    public GameweekCalendarService(
        IMatchRepository matches, IGameweekLockRepository locks, Func<DateTimeOffset> now)
    {
        _matches = matches;
        _locks = locks;
        _now = now;
    }
```

with:

```csharp
    public GameweekCalendarService(
        IMatchRepository matches, IGameweekLockRepository locks, TimeProvider clock)
    {
        _matches = matches;
        _locks = locks;
        _clock = clock;
    }
```

Replace the read (line 32):

```csharp
        var now = _now();
```

with:

```csharp
        var now = _clock.GetUtcNow();
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekCalendarServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekCalendarService.cs Ez.Handball.Tests/TestSupport/StubTimeProvider.cs Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs
git commit -m "refactor: GameweekCalendarService reads domain clock via TimeProvider (Backend#94)"
```

---

## Task 3: Migrate GameweekSnapshotGuard to TimeProvider

**Files:**
- Modify: `Ez.Handball.Application/Services/GameweekSnapshotGuard.cs:24,26-36,46`
- Test: `Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs:17-18`

- [ ] **Step 1: Update the test to use the fake (failing)**

In `Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs`, add the using at the top:

```csharp
using Ez.Handball.Tests.TestSupport;
```

Change the SUT factory (lines 17-18) from:

```csharp
    private GameweekSnapshotGuard CreateSut() => new(
        _config.Object, _calendar.Object, _locks.Object, _snapshots.Object, _liveLineup.Object, () => _now);
```

to:

```csharp
    private GameweekSnapshotGuard CreateSut() => new(
        _config.Object, _calendar.Object, _locks.Object, _snapshots.Object, _liveLineup.Object, new StubTimeProvider(_now));
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekSnapshotGuardTests"`
Expected: FAIL — constructor still takes `Func<DateTimeOffset>` (compile error).

- [ ] **Step 3: Migrate the service**

In `Ez.Handball.Application/Services/GameweekSnapshotGuard.cs`:

Replace the field (line 24):

```csharp
    private readonly Func<DateTimeOffset> _now;
```

with:

```csharp
    private readonly TimeProvider _clock;
```

Replace the constructor (lines 26-36):

```csharp
    public GameweekSnapshotGuard(
        IGameweekConfigRepository config, IGameweekCalendarService calendar, IGameweekLockRepository locks,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup, Func<DateTimeOffset> now)
    {
        _config = config;
        _calendar = calendar;
        _locks = locks;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
        _now = now;
    }
```

with:

```csharp
    public GameweekSnapshotGuard(
        IGameweekConfigRepository config, IGameweekCalendarService calendar, IGameweekLockRepository locks,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup, TimeProvider clock)
    {
        _config = config;
        _calendar = calendar;
        _locks = locks;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
        _clock = clock;
    }
```

Replace the read (line 46):

```csharp
        var now = _now();
```

with:

```csharp
        var now = _clock.GetUtcNow();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekSnapshotGuardTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekSnapshotGuard.cs Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs
git commit -m "refactor: GameweekSnapshotGuard reads domain clock via TimeProvider (Backend#94)"
```

---

## Task 4: Register GameClock and wire the master flag

**Files:**
- Modify: `Ez.Handball.Api/Program.cs` (after line 52 `AddAuthInfrastructure`, before the gameweek service registrations near line 175)
- Modify: `Ez.Handball.Api/appsettings.json`

- [ ] **Step 1: Add the master flag to appsettings (off in production)**

In `Ez.Handball.Api/appsettings.json`, add a top-level `Debug` block. The full file becomes:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Storage": {
    "ConnectionString": ""
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Debug": {
    "GameClock": {
      "OverrideEnabled": false
    }
  }
}
```

- [ ] **Step 2: Register GameClock as the singleton TimeProvider**

In `Ez.Handball.Api/Program.cs`, confirm these usings are present near the top (add any that are missing):

```csharp
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
```

Immediately after line 52 (`builder.Services.AddAuthInfrastructure(...)`), add:

```csharp
// Domain/game clock for the time-shift replay harness (#94). Off by default (production):
// resolves to the real wall clock. Auth keeps its own Func<DateTimeOffset> — overriding the
// game clock must not move JWT expiry.
builder.Services.AddSingleton<TimeProvider>(sp => new GameClock(
    builder.Configuration.GetValue("Debug:GameClock:OverrideEnabled", false),
    sp.GetRequiredService<TableServiceClient>()));
```

- [ ] **Step 3: Build to verify DI resolves**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeds. (`GameweekCalendarService` / `GameweekSnapshotGuard` now resolve their `TimeProvider` from this registration; the auth `Func<DateTimeOffset>` registration is unchanged.)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS — all tests green (no `Func<DateTimeOffset>` consumer left for the two migrated services; auth tests unaffected).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/Program.cs Ez.Handball.Api/appsettings.json
git commit -m "feat: register GameClock TimeProvider with master flag off in prod (Backend#94)"
```

---

## Manual verification (post-merge, non-prod only)

1. In a non-prod environment, set `Debug__GameClock__OverrideEnabled=true` (env var) and restart the API.
2. Write the override row to the `Config` table: PartitionKey `debug-clock-v1`, RowKey `virtualNow`, Value `2025-09-01T17:00:00Z` (use `Edm.String` — it is parsed as text).
3. `GET /api/gameweeks` → every gameweek for the configured tournament reads `Open` (virtual now is before round 1).
4. Bump the override row forward past a round deadline → that gameweek flips to `DeadlineLocked`/`InPlay`; a fresh login still issues a token expiring ~15 min from real wall-clock time.
5. Delete the row or set the flag back off → calendar returns to wall-clock behaviour.
```
