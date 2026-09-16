# Fantasy Pricing: Prior-Season Blend — Design Spec

**Trigger:** Every player in Olís deild karla priced at the flat 5,000,000 ISK floor band at the start of the 2026/2027 season — reported by the user during debugging.
**Date:** 2026-09-16
**Labels:** bug, game, fantasy-only

## Goal

`FantasyPricing.Compute` forces a player's `Score` to `0` whenever their current-season game count is below `PriceRuleSet.MinGames` (currently 3), which lands every under-sampled player on the same floor-band price. At the start of a season, before anyone has played 3 games, this means the *entire* pool prices identically — no signal for managers to differentiate players by. This design replaces the hard cutoff with a fade: a player's price is anchored on their previous season's performance and smoothly transitions to a pure current-season price as they accumulate games this season.

## Scope decisions

These were settled during brainstorming and shape the whole design:

1. **Fading blend, not a hard cutoff.** Below `MinGames`, today's behavior jumps straight from "no price signal" (0) to "full signal" the instant game 3 is played. Instead, weight shifts continuously from the prior season's rate to the current season's rate as `currentGames` grows, avoiding both the season-start flat-price problem and an abrupt jump partway through the season.

2. **A new, separate `BlendGames` threshold — not `MinGames` reused.** `MinGames` (3) stays as the "is this season's own sample even usable" guard. Reusing it as the fade horizon would produce a 3-game taper, barely different from today's cutoff. `BlendGames` (a larger, independently tunable number, e.g. 10) is the point at which weight fully shifts to the current season alone.

3. **Prior-season data only counts from the same competition.** A qualifying prior season for a player is last season's stats in **the same competition** (Olís deild karla specifically, resolved via the stable `CompetitionId`, e.g. `"olis-karla"` — not the raw `tournamentId`, which changes every season). A player promoted up from Grill 66 deild, or genuinely new to the top flight, has no qualifying prior-season data and keeps today's exact behavior (floor band until they individually reach `MinGames` this season). Mixing in stats from a lower/different-difficulty tier was explicitly rejected as not comparable.

4. **The prior season must itself clear `MinGames`.** A player who barely played last season (fewer than `MinGames` games) doesn't have a reliable prior rate either — treated the same as "no qualifying prior season."

5. **Prior-season rate is recomputed through the current scoring config, not stored historically.** `priorSeasonRate` = last season's raw `AggregatedStats` run through the *current* `FantasyPlayerRatingFunction` / `ScoringRuleSet` — never whatever scoring version was live last season. Keeps prior and current rates on the same scale and automatically picks up scoring calibration changes (#27).

6. **Only the price-driving `Score` blends — `Rating` stays current-season-only.** `PlayerPricing.Rating` is documented as "current-season fantasy rating" and is a distinct, user-visible number from the price. `GetPlayerRatingUseCase` (a separate, general-purpose rating lookup) doesn't call `FantasyPricing` at all and is untouched by this change.

## Out of scope

- Changing `MinGames`'s role as the "usable sample" guard for players with no qualifying prior season — that fallback is unchanged.
- Looking back further than one season if a player also missed last season (e.g. injury) — no qualifying prior season, same fallback as today.
- Any change to `GetPlayerRatingUseCase` or the displayed `Rating` value.
- Retroactively blending seasons before 2026/2027 launched, or backfilling historical prices.

## Formula

```
w = min(currentGames / BlendGames, 1)
currentRate = currentGames > 0 ? currentRating / currentGames : 0

score =
  hasPriorSeason
    ? w * currentRate + (1 - w) * priorSeasonRate
    : (currentGames >= MinGames ? currentRate : 0)      // unchanged fallback

hasPriorSeason = priorGames >= MinGames   // same-competition prior season only
```

At `currentGames = 0` with a qualifying prior season, `score = priorSeasonRate` (this season's price starts where last season left off, instead of at the floor). The formula converges exactly to today's existing behavior once `currentGames >= BlendGames`, and is byte-for-byte unchanged for players with no qualifying prior season.

## Data model

`Ez.Handball.Domain/PriceRuleSet.cs` — `PriceRuleSet` gains:
```
BlendGames : int   // fade horizon; must be > MinGames
```

Seeded alongside the existing keys in `SeedPriceRuleSetsFunction.RuleSetDefinitions`:
```
("fantasy-price-v1", "blendGames", "10")
```

`TablePriceRuleSetRepository.GetAsync` parses `blendGames` the same way it already parses `minGames` (`TryGetInt`) — missing it makes `GetAsync` return `null`, same "rule set not found" behavior as a missing `minGames`/`currency`.

No new persisted entities — prior-season stats are read from the existing `PlayerStats` table, scoped to a resolved prior-season tournament id.

## Components

**`ITournamentScopeResolver`** (existing, extended) — gains a way to resolve "the tournament id(s) for this `CompetitionId` in a given (prior) season label," reusing the existing `CompetitionId`-filtered lookup that `ResolveTournamentIdsAsync` already does internally for the current season. This is the one place both consumers below call to find "last season's Olís deild karla" from this season's tournament scope.

**`FantasyPricing.Compute`** (existing, modified) — signature gains `AggregatedStats? previousSeasonStats`. Implements the formula above. Both callers below are responsible only for supplying `previousSeasonStats`; the blend math itself lives here and nowhere else, preserving the "formula lives in exactly one place" invariant already documented in this file.

**`PlayerStatsAggregator`** (existing, modified) — single-player path. Alongside its existing current-season fetch, resolves the prior season's label + this competition's tournament id(s) (via the extended scope resolver) and fetches/aggregates that player's stats the same way. `PlayerPriceService` passes both `AggregatedStats` results into `FantasyPricing.Compute`.

**`TablePlayerPoolRepository.GetAggregatedAsync`** (existing, modified) — bulk path. Resolves the prior season's tournament id(s) for the requested competition/tournament scope once per call, runs a second `PlayerStats` table query scoped to those ids, groups it into per-player `AggregatedStats` the same way the current-season query already is, and joins by `PlayerId` before `GetPlayerPoolUseCase` calls `FantasyPricing.Compute` per player.

## Error handling / edge cases

- No prior season exists at all for the resolved competition (e.g. first season the system has ever tracked) → `hasPriorSeason = false` for every player, identical to today's behavior.
- `BlendGames` missing from a seeded rule set → `PriceRuleSetRepository.GetAsync` returns `null`, surfacing as the existing `invalid_rule_set` response — not a silent fallback.
- A player who transferred clubs between seasons still qualifies — prior-season lookup is by `PlayerId` within the competition, independent of `ClubId`.
- A player who qualifies for prior-season blending but has `currentGames = 0` this season still needs their `Games` field (reported alongside `Price`/`Score`) to reflect `0`, not last season's count — only the price-driving rate blends, not the reported game count.

## Testing

- `FantasyPricingTests`: cases at `currentGames = 0` (pure prior rate), partway through `BlendGames` (blended), at/above `BlendGames` (pure current — matches existing `Compute_BelowMinGames_ScoreIsZero_FloorBand`-style tests), and no-qualifying-prior-season (byte-for-byte unchanged fallback, including when the prior season itself has `< MinGames`).
- `PlayerStatsAggregatorTests` / `PlayerPriceServiceTests`: prior-season resolution and fetch, including the "no prior season" and "prior season below MinGames" cases.
- `TablePlayerPoolRepositoryTests`: bulk prior-season query + join, including players present in the current query but absent from the prior one.
- `GetPlayerPoolUseCaseTests`: end-to-end case showing a blended (non-floor-band) price at season start for a player with qualifying prior-season data, alongside an unchanged floor-band case for one without.

## Related

- Depends on none; directly resolves the season-start flat-pricing symptom reported for Olís deild karla (tournament `9142`, competition `olis-karla`, 2026/2027 season).
- Scoring calibration (#27) governs the underlying rating formula this blend reuses unmodified.
