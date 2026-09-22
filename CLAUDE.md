# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Read `AGENTS.md` too — it sets the engineering persona and principles (security,
reliability, cost, monitoring) this repo expects on every change. This file
covers architecture, commands, and schema. See `docs/runbook.md` for one-time
backfills and post-deploy actions.

## Commands

```bash
# Build
dotnet build Ez.Handball.sln

# Run all tests (requires Azurite running — see below)
dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj

# Run a single test class
dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MatchParserTests"

# Run a single test
dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MatchParserTests.ParseAsync_HappyPath_UpsertsClubsTeamsAndMatch"

# Start local Azure Storage emulator (required for BlobArchiverTests and TableWriterTests)
azurite --silent --location /tmp/azurite-test &

# Start the Functions host locally
cd Ez.Handball.Ingestion && func start

# Seed tournaments for a season (after func start)
curl -X POST "http://localhost:7071/api/seed/tournaments?season=2025"

# Trigger a full sync
curl -X POST "http://localhost:7071/api/sync"

# Re-parse archived blobs after a schema change (no hsi.is fetch)
curl -X POST "http://localhost:7071/api/reparse"
curl -X POST "http://localhost:7071/api/reparse?matchId=103414"
```

## Architecture

### Projects

- **Ez.Handball.Shared** — Class library containing the six `ITableEntity` domain classes (`TournamentEntity`, `ClubEntity`, `TeamEntity`, `MatchEntity`, `PlayerEntity`, `PlayerStatEntity`). Referenced by both Ingestion and the future Api project.
- **Ez.Handball.Ingestion** — Azure Functions v4 isolated worker (.NET 8). Contains all functions, services, and API response models.
- **Ez.Handball.Tests** — xUnit tests. `BlobArchiverTests` and `TableWriterTests` are Azurite integration tests; all function tests use Moq.

### Event-Driven Pipeline

The pipeline is fully driven by blob triggers — no polling:

```
POST /api/sync
  → FetchMatchListFunction
  → archives raw/tournaments/{tournamentId}/matches.json (one per tournament)

Blob trigger: raw/tournaments/*/matches.json
  → FetchMatchDetailsFunction
  → for each match: skip if Status=="S" AND details blob exists
  → archives raw/matches/{matchId}/details.json
              raw/matches/{matchId}/players-{homeTeamId}.json
              raw/matches/{matchId}/players-{awayTeamId}.json

Blob trigger: raw/matches/*/details.json
  → ParseMatchFunction
  → looks up gender from Tournaments table via tournamentId
  → upserts Clubs, Teams, Matches tables

Blob trigger: raw/matches/*/players-*.json
  → ParsePlayersFunction
  → looks up synthetic teamId from Matches table
  → upserts Players, PlayerStats tables
```

Blobs are the source of truth — tables can always be rebuilt from them.

### Services

- **IHsiApiClient / HsiApiClient** — Wraps the three hsi.is API endpoints. Must set a browser-style Accept header on all requests; the API returns HTTP 406 for `application/json`.
- **IBlobArchiver / BlobArchiver** — Operates within the configured container (`raw`). Paths passed to it are relative to the container root (e.g. `matches/123/details.json`, not `raw/matches/...`).
- **ITableWriter / TableWriter** — Generic upsert/get/query over `TableServiceClient`. `UpsertAsync` defaults to `TableUpdateMode.Replace`; pass `mode: TableUpdateMode.Merge` to preserve columns the writer doesn't set. The `Clubs` upserts in `MatchParser` use `Merge` so the out-of-band `LogoSrc` survives re-parses; all other writes use `Replace`.

### Key hsi.is API facts

All three endpoints wrap their payload in `{"data": ...}`:
- Match list (`/tournaments/{id}/matches`): `data` is an array; field names are **PascalCase**; status `"S"` = finished, `"O"` = upcoming
- Match details (`/match/{id}`): `data` is a single object; field names are **SCREAMING_SNAKE_CASE**; scores returned as strings; date format `"dd.MM.yyyy - HH:mm"`; status field is `REPORT_STATUS`
- Player stats (`/match/{id}/{clubId}/players`): `data` is an array; includes non-playing staff — filter to `PLAYER == "1"`; all stat values are strings; no `minutesPlayed` (API has `TWO_MINUTE_SUSPENSIONS`)

`MatchSummary.HomeTeamId` maps JSON `"HomeTeamid"` (lowercase 'd') — an upstream API inconsistency.

### Table Storage schema

| Table | PartitionKey | RowKey |
|-------|-------------|--------|
| Tournaments | season label (e.g. `"2025-26"`) | tournamentId |
| Clubs | `"club"` | clubId (hsi.is ID) |
| Teams | `"team"` | `"{clubId}-{gender}"` e.g. `"385-karlar"` |
| Matches | tournamentId | matchId |
| Players | teamId (synthetic) | playerId |
| PlayerStats | matchId | playerId |
| PlayerPositionObservations | playerId | matchId |

Gender values are `"karlar"` (men) or `"kvenna"` (women). The synthetic `teamId` is derived by `MatchParser` at parse time using the `gender` field from the `Tournaments` table — this lookup must succeed before any match/player data is written.

`MatchEntity` carries `Venue`, `Attendance` (nullable), `HomeHalftimeScore`, and `AwayHalftimeScore` in addition to the final score. Second-half scores are derived (`final − halftime`) at read time, not stored.

### Tournament IDs

The `Tournaments` table must be seeded before the parse functions will work. Use `POST /api/seed/tournaments?season=<startYear>`. Current IDs hardcoded in `SeedTournamentsFunction`:

| ID | Competition | Season |
|----|------------|--------|
| 9142 | Olís deild karla | 2026/2027 |
| 8434 | Olís deild kvenna | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8427 | Olís deild úrslit karla (playoffs) | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8430 | Olís deild úrslit kvenna (playoffs) | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8424 | Grill 66 deild karla | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8443 | Grill 66 deild kvenna | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8441 | Grill 66 deild umspil karla (playoffs) | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8422 | Grill 66 deild umspil kvenna (playoffs) | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8437 | Powerade bikar karla | 2025/2026 (stale — 2026/27 ID not yet known) |
| 8436 | Powerade bikar kvenna | 2025/2026 (stale — 2026/27 ID not yet known) |

Only Olís deild karla has `Ingest=true`/`Active=true`, so it's the only row that
must be correct for the current season. The other rows still carry their
2025/26 hsi.is IDs and need updating once next season's IDs for those
competitions are known — seeding a new season now would write those stale IDs
under the new season's partition.

The `?season=` parameter is the integer **start year**; it is stored as the
`YYYY-YY` label (e.g. `?season=2025` → PartitionKey `"2025-26"`). The label is
the canonical value, denormalized onto `PlayerStatEntity.Season`.

See `docs/runbook.md` for how to re-label an existing season locally.

### Testing approach

Parsing logic lives in injectable services (`MatchParser` / `PlayerParser`, behind `IMatchParser` / `IPlayerParser`) with a testable `ParseAsync` method; the blob-trigger functions (`ParseMatchFunction` / `ParsePlayersFunction`) and the `ReparseFunction` HTTP trigger are thin wrappers that delegate to them. Other functions keep a testable `ProcessAsync`/`SyncAsync` core called by a thin `RunAsync` entry point. Function and parser tests use Moq for the service interfaces. The Azurite integration tests (`BlobArchiverTests`, `TableWriterTests`) create and delete a dedicated test container/table per test class via `IAsyncLifetime`.

### Backfill after schema changes

`PlayerEntity` and `PlayerStatEntity` carry denormalized lookup fields (`Gender`, `ClubId`, `ClubName`, `TournamentId`, `Season`). After deploying any change that adds or alters these fields, re-trigger the parse step (`POST /api/reparse`, optionally scoped with `?matchId=`) so already-ingested matches pick them up — the blob archive is the source of truth and re-parses are idempotent (`TableUpdateMode.Replace`).

See `docs/runbook.md` for the specific one-time backfills each past schema change required (pricing blend, `Retired` flag, HBStatz positions).

After deploying the mini-league "your leagues" reverse index (Web#56,
Backend#128), run `POST /api/admin/mini-leagues/backfill-membership-index`
once (add `?dryRun=false` to actually write — it defaults to a dry run) to
populate `MiniLeagueMembersByUser` for leagues/members created before that
deploy. Without it, `GET /api/mini-leagues/mine` silently omits any
membership older than the deploy, since the reverse-index row is only ever
written going forward, inside `AddMemberAsync`. Idempotent and safe to re-run
(replays every `MiniLeagueMembers` row through the same upsert `AddMemberAsync`
already uses).

### Duplicate Players rows from club transfers

`PlayerEntity.PartitionKey` is `"{clubId}-{gender}"`, and Table Storage can't
rename a PartitionKey in place. Before this fix, a player who transferred
clubs mid-season ended up with two `Players` rows sharing the same `RowKey`
(playerId): a fresh one under the new club that `PlayerParser` keeps writing,
and a stale one under the old club that nothing ever touched again — often
still holding hsi.is's `"Leikmaður"` placeholder position. Any reader that
resolves a player by `RowKey` alone (the public player pool, the admin
missing-position worklist) could land on either row depending on table scan
order, which is why a player could show a stale/placeholder position on
`/players` even after their real position was set.

`PlayerParser.ParseAsync` now looks up a player by `RowKey` across all
partitions, inherits a previously-known position instead of falling back to
hsi.is on a transfer, and deletes any row left behind under a different
partition — so this self-heals the next time a transferred player is parsed
in a new match for their current club.

Only the player's **newest** match may move them (Backend#132). Parse order is
not match order: blob listing is lexical, so a full reparse used to replay
5-digit (older-season) matchIds after 6-digit current-season ones and moved
every transferred player back to a former club. `PlayerEntity.LastMatchDate`
records the date of the match that placed the row; `PlayerParser` skips the
Players write (and the stale-row deletes) for any match older than it and only
writes that match's `PlayerStats` line. `ReparseFunction` also replays in
numeric matchId order. `TransferPlayers` writes a fresh row without
`LastMatchDate`, so the player's next parsed match always wins over a manual
transfer.

For duplicates that predate this fix, see `docs/runbook.md` for the one-time
dedupe op.

### Gameweek engine (Backend#60)

The fantasy gameweek engine derives the gameweek calendar on demand from the
`Matches` table — gameweeks are the configured tournament's HSÍ rounds, not a
materialized entity. Only three things are persisted: a pinned deadline per
gameweek (`GameweekLocks`), a frozen per-(team, gameweek) lineup snapshot
(`GameweekLineups`), and the settled per-(team, gameweek) score (`GameweekScores`).

- **Seed config per environment:** `POST /api/seed/gameweek-config` writes the
  `fantasy-gameweek-v1` Config group (`tournamentId`, `lockOffsetHours`,
  `scoringRuleSetVersion`, `lineupConstraintsVersion`). The `tournamentId` and
  `lockOffsetHours` are owner-tunable per season; nothing works until this is seeded.
- **Locking is lazy** — a gameweek's deadline = earliest member throw-off −
  `lockOffsetHours`, pinned in `GameweekLocks` the first time it is observed as
  passed (so a later fixture reschedule can't move a passed deadline). The
  snapshot guard runs before every buy/sell/lineup mutation and freezes the live
  lineup into `GameweekLineups` for any locked-but-unsnapshotted gameweek.
- **Settlement is idempotent/recomputable:** `POST /api/gameweeks/settle?round={label}`
  (optional `&version=`; authed, always settles the caller's own team — `teamId` is
  derived from the token, not accepted as a parameter). Re-running absorbs stat
  corrections and late-played (postponed) matches via `TableUpdateMode.Replace`.
  Returns `not_ready` (409) until every member match is final.
- **Reads:** public `GET /api/gameweeks` (calendar) and `GET /api/gameweeks/current`;
  authed `GET /api/users/me/gameweeks` (per-gameweek scores + running total).
- **V0 limitation:** settlement runs per team. There is no all-teams fan-out yet —
  `ISettlementTrigger`/`SettlementTrigger` (`Ez.Handball.Ingestion/Services`) is a logging stub
  establishing the trigger point; the per-team POST loop is a follow-up. It's poked from
  `TriggerHbStatzSyncFunction` once a match's HBStatz enrichment sync succeeds — not from the
  raw hsi.is ingestion path — so settlement waits for HBStatz's richer stats rather than firing
  on the first hsi.is pass. Scoring point values come from the configured `ScoringRuleSet`
  version (#27 calibration plugs in there).
