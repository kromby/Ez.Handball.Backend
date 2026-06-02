# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

Gender values are `"karlar"` (men) or `"kvenna"` (women). The synthetic `teamId` is derived by `MatchParser` at parse time using the `gender` field from the `Tournaments` table — this lookup must succeed before any match/player data is written.

`MatchEntity` carries `Venue`, `Attendance` (nullable), `HomeHalftimeScore`, and `AwayHalftimeScore` in addition to the final score. Second-half scores are derived (`final − halftime`) at read time, not stored.

### Tournament IDs (2025/2026 season)

The `Tournaments` table must be seeded before the parse functions will work. Use `POST /api/seed/tournaments?season=2025`. Current IDs hardcoded in `SeedTournamentsFunction`:

| ID | Competition |
|----|------------|
| 8444 | Olís deild karla |
| 8434 | Olís deild kvenna |
| 8424 | Grill 66 deild karla |
| 8443 | Grill 66 deild kvenna |
| 8437 | Powerade bikar karla |
| 8436 | Powerade bikar kvenna |

The `?season=` parameter is the integer **start year**; it is stored as the
`YYYY-YY` label (e.g. `?season=2025` → PartitionKey `"2025-26"`). The label is
the canonical value, denormalized onto `PlayerStatEntity.Season`.

#### Re-labelling an existing season (local)

The parse step looks up tournaments by `RowKey eq '{tournamentId}'` with no
partition filter, so a stale `"2025"` partition alongside a new `"2025-26"`
partition makes season resolution ambiguous. To re-label cleanly:

1. Clear the `Tournaments` table (drop the old partition).
2. Re-seed: `POST /api/seed/tournaments?season=2025`.
3. Re-run `POST /api/sync` — the parse step replaces `PlayerStats.Season`
   in place (rows are keyed by matchId/playerId, so no duplicates).

### Testing approach

Parsing logic lives in injectable services (`MatchParser` / `PlayerParser`, behind `IMatchParser` / `IPlayerParser`) with a testable `ParseAsync` method; the blob-trigger functions (`ParseMatchFunction` / `ParsePlayersFunction`) and the `ReparseFunction` HTTP trigger are thin wrappers that delegate to them. Other functions keep a testable `ProcessAsync`/`SyncAsync` core called by a thin `RunAsync` entry point. Function and parser tests use Moq for the service interfaces. The Azurite integration tests (`BlobArchiverTests`, `TableWriterTests`) create and delete a dedicated test container/table per test class via `IAsyncLifetime`.

### Backfill after schema changes

`PlayerEntity` and `PlayerStatEntity` carry denormalized lookup fields (`Gender`, `ClubId`, `ClubName`, `TournamentId`, `Season`). After deploying any change that adds or alters these fields, re-trigger the parse step so already-ingested matches pick them up. The blob archive is the source of truth and re-parses are idempotent (`TableUpdateMode.Replace`).

Use `POST /api/reparse` to replay the parse step over the existing `raw/` blobs
without re-fetching from hsi.is. Scope to one match with `?matchId={id}`. This is
the preferred backfill after any change to `MatchEntity`, `PlayerEntity`, or
`PlayerStatEntity`. (Re-running `POST /api/sync` still works but re-fetches every
match from hsi.is.)
