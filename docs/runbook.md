# Runbook

One-time backfills and post-deploy actions for Ez.Handball.Backend. See
`CLAUDE.md` for architecture and commands, and `AGENTS.md` for the engineering
principles that govern how to make changes here.

## Re-labelling an existing season (local)

The parse step looks up tournaments by `RowKey eq '{tournamentId}'` with no
partition filter, so a stale `"2025"` partition alongside a new `"2025-26"`
partition makes season resolution ambiguous. To re-label cleanly:

1. Clear the `Tournaments` table (drop the old partition).
2. Re-seed: `POST /api/seed/tournaments?season=2025`.
3. Re-run `POST /api/sync` — the parse step replaces `PlayerStats.Season`
   in place (rows are keyed by matchId/playerId, so no duplicates).

## Reparse after a schema change

Use `POST /api/reparse` to replay the parse step over the existing `raw/` blobs
without re-fetching from hsi.is. Scope to one match with `?matchId={id}`. This is
the preferred backfill after any change to `MatchEntity`, `PlayerEntity`, or
`PlayerStatEntity`. (Re-running `POST /api/sync` still works but re-fetches every
match from hsi.is.)

## Prior-season pricing blend (`PriceRuleSet.BlendGames`)

After deploying the prior-season pricing blend (adds `PriceRuleSet.BlendGames`),
re-run `POST /api/seed/price-rule-sets` before or immediately alongside the
deploy. The `fantasy-price-v1` Config group now requires a `blendGames` row;
without it, every pricing-touching endpoint (`/api/players`, squad views,
buy/sell) returns `invalid_rule_set` until it's reseeded. Safe and idempotent
to re-run at any time.

## `Retired` flag bootstrap

After deploying the `Retired` flag, run `POST /api/players/bootstrap-retired`
once. It marks every player with no `PlayerStats` in the latest season
(lexical-max `Tournaments` partition key) as `Retired = true`, writing back the
full row via `Merge`. It only ever sets `true`, so it is safe to re-run and never
clobbers manual edits. Curate further by editing the `Retired` column directly in
the `Players` table — use `Edm.Boolean`, not String (a String value causes a 500
on read). `POST /api/reparse` preserves all `Retired` values because the Players
upsert uses `Merge`.

## HBStatz position backfill (Backend#106)

After deploying the HBStatz position backfill, run
`POST /api/players/backfill-positions` once (add `?dryRun=false` to actually
write — it defaults to a dry run) to derive `Position`/`PositionSecondary` for
every player observed in an already-archived `hbstatz/matches/*.json` blob.
It's idempotent and safe to re-run. Going forward, `POST /api/hbstatz/sync`
keeps both fields current automatically as new matches sync. Players HBStatz
never reaches can be corrected manually via `POST /api/players/set-position`.

## Duplicate Players dedupe

For `Players` rows that duplicated across a club transfer before
`PlayerParser` started self-healing (see `CLAUDE.md`), run
`POST /api/players/dedupe` once (add `?dryRun=false` to actually delete — it
defaults to a dry run). For every `RowKey` with more than one row, it keeps
the one with the latest `Timestamp` (the one still receiving ingestion writes)
and deletes the rest. Idempotent and safe to re-run.

## Players moved back to former clubs by reparse (Backend#132)

Before `PlayerEntity.LastMatchDate` existed, a full `POST /api/reparse` could
leave transferred players under their old club (the match page showed them as
"Óþekktur leikmaður"). After deploying the fix, run a full `POST /api/reparse`
once. It now replays in numeric matchId order, so each player's newest match is
parsed last, and it stamps `LastMatchDate` on every row, which guards all later
reparses. Check a known case afterwards: `GET /api/players/177119` should show
`teamId` `96-karlar` (FH), and every line in `GET /api/matches/111452` should
have a `jerseyNumber`.

Reparse rewrites `Matches` rows, which clears `HbStatzSyncedAt`, and rewrites
`PlayerStats` rows, which clears their HBStatz columns. Any full reparse does
this. Run `POST /api/hbstatz/sync` afterwards: the default sweep re-syncs every
match whose `HbStatzSyncedAt` is empty.

## Settle gameweeks that were never scored (Backend#136)

Before Backend#136 nothing settled gameweeks in production, so no `GameweekScores` rows
existed and every leaderboard/mini-league showed no points. After deploying it, backfill
once as an admin:

```bash
curl -X POST -H "Authorization: Bearer <admin token>" \
  "https://ez-handball-api.azurewebsites.net/api/admin/gameweeks/settle"
```

With no `round` it settles every complete gameweek and returns one report per round
(`teamsConsidered`, `settled`, `notReady`, `skipped`). Idempotent — safe to re-run. From then
on `AutoSettlementService` keeps recent rounds settled automatically.

