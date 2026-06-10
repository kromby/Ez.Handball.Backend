# Round listing endpoint (umferð view) — Design

Issue: [Backend#10](https://github.com/kromby/Ez.Handball.Backend/issues/10)
Date: 2026-06-10

## Goal

Expose a tournament's fixtures grouped by round — the way a Saturday viewer
thinks about the league. For a given tournament, return its rounds (umferðir),
each with a date range and its matches; each match shows the final score if
played or the scheduled kickoff if upcoming.

## Acceptance criteria (from the issue)

- Returns rounds for a tournament's season, each with a date range and matches.
- Each match includes the final score if played, the scheduled time if upcoming.
- Supports filter by season — satisfied implicitly: a tournament id pins exactly
  one season (see [Scope](#scope)).

## Scope

The endpoint is **per tournament** (required path parameter). Round numbers reset
per tournament — round 5 of *Olís deild karla* is unrelated to round 5 of *Olís
deild kvenna* — so a single tournament is the only scope at which round numbers
are unambiguous.

A tournament id already pins exactly one season (the `Tournaments` table is
partitioned by season label), so **no `season` query parameter is needed**. The
issue's "filter by season" criterion is met because each tournament belongs to
one season. Switching seasons is a competition-level concern already served by
the existing tournaments listing (`GetTournamentsUseCase`); it is out of scope
here.

The endpoint is **public** (no authentication), consistent with the other read
endpoints (`/api/matches/{id}`, `/api/leaderboard`, `/api/clubs`).

## Background: where round data lives today

The HSÍ match-list endpoint (`/api/hsi/tournaments/{id}/matches`) returns a
`Round` field per match. It is already modelled in
`Ez.Handball.Ingestion/Models/MatchListResponse.cs` (`MatchSummary.Round`) but is
**not persisted** — `MatchEntity` has no round column.

The ingestion pipeline writes the `Matches` table from the per-match **details**
blob (`raw/matches/{matchId}/details.json`), which has no round field. The round
exists only in the per-tournament **list** blob
(`raw/tournaments/{tournamentId}/matches.json`). So capturing round requires an
ingestion change.

`MatchEntity.Status` already stores the HSÍ status (`"S"` = finished/Slokið,
`"O"` = upcoming/Opið) via the details `REPORT_STATUS` field, so a match is
considered **played when `Status == "S"`**. No extra status field is needed.

## Design

### 1. Ingestion — capture round

Add one field to `MatchEntity`:

```csharp
public string Round { get; set; } = string.Empty;   // opaque HSÍ round label
```

Populate it in `MatchParser.ParseAsync`. After the parser resolves the
tournament, it reads the already-archived `tournaments/{tournamentId}/matches.json`
blob, finds the summary whose `GameId == matchId`, and copies its `Round` onto the
`MatchEntity`. If the list blob or the matching entry is missing, `Round` is left
empty and a warning is logged — the match still parses and writes.

`MatchParser` gains an `IBlobArchiver` dependency (it currently takes only
`ITableWriter` + `ILogger`). A read of the list JSON is added (deserialize
`MatchListResponse`, match by `GameId`).

**Why read from the list blob rather than thread round through the fetch step:**
blobs are the source of truth and reparses are idempotent. Reading round from the
archived list blob means `POST /api/reparse` backfills it with no hsi.is
re-fetch, consistent with the documented backfill strategy. It also avoids
blob-trigger ordering races (the details parser writes `Matches` with
`TableUpdateMode.Replace`, which would clobber a round written by a separate
step).

**Ops:** after deploy, run `POST /api/reparse` to backfill `Round` onto existing
matches.

### 2. Read model — repository

Extend `IMatchRepository`:

```csharp
Task<IReadOnlyList<MatchListItem>> ListByTournamentAsync(
    string tournamentId, CancellationToken ct);
```

The implementation:

- Queries the `Matches` table by `PartitionKey eq '{tournamentId}'`.
- Joins club name + `LogoSrc` from the `Clubs` table for each home/away club
  (batched — collect distinct club ids, one query, no N+1).
- Returns lightweight `MatchListItem` rows (match id, round, date, status, venue,
  both teams with team id / club id / club name / logo / score). No player lines.

Tournament name and season label are read from the `Tournaments` table by the use
case (a tournament lookup is needed anyway for the not-found check).

### 3. Use case — grouping

`GetRoundsUseCase` implementing `IGetRoundsUseCase`:

```csharp
public abstract record GetRoundsResult
{
    public sealed record NotFound : GetRoundsResult;
    public sealed record Found(TournamentRounds Rounds) : GetRoundsResult;
}
```

Behaviour:

- Load the tournament. If the id is not in the `Tournaments` table → `NotFound`.
- Fetch matches via the repository.
- Group matches by `Round`.
- **Round ordering:** numeric labels ascending (`1, 2, 3 …`); non-numeric labels
  last, sorted alphabetically. (Numeric parse on the round label; failures sort
  after all numerics.)
- Within a round, matches sorted by kickoff date ascending.
- **Round dates:** `startDate` = earliest match calendar day, `endDate` = latest
  match calendar day (date-only). Equal for single-day rounds.
- **Score mapping:** `Status == "S"` → `played: true`, scores populated;
  otherwise `played: false` and both scores `null`.

### 4. API edge

`GET /api/tournaments/{tournamentId}/rounds` — a dumb edge:

- Validate `tournamentId` is non-blank → else `400 { "error": "invalid_tournament_id" }`.
- Call the use case.
- `NotFound` → `404 { "error": "tournament_not_found" }`.
- `Found` → `200` with the body below.

Registered in `Program.cs` alongside the existing `GET /api/matches/{matchId}`
mapping; `IGetRoundsUseCase` registered scoped.

### 5. Response shape

```jsonc
{
  "tournamentId": "8444",
  "tournamentName": "Olís deild karla",
  "season": "2025-26",
  "rounds": [
    {
      "round": "1",
      "startDate": "2025-09-03",
      "endDate": "2025-09-03",
      "matches": [
        {
          "matchId": "103414",
          "played": true,
          "date": "2025-09-03T19:30:00+00:00",
          "venue": "Höllin",
          "home": {
            "teamId": "385-karlar", "clubId": "385", "name": "Valur",
            "logoSrc": "https://…", "score": 28
          },
          "away": {
            "teamId": "402-karlar", "clubId": "402", "name": "Haukar",
            "logoSrc": "https://…", "score": 25
          }
        }
      ]
    }
  ]
}
```

Upcoming matches: `played: false`, and `score` is `null` on both `home` and
`away`. `startDate` / `endDate` are date-only strings; equal for a single-day
round.

### 6. Testing

- **`MatchParser`** (ingestion): round copied from the list blob onto the match;
  missing list blob or no matching `GameId` → empty round + warning, match still
  written; reparse remains idempotent.
- **`GetRoundsUseCase`**: numeric-ascending then text-last ordering; multi-day
  round → distinct `startDate`/`endDate`; single-day round → equal dates; played
  vs upcoming score mapping; unknown tournament id → `NotFound`.
- **Repository**: groups by tournament partition; club name/logo join; no player
  lines fetched.
- **Endpoint**: 200 body shape; 404 for unknown tournament; blank id → 400.

## Out of scope (YAGNI)

- Multi-tournament / whole-season aggregation.
- A "current round" / "next round" marker.
- Player lines or per-match detail (already served by `/api/matches/{id}`).
- A `season` query parameter (tournament id pins the season).
