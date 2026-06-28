# HBStatz per-game stat lines → scoring demo — design

**Issue:** extends the HBStatz scraping spike (subtask of [Backend #7](https://github.com/kromby/Ez.Handball.Backend/issues/7); builds on PR #100)
**Date:** 2026-06-28
**Type:** Spike / proof-of-concept (disposable, extends `tools/HbStatz.Spike`)

## Goal

Scrape a couple of real games' **per-player per-game** stat lines from HBStatz,
apply the project's actual fantasy `ScoringRuleSet`, and print each player's
computed fantasy points — so the scoring mechanism can be seen working on real
data.

This corrects the first pass of the spike (PR #100), which captured **season
aggregate** stats per player. Season averages can't exercise scoring; scoring
operates on a single game's stat line.

## Target games

Two real Olís deild karla match reports:

- `https://hbstatz.is/OlisDeildKarlaLeikur.php?ID=12922`
- `https://hbstatz.is/OlisDeildKarlaLeikur.php?ID=12919`

## Correction on feasibility (recorded so it isn't re-discovered)

An earlier reading suggested per-game outfield stats were JavaScript-rendered
and would need a headless browser. That was wrong. The per-game per-player data
is **server-rendered** and reachable with a plain HTTP GET. The JS-rendered
`thetester.php` iframe contains only the scoreboard header (score, attendance,
referees), not player data. **No headless browser / Playwright is required.**

## Data shape (verified on games 12924, 12922, 12919)

A match report (`OlisDeildKarlaLeikur.php?ID={matchId}`) embeds per-team player
data in two server-rendered pages:

- `test6b.php?ID={matchId}` — **home** team
- `test7b.php?ID={matchId}` — **away** team

Each team page contains 5 HTML tables. Three carry the scoring inputs:

1. **Goalkeepers** — columns `Nafn, Varin, …, Mörk, Gul, 2Mín, Rau` — all
   inputs (goals + cards) in one row. ~2 rows.
2. **Outfield offensive** — columns `Nafn, Mörk, Skot, …` — goals. ~13–14 rows.
3. **Outfield discipline** — columns `Nafn, Lögleg Stopp, …, Gul, 2Mín, Rau` —
   cards. Same ~13–14 players as table 2.

The other two tables (passing combinations, GK-vs-shooter matrix) are empty or
irrelevant and are ignored.

Column meanings used by scoring:

| Icelandic | Meaning | Scoring field |
|-----------|---------|---------------|
| `Mörk`    | goals   | goals         |
| `Gul`     | yellow card | yellowCards |
| `2Mín`    | 2-minute suspension | twoMinuteSuspensions |
| `Rau`     | red card | redCards     |

Player identity in a row is `"{jersey}. {name}"`, e.g.
`"6. Arnór Snær Óskarsson"`. The outfield offensive and discipline tables are
**joined by this player string**. A player having a row means they appeared
(used for the appearance point).

## Scoring rule set (the engine's real values)

The fantasy scorer mirrors the domain `ScoringRuleSet` seeded by
`SeedScoringRuleSetsFunction` (`fantasy-v1`):

| Input | Points |
|-------|--------|
| goal | +2 |
| yellow card | −1 |
| 2-minute suspension | −2 |
| red card | −5 |
| appearance | +1 |

`points = 2·goals − 1·yellow − 2·twoMin − 5·red + 1·appearance`

These values are constants in the spike, documented as sourced from
`SeedScoringRuleSetsFunction.RuleSetDefinitions`. The spike does not read the
live `Config` table.

## Components (added to `tools/HbStatz.Spike`)

1. **`MatchReportClient`** — `GetTeamPageHtmlAsync(matchId, side)` fetches
   `test6b.php`/`test7b.php` for the given match via plain HTTP GET, reusing the
   existing descriptive User-Agent. (`side` ∈ {home, away}.)
2. **`StatsTableParser.ParseAll(html)`** — a small addition returning **all**
   tables in a page as `IReadOnlyList<ParsedTable>` (the existing `Parse`
   returns a single table; per-game pages have five). The existing single-table
   `Parse` is unchanged.
3. **`MatchStatLineBuilder`** — turns a team page's parsed tables into one
   `PlayerStatLine` per player:
   - GK table → a stat line directly (goals + cards from its columns).
   - Outfield: join the offensive table (goals) and discipline table (cards)
     by the `"{jersey}. {name}"` key into one stat line each.
   - Tags each line with `side` (home/away) and the raw player string
     (jersey + name preserved).
4. **`PlayerStatLine`** — record: `Side`, `Jersey`, `Name`, `Goals`,
   `YellowCards`, `TwoMinuteSuspensions`, `RedCards`. Appearance is implicit
   (the line exists).
5. **`FantasyScorer`** — pure function `Score(PlayerStatLine) -> double` applying
   the rule-set constants above. (Optionally returns a small breakdown for
   display.)
6. **`Program`** (new mode) — for each target match ID: fetch home + away pages,
   build all stat lines, score them, and print per game a table sorted by points
   descending: `player | side | G | Y | 2m | R | points`. Also writes
   `output/scored-{matchId}.csv`.

## Testing (TDD against committed fixtures)

- Capture `test6b.php`/`test7b.php` HTML for both target games (12922, 12919) as
  committed fixtures.
- `StatsTableParser.ParseAll` returns 5 tables for a team page.
- `MatchStatLineBuilder`:
  - Joins outfield offensive + discipline by player key into one line (assert a
    known outfield player's goals and cards from the fixture).
  - Emits GK lines with goals + cards from the single GK table.
  - Player count per team matches GK rows + outfield rows.
- `FantasyScorer` math on concrete lines, e.g.:
  - 9 goals, no cards, appeared → `2·9 + 1 = 19`.
  - 0 goals, 1 yellow, 1 two-minute, appeared → `−1 − 2 + 1 = −2`.

## Out of scope (YAGNI)

- No advanced-metric scoring (the engine consumes only goals/cards today).
- No headless browser, DI, retries, or hardening.
- No reconciliation of HBStatz players to HSÍ player IDs.
- No sweep over all games — just the two named match IDs.
- No reading of the live `Config` rule-set table; values are constants.

## Success criteria

1. Running the tool for match IDs 12922 and 12919 prints, per game, each
   player's per-game stat line and computed fantasy points.
2. `output/scored-12922.csv` and `output/scored-12919.csv` are written and
   committed as evidence.
3. Parser/builder/scorer logic is covered by tests against committed fixtures.
