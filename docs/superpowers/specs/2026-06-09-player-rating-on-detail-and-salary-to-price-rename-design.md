# Add fantasy rating to player-detail + rename "salary" → "price" (Backend #78)

**Status:** design approved
**Date:** 2026-06-09
**Issue:** [Backend #78](https://github.com/kromby/Ez.Handball.Backend/issues/78)
**Consumed by:** [Web #23](https://github.com/kromby/Ez.Handball.Web/issues/23) (player-page rating + price display)

## Goal

Two things, done together:

1. **Surface `rating` on `GET /api/players/{playerId}`** (the issue) — the
   current-season fantasy rating, alongside the `price` already returned, so the
   Web player page can show both without a second round-trip.
2. **Erase the "salary" vocabulary from the code** — in the fantasy game the
   concept is a player **price**, not a salary. "Salary" was introduced in #52 and
   has spread across the domain, services, a use case, and a public endpoint. We
   rename it all to "price."

The rating value the issue wants is **already computed** one layer down:
`FantasyPricing.Compute` returns `FantasyPriceResult(Rating, Score, Cost)` and the
service currently discards `Rating` when it builds its result record. Surfacing it
is a thread-through, not a new computation — no second aggregation, no N+1.

## Key finding — the rename is code-only

The stored configuration already speaks "price." `TablePriceRuleSetRepository`
(today `TableSalaryRuleSetRepository`) reads the `Config` table partition
`fantasy-price-v{version}`, and `SalaryRuleSet.Name` is already
`"fantasy-price-v{Version}"`. **No stored rows, partition keys, config values, or
table schemas change.** This is purely a C# identifier rename plus one new field.

## Part 1 — Rename "salary" → "price"

Pure identifier rename across the codebase. Behaviour is unchanged; only names move.

| Today | Becomes |
|-------|---------|
| `PlayerCost(Amount, Currency)` | `PlayerPrice(Amount, Currency)` — the money value type behind the JSON `price` object |
| `PlayerSalary(PlayerId, Cost, Score, Games, Version)` | `PlayerPricing(PlayerId, Price, Score, Games, Version, Rating)` — see Part 2 for the new `Rating` field; `Cost` field → `Price` |
| `SalaryRuleSet` / `SalaryBand` | `PriceRuleSet` / `PriceBand` |
| `ISalaryRuleSetRepository` / `TableSalaryRuleSetRepository` | `IPriceRuleSetRepository` / `TablePriceRuleSetRepository` |
| `IPlayerSalaryService` / `PlayerSalaryService` | `IPlayerPriceService` / `PlayerPriceService` |
| `GetSalaryAsync(...)` | `GetPriceAsync(...)` |
| `GetPlayerSalaryUseCase` / `GetPlayerSalaryResult` | **deleted** — see endpoint removal below |

Callers updated to the new names (type references and local variable names
`salary` → `price`): `SellPlayerUseCase`, `GetSquadUseCase`,
`GetBuyDecisionUseCase`, `GetPlayerPoolUseCase`, `GetPlayerProfileUseCase`,
`FantasyPricing`, and the DI registrations in `Program.cs`.

The `PlayerPricing` record keeps its `Score`, `Games`, and `Version` fields — they
are consumed by `GetBuyDecisionUseCase` (`Version`) and remain part of the
compute result. No buy/sell/squad/pool **logic** changes; only the type names.

### Remove the dedicated salary endpoint

`GET /api/players/{playerId}/salary` and its `GetPlayerSalaryUseCase` are
**deleted entirely.** Player-detail already covers the default-scope price, and now
also carries `rating`. The endpoint's only added capability was query-param scoping
(`version` / `season` / `tournamentId`); that is intentionally dropped — no current
consumer uses it.

- **Web impact: none.** The Web player page reads the `price` field off
  player-detail and only *labels* it "Salary/Laun" in its own UI strings; it never
  calls `/salary`. The display label is the Web's concern (see the web-developer
  prompt accompanying this spec).
- Tests `GetPlayerSalaryUseCaseTests` and `PlayerSalaryEndpointTests` are deleted.

## Part 2 — Add `rating` to player-detail

1. **`PlayerPricing` gains `double Rating`** — set from the existing
   `FantasyPriceResult.Rating` that the pricing service currently throws away.
2. **`PlayerPriceService.GetPriceAsync`** sets `Rating = result.Rating`. One line;
   no new aggregation, no extra query.
3. **`GetPlayerProfileResult.Found(Player Player, PlayerPrice? Price, double? Rating)`** —
   carries price and rating as two explicit fields, matching the response shape.
4. **`GetPlayerProfileUseCase`** passes `pricing?.Price` and `pricing?.Rating`. Both
   are `null` together exactly when the price rule-set (or its scoring rule-set) is
   missing — the same guard that already nulls `price`. `0` flows through naturally
   for a player with no current-season games.
5. **`Program.cs`** player-detail response adds `rating = f.Rating`.

### Response contract

```jsonc
{
  "playerId": "12345",
  "name": "...",
  "jerseyNumber": "23",
  "dateOfBirth": "1990-07-19",
  "age": 35,
  "teamId": "385-karlar",
  "clubId": "385",
  "clubName": "Stjarnan",
  "gender": "karlar",
  "position": "VS",
  "price": { "amount": 11000000, "currency": "ISK" },
  "rating": 128.0
}
```

- `rating`: number — current-season fantasy rating (weighted season totals:
  goals×2 + appearances×1 + yellow×−1 + twoMin×−2 + red×−5), the same value the
  pool / `/rating` endpoint returns under the default (current-season, fantasy)
  scope.
- **Nullability:** `null` only when uncomputable (scoring/price rule-set missing —
  the same condition that already leaves `price` null). `0` is valid (no games in
  scope).
- No new query params — uses the endpoint's existing default scope.

## Testing

- **`GetPlayerProfileUseCaseTests`:** player with games → `rating > 0` and `price`
  present; rule-set missing → both `price` and `rating` `null`.
- **Player-detail endpoint test:** `rating` present in the JSON; `0` for a player
  with no current-season games.
- **Rename ripple:** update `FantasyPricingTests`, `PlayerSalaryServiceTests`
  (→ `PlayerPriceServiceTests`), `GetPlayerPoolUseCaseTests`, and the squad / buy /
  sell tests to the new type names. Behaviour assertions unchanged.
- **Delete:** `GetPlayerSalaryUseCaseTests` and `PlayerSalaryEndpointTests`.
- Whole suite green (currently ~659 tests, minus the deleted salary-endpoint
  tests).

## Acceptance criteria

- `rating` present on `GET /api/players/{playerId}`, computed from the same
  aggregated stats already used for `price` (no second aggregation / no N+1).
- `rating` is `null` when uncomputable (mirrors `price`); `0` for no-games-in-scope.
- No "salary"-named type, interface, member, or route remains in the codebase
  (`grep -ri salary` over `*.cs` returns nothing).
- `GET /api/players/{playerId}/salary` is removed.
- No stored config / table data changes; existing price values are byte-for-byte
  unchanged.

## Out of scope

- The Web player-page display label ("Salary/Laun" → "Price/Verð") and ungating the
  `rating` field — owned by Web #23; see the web-developer prompt.
- Buy / sell / squad / pool **logic** — only type names change.
- Stored config / data — already keyed on "price."

## Dependencies

- Consumes the rating + pricing primitives from #52 / #67 (no re-derivation).
- Consumed by Web #23 (player-page rating + price display).
