# Centralised domain clock with gated override

Sub-task [#94](https://github.com/kromby/Ez.Handball.Backend/issues/94) of the
time-shift replay harness epic [#93](https://github.com/kromby/Ez.Handball.Backend/issues/93).
See the epic spec `2026-06-17-time-shift-replay-harness.md` for the whole picture;
this spec covers only the clock seam.

## Goal

Give the fantasy loop a single, overridable source of **domain/game time** that is
independent of the auth wall clock. Move this clock and gameweek status moves; auth
token expiry does not. In production the override is a no-op and the clock returns
real `DateTimeOffset.UtcNow`.

## Today

`AuthInfrastructureRegistration.cs:29` registers one shared
`services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow)`.
That same singleton is consumed by both game-time readers
(`GameweekCalendarService`, `GameweekSnapshotGuard`) and auth
(`JwtTokenService` plus the auth/timestamp use cases). Overriding it to time-travel a
season would also shift JWT expiry and nuke every session the moment the clock jumps.
The two uses must be split.

## The core tension

`TimeProvider.GetUtcNow()` is **synchronous**, but the override value lives in the
async-read `Config` table. The chosen resolution is a **synchronous point-read** inside
`GetUtcNow()` (`TableClient.Query<ConfigEntity>(...)`), gated so it only runs when the
master flag is on:

- Flag **off** (production): `GetUtcNow()` returns `base.GetUtcNow()` with **zero I/O** —
  a pure wall-clock call.
- Flag **on** (debug, non-prod): one synchronous point-read of a single Config row.

Game-time `now` is read only a couple of times per request, so the debug-mode read cost
is irrelevant. Rejected alternatives: blocking on the async read
(`.GetAwaiter().GetResult()` — deadlock/starvation risk) and resolving `now` in a
per-request async preamble (spreads clock logic into every caller, fights the
`TimeProvider` shape).

## Components

### 1. `GameClock : TimeProvider` (new, `Ez.Handball.Infrastructure`)

Constructed with a captured `bool _enabled` (from `IConfiguration`) and the already-registered
`TableServiceClient`.

```
override DateTimeOffset GetUtcNow()
    if (!_enabled) return base.GetUtcNow();         // production path, no table read
    read Config row (PK "debug-clock-v1", RK "virtualNow")
    if present and parses as a UTC instant -> return it
    else -> return base.GetUtcNow();                 // missing/garbage -> safe fallback
```

The resolved value is **never cached** — each call re-reads the row, so moving the date
takes effect immediately. A missing or unparseable override falls back to the wall clock
rather than throwing.

### 2. Master enable flag

`Debug:GameClock:OverrideEnabled` (bool) in env/`appsettings`. Read from `IConfiguration`
at registration and captured into `GameClock`. Defaults **off**; set explicitly `false` in
production `appsettings`. Captured-at-construction means flipping the switch requires a
restart — a deliberate kill-switch property. The *virtual now value* is what stays
runtime-settable via the Config row.

### 3. DI registration (`Ez.Handball.Api/Program.cs`)

Register `GameClock` as its **concrete type** (`AddSingleton<GameClock>`) and hand it to the
two game-time readers via explicit factory wiring — do **not** register it as the framework
`TimeProvider`. ASP.NET's rate limiter resolves `TimeProvider` from DI, so registering
`GameClock` as the framework `TimeProvider` would leak virtual game time into rate limiting
(and is also resolved eagerly at host build, before storage is configured). Registering it
as its own type leaves the framework default `TimeProvider` (the wall clock) in place for
the rate limiter, auth, and log timestamps, satisfying the epic guardrail. The singleton is
constructed lazily on first game request, so `TableServiceClient` is never resolved at
host-build time.

The two services still declare a base `TimeProvider` constructor parameter (so tests can
substitute a fake); the production wiring passes the concrete `GameClock`. The auth
`Func<DateTimeOffset>` registration is **left untouched** — `JwtTokenService` and the
auth/timestamp use cases keep the wall clock.

Only the API host needs the registration: the two game-time readers are registered solely
in `Ez.Handball.Api/Program.cs`. Ingestion does not consume them.

### 4. Migrate the two game-time readers

`GameweekCalendarService` and `GameweekSnapshotGuard`: replace the
`Func<DateTimeOffset> _now` constructor parameter with `TimeProvider _clock`, and replace
`_now()` with `_clock.GetUtcNow()`. No behaviour change beyond the source of `now`.

`SettleGameweekUseCase` finality (the results-axis gate) is **out of scope here** — it
belongs to #95.

## Config & conventions

| Field | Value |
|-------|-------|
| Table | `Config` |
| PartitionKey | `debug-clock-v1` |
| RowKey | `virtualNow` |
| Value | ISO-8601 UTC instant, e.g. `2025-09-01T17:00:00Z` |

Set manually for now (the admin setter endpoint is deferred to #96), the same workflow as
the existing `Retired` flag edits. No seed function is needed — an absent row means no
override, i.e. wall clock.

## Testing

- **`GameClockTests`** (new): flag off returns wall clock and ignores the override row;
  flag on + valid row returns the virtual now; flag on + missing/garbage row falls back to
  the wall clock.
- **Independence proof** (acceptance criterion): setting the override moves a
  `GameweekCalendarService` status while a `JwtTokenService`-issued token's expiry (driven
  by the untouched `Func<DateTimeOffset>`) is unaffected.
- **Migrate** existing `GameweekCalendarServiceTests` and `GameweekSnapshotGuardTests` off
  `Func<DateTimeOffset>` onto a fake `TimeProvider`. Use a small local
  `StubTimeProvider : TimeProvider` (no new dependency) rather than pulling in
  `Microsoft.Extensions.TimeProvider.Testing`.

## Acceptance criteria

- Domain time and auth time resolve independently; a set override moves gameweek status but
  not JWT expiry (proved by a test).
- Override flag off → game clock == wall clock; the override row is ignored (zero table I/O).
- Override flag on + a virtual `now` set → `GameweekCalendarService` derives status against
  the virtual `now`.
- Unit tests use a fake/overridden `TimeProvider`; existing gameweek tests are migrated to it.

## Guardrails

- Master enable flag defaults **off**; off in production `appsettings`.
- Setting the override value (the setter endpoint) lands in #96. Until then it is a manual
  Config table edit.

## Out of scope

- The virtual-now finality gate in `GameweekCalendarService` + `SettleGameweekUseCase` (#95).
- The admin advance-and-settle fan-out and the override setter endpoint (#96).
