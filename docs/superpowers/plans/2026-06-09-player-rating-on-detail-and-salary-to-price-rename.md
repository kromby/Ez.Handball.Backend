# Player-detail rating + salary→price rename — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `rating` field to `GET /api/players/{playerId}` and erase the "salary" vocabulary from the codebase, renaming it to "price."

**Architecture:** The rating value is already computed inside `FantasyPricing.Compute` (returned as `FantasyPriceResult.Rating`) and discarded by the pricing service — surfacing it is a thread-through, not a new computation. The rename is a pure identifier change: stored config already keys on `fantasy-price-v{n}`, so no data, schema, or config values move. Work proceeds: delete the dead `/salary` surface first (so the blanket rename can't resurrect it), then rename `PlayerCost`, then sweep `salary`→`price` in one ordered pass, then add the new `rating` behaviour via TDD.

**Tech Stack:** C# / .NET 8, clean architecture (Domain / Application / Infrastructure / Api / Ingestion), xUnit + Moq, Azure Table Storage (Azurite for integration tests).

**Spec:** `docs/superpowers/specs/2026-06-09-player-rating-on-detail-and-salary-to-price-rename-design.md`

---

## Conventions used in every task

- **Build:** `dotnet build Ez.Handball.sln`
- **Full test suite:** `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
  - **Azurite must be running** (some tests are Table-storage integration tests):
    `azurite --silent --location /tmp/azurite-test &`
- All commands run from the repo root `Ez.Handball.Backend/`.
- **Why ordered substring (not word-boundary) renaming in Task 3:** "salary" appears inside compound test identifiers (`Fantasy_MissingSalaryRuleSet`, `SalaryOf`, `PassesScopeThroughToSalaryService`), helper names, comments, the seed `[Function]` name, and the seed route — word boundaries miss those, and the spec's acceptance is `grep -rni salary` returns **nothing**. Ordered longest-first substitutions catch every occurrence while avoiding collisions (e.g. `PlayerSalaryService` is rewritten before `PlayerSalary`, so it never becomes the wrong thing).

---

## Task 1: Delete the dead `/salary` endpoint and its use case

`GET /api/players/{playerId}/salary` is removed; player-detail covers the default-scope price (and, after Task 5, the rating). Done **first** so the Task 3 rename does not turn `GetPlayerSalaryUseCase` into a surviving `GetPlayerPriceUseCase`. The pricing **service** (`IPlayerSalaryService`) is *not* touched here — only the use case + endpoint.

**Files:**
- Delete: `Ez.Handball.Application/UseCases/GetPlayerSalaryUseCase.cs`
- Delete: `Ez.Handball.Tests/Application/UseCases/GetPlayerSalaryUseCaseTests.cs`
- Delete: `Ez.Handball.Tests/Api/Endpoints/PlayerSalaryEndpointTests.cs`
- Modify: `Ez.Handball.Api/Program.cs`

- [ ] **Step 1: Delete the use case and its two test classes**

```bash
git rm Ez.Handball.Application/UseCases/GetPlayerSalaryUseCase.cs \
       Ez.Handball.Tests/Application/UseCases/GetPlayerSalaryUseCaseTests.cs \
       Ez.Handball.Tests/Api/Endpoints/PlayerSalaryEndpointTests.cs
```

- [ ] **Step 2: Remove the DI registration in `Program.cs` (line ~134)**

Delete exactly this line:
```csharp
builder.Services.AddScoped<IGetPlayerSalaryUseCase, GetPlayerSalaryUseCase>();
```
**Keep** the line above it (`builder.Services.AddScoped<IPlayerSalaryService, PlayerSalaryService>();`) — that service is still used and is renamed in Task 3.

- [ ] **Step 3: Remove the endpoint mapping in `Program.cs` (lines ~294–313)**

Delete the entire block, from:
```csharp
app.MapGet("/api/players/{playerId}/salary", async Task<IResult> (
```
through its closing:
```csharp
    };
});
```
(The handler takes `IGetPlayerSalaryUseCase uc`, switches on `GetPlayerSalaryResult.*`, and returns `f.Salary`.)

- [ ] **Step 4: Verify nothing references the deleted symbols**

Run: `grep -rn --include='*.cs' --exclude-dir=bin --exclude-dir=obj -e 'GetPlayerSalary' -e 'players/{playerId}/salary' .`
Expected: no output.

- [ ] **Step 5: Build + test**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: build succeeds; all tests pass (suite is smaller by two deleted classes).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: remove GET /api/players/{id}/salary endpoint + use case (#78)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Rename `PlayerCost` → `PlayerPrice`

`PlayerCost` is the money value type `{ Amount, Currency }` behind every JSON `price` object. Rename to `PlayerPrice` for vocabulary consistency. ~15 production sites + several test files. The JSON shape is unchanged. This token contains no "salary", so it is independent of Task 3.

**Files:**
- Rename: `Ez.Handball.Domain/PlayerCost.cs` → `Ez.Handball.Domain/PlayerPrice.cs`
- Modify (token only): all `.cs` files referencing `PlayerCost`.

- [ ] **Step 1: Rename the type file**

```bash
git mv Ez.Handball.Domain/PlayerCost.cs Ez.Handball.Domain/PlayerPrice.cs
```

- [ ] **Step 2: Rename the token everywhere**

```bash
find Ez.Handball.Domain Ez.Handball.Application Ez.Handball.Infrastructure \
     Ez.Handball.Api Ez.Handball.Ingestion Ez.Handball.Tests \
     -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 perl -i -pe 's/\bPlayerCost\b/PlayerPrice/g'
```

- [ ] **Step 3: Verify no `PlayerCost` remains**

Run: `grep -rn --include='*.cs' --exclude-dir=bin --exclude-dir=obj 'PlayerCost' .`
Expected: no output.

- [ ] **Step 4: Build + test**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: build succeeds; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename PlayerCost -> PlayerPrice (#78)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Sweep "salary" → "price" (ordered rename + file moves + member rename)

One ordered substring pass renames every remaining "salary"/"Salary" identifier, comment, function name, and route. Then the `Cost` member on the pricing bundle + `FantasyPriceResult` is renamed to `Price` (these can't be done by the blanket pass — `Cost` is also a member on unrelated records). Behaviour is unchanged.

**Files renamed (git mv):**
- `Ez.Handball.Application/Abstractions/IPlayerSalaryService.cs` → `IPlayerPriceService.cs`
- `Ez.Handball.Application/Services/PlayerSalaryService.cs` → `PlayerPriceService.cs`
- `Ez.Handball.Domain/PlayerSalary.cs` → `PlayerPricing.cs`
- `Ez.Handball.Domain/SalaryRuleSet.cs` → `PriceRuleSet.cs`
- `Ez.Handball.Application/Abstractions/ISalaryRuleSetRepository.cs` → `IPriceRuleSetRepository.cs`
- `Ez.Handball.Infrastructure/TableAccess/TableSalaryRuleSetRepository.cs` → `TablePriceRuleSetRepository.cs`
- `Ez.Handball.Ingestion/Functions/SeedSalaryRuleSetsFunction.cs` → `SeedPriceRuleSetsFunction.cs`
- `Ez.Handball.Tests/Application/Services/PlayerSalaryServiceTests.cs` → `PlayerPriceServiceTests.cs`
- `Ez.Handball.Tests/Domain/SalaryRuleSetTests.cs` → `PriceRuleSetTests.cs`
- `Ez.Handball.Tests/Infrastructure/Tables/TableSalaryRuleSetRepositoryTests.cs` → `TablePriceRuleSetRepositoryTests.cs`
- `Ez.Handball.Tests/Ingestion/Functions/SeedSalaryRuleSetsFunctionTests.cs` → `SeedPriceRuleSetsFunctionTests.cs`

**Files modified for the `Cost`→`Price` member:** `PlayerPricing.cs`, `FantasyPricing.cs`, `PlayerPriceService.cs`, `GetSquadUseCase.cs`, `SellPlayerUseCase.cs`, `GetBuyDecisionUseCase.cs`, `GetPlayerProfileUseCase.cs`, `GetPlayerPoolUseCase.cs`, `PlayerPriceServiceTests.cs`.

- [ ] **Step 1: Rename the files**

```bash
git mv Ez.Handball.Application/Abstractions/IPlayerSalaryService.cs Ez.Handball.Application/Abstractions/IPlayerPriceService.cs
git mv Ez.Handball.Application/Services/PlayerSalaryService.cs Ez.Handball.Application/Services/PlayerPriceService.cs
git mv Ez.Handball.Domain/PlayerSalary.cs Ez.Handball.Domain/PlayerPricing.cs
git mv Ez.Handball.Domain/SalaryRuleSet.cs Ez.Handball.Domain/PriceRuleSet.cs
git mv Ez.Handball.Application/Abstractions/ISalaryRuleSetRepository.cs Ez.Handball.Application/Abstractions/IPriceRuleSetRepository.cs
git mv Ez.Handball.Infrastructure/TableAccess/TableSalaryRuleSetRepository.cs Ez.Handball.Infrastructure/TableAccess/TablePriceRuleSetRepository.cs
git mv Ez.Handball.Ingestion/Functions/SeedSalaryRuleSetsFunction.cs Ez.Handball.Ingestion/Functions/SeedPriceRuleSetsFunction.cs
git mv Ez.Handball.Tests/Application/Services/PlayerSalaryServiceTests.cs Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs
git mv Ez.Handball.Tests/Domain/SalaryRuleSetTests.cs Ez.Handball.Tests/Domain/PriceRuleSetTests.cs
git mv Ez.Handball.Tests/Infrastructure/Tables/TableSalaryRuleSetRepositoryTests.cs Ez.Handball.Tests/Infrastructure/Tables/TablePriceRuleSetRepositoryTests.cs
git mv Ez.Handball.Tests/Ingestion/Functions/SeedSalaryRuleSetsFunctionTests.cs Ez.Handball.Tests/Ingestion/Functions/SeedPriceRuleSetsFunctionTests.cs
```

- [ ] **Step 2: Ordered substring rename across all `.cs` files**

The substitution order is deliberate — longer/containing identifiers first so they aren't half-rewritten, the bundle `PlayerSalary`→`PlayerPricing` before the generic pass (so it never collides with `PlayerPrice`), then a generic mop-up for compound names, comments, the `[Function]` name, and the route.

```bash
find Ez.Handball.Domain Ez.Handball.Application Ez.Handball.Infrastructure \
     Ez.Handball.Api Ez.Handball.Ingestion Ez.Handball.Tests \
     -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 perl -i -pe '
      s/IPlayerSalaryService/IPlayerPriceService/g;
      s/PlayerSalaryService/PlayerPriceService/g;
      s/PlayerSalary/PlayerPricing/g;
      s/ISalaryRuleSetRepository/IPriceRuleSetRepository/g;
      s/TableSalaryRuleSetRepository/TablePriceRuleSetRepository/g;
      s/SeedSalaryRuleSetsFunction/SeedPriceRuleSetsFunction/g;
      s/SalaryRuleSet/PriceRuleSet/g;
      s/SalaryBand/PriceBand/g;
      s/GetSalaryAsync/GetPriceAsync/g;
      s/Salary/Price/g;
      s/salary/price/g;
    '
```

After this pass: the `[Function("SeedSalaryRuleSets")]` attribute becomes `[Function("SeedPriceRuleSets")]` (via `SalaryRuleSet`→`PriceRuleSet` on `SeedSalaryRuleSets`), and the route `"seed/salary-rule-sets"` becomes `"seed/price-rule-sets"` (via the lowercase pass). **Operational note:** the seeding route changes accordingly; the seeded data is byte-for-byte identical.

- [ ] **Step 3: Verify NO "salary" remains anywhere**

Run: `grep -rni --include='*.cs' --exclude-dir=bin --exclude-dir=obj 'salary' .`
Expected: no output. (If anything prints, it is a missed occurrence — add a targeted edit before continuing.)

- [ ] **Step 4: Build to find the broken `.Cost` member references**

Run: `dotnet build Ez.Handball.sln`
Expected: build SUCCEEDS at this point — the blanket rename is internally consistent and the `Cost` member still exists on the renamed `PlayerPricing`/`FantasyPriceResult`. (Steps 5–6 rename that member; they are a separate, optional-for-compilation but spec-required consistency change.)

- [ ] **Step 5: Rename the bundle's `Cost` member → `Price`**

In `Ez.Handball.Domain/PlayerPricing.cs`, change:
```csharp
    PlayerPrice Cost,
```
to:
```csharp
    PlayerPrice Price,
```

In `Ez.Handball.Application/Services/FantasyPricing.cs`, change the record-struct definition:
```csharp
public readonly record struct FantasyPriceResult(double Rating, double Score, PlayerPrice Cost);
```
to:
```csharp
public readonly record struct FantasyPriceResult(double Rating, double Score, PlayerPrice Price);
```

- [ ] **Step 6: Update every `.Cost` access of those two types**

Apply each edit exactly (these are the only accesses of the renamed member):

`Ez.Handball.Application/Services/PlayerPriceService.cs` — in the `return new PlayerPricing(...)`:
- `playerId, result.Cost, result.Score, stats.Games, priceRuleSet.Name);` → `playerId, result.Price, result.Score, stats.Games, priceRuleSet.Name);`

`Ez.Handball.Application/UseCases/GetSquadUseCase.cs`:
- `Price: price.Cost,` → `Price: price.Price,`
- `squadValue += price.Cost.Amount;` → `squadValue += price.Price.Amount;`

`Ez.Handball.Application/UseCases/SellPlayerUseCase.cs`:
- `var credit = SellValue.Compute(entry.PricePaidAmount, price.Cost.Amount, constraints.SellOnFeeRate);` → `... price.Price.Amount ...`

`Ez.Handball.Application/UseCases/GetBuyDecisionUseCase.cs`:
- `playerId, player.Position, price.Cost, price.Version, constraints, squad, context);` → `playerId, player.Position, price.Price, price.Version, constraints, squad, context);`

`Ez.Handball.Application/UseCases/GetPlayerProfileUseCase.cs`:
- `return new GetPlayerProfileResult.Found(player, price?.Cost);` → `return new GetPlayerProfileResult.Found(player, price?.Price);`

`Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`:
- `Price: priced.Cost,` → `Price: priced.Price,`

`Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs` (in `PerGameScore_SelectsBand`, `BelowMinGames_FloorBand`, `ZeroGames_FloorBand`):
- `price!.Cost.Amount` → `price!.Price.Amount`
- `price.Cost.Amount` → `price.Price.Amount`
- `price.Cost.Currency` → `price.Price.Currency`

- [ ] **Step 7: Verify no stray `.Cost` references to the pricing types remain**

Run: `grep -rn --include='*.cs' --exclude-dir=bin --exclude-dir=obj -e 'price.Cost' -e 'priced.Cost' -e 'result.Cost' .`
Expected: no output. (Other records' `.Cost` members — `BuyDecision`, `IBuyPlayerFunction`, `BuyPlayerInputs` — are intentionally unchanged.)

- [ ] **Step 8: Build + full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: build succeeds; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: rename salary -> price across types, service, seed fn, route (#78)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Add `Rating` to `PlayerPricing` and populate it from the service (TDD)

`FantasyPricing.Compute` already returns `FantasyPriceResult.Rating`; the service discards it. Add a `Rating` field to the `PlayerPricing` bundle and populate it. Adding a required positional param breaks every `new PlayerPricing(...)` / target-typed `new(...)` site, so all are updated in this task.

**Files:**
- Test: `Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs`
- Modify: `Ez.Handball.Domain/PlayerPricing.cs`, `Ez.Handball.Application/Services/PlayerPriceService.cs`
- Modify (construction sites): `Ez.Handball.Tests/Application/UseCases/{GetPlayerProfileUseCaseTests,GetBuyDecisionUseCaseTests,GetSquadUseCaseTests,SellPlayerUseCaseTests}.cs`

- [ ] **Step 1: Write the failing tests**

Add to `PlayerPriceServiceTests.cs` (the existing `PerGameScore_SelectsBand` setup yields rating = 20×2 + 8×1 = 48):

```csharp
    [Fact]
    public async Task Rating_IsWeightedSeasonTotal()
    {
        Aggregate(games: 8, goals: 20);   // rating = 20*2 + 8*1 = 48

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(48, price!.Rating);
    }

    [Fact]
    public async Task Rating_ZeroGames_IsZero()
    {
        Aggregate(games: 0, goals: 0);

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(0, price!.Rating);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPriceServiceTests.Rating"`
Expected: COMPILE FAILURE — `PlayerPricing` has no `Rating` member.

- [ ] **Step 3: Add the `Rating` field to `PlayerPricing.cs`**

The file is now (after Task 3):
```csharp
namespace Ez.Handball.Domain;

public sealed record PlayerPricing(
    string PlayerId,
    PlayerPrice Price,
    double Score,    // points per game (0 when below the min-games guard)
    int Games,
    string Version); // the price rule set name, e.g. "fantasy-price-v1"
```
Change it to:
```csharp
namespace Ez.Handball.Domain;

public sealed record PlayerPricing(
    string PlayerId,
    PlayerPrice Price,
    double Score,    // points per game (0 when below the min-games guard)
    int Games,
    string Version,  // the price rule set name, e.g. "fantasy-price-v1"
    double Rating);  // current-season fantasy rating (the #52 metric)
```

- [ ] **Step 4: Populate `Rating` in `PlayerPriceService.cs`**

The construction is now:
```csharp
        var result = _pricing.Compute(playerId, stats, scoring, priceRuleSet, ctx);
        return new PlayerPricing(
            playerId, result.Price, result.Score, stats.Games, priceRuleSet.Name);
```
Change the `return` to:
```csharp
        var result = _pricing.Compute(playerId, stats, scoring, priceRuleSet, ctx);
        return new PlayerPricing(
            playerId, result.Price, result.Score, stats.Games, priceRuleSet.Name, result.Rating);
```

- [ ] **Step 5: Fix the four test construction sites (rating value is arbitrary — these tests don't assert on it)**

`GetPlayerProfileUseCaseTests.cs` (`ExecuteAsync_PlayerExists_...`):
```csharp
            .ReturnsAsync(new PlayerPricing(
                "12345", new PlayerPrice(11_000_000, "ISK"), Score: 11, Games: 10, Version: "fantasy-price-v1"));
```
→ add `, Rating: 128`:
```csharp
            .ReturnsAsync(new PlayerPricing(
                "12345", new PlayerPrice(11_000_000, "ISK"), Score: 11, Games: 10, Version: "fantasy-price-v1", Rating: 128));
```

`GetBuyDecisionUseCaseTests.cs` (the `Price()` helper, renamed from `Salary()`):
```csharp
        new("p1", new PlayerPrice(20_000_000, "ISK"), 6, 8, "fantasy-price-v1");
```
→ `new("p1", new PlayerPrice(20_000_000, "ISK"), 6, 8, "fantasy-price-v1", 48);`

`GetSquadUseCaseTests.cs` (the `PriceOf` helper, renamed from `SalaryOf`):
```csharp
        new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1");
```
→ `new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1", 50.0);`

`SellPlayerUseCaseTests.cs` (the `PriceOf` helper, renamed from `SalaryOf`):
```csharp
        new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1");
```
→ `new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1", 50.0);`

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPriceServiceTests.Rating"`
Expected: PASS (both).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: surface fantasy rating from PlayerPriceService (#78)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Surface `rating` on the player-detail response (TDD)

Thread the rating from `PlayerPricing` through `GetPlayerProfileResult.Found` to the `GET /api/players/{playerId}` JSON.

**Files:**
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerProfileUseCaseTests.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`
- Modify: `Ez.Handball.Application/UseCases/GetPlayerProfileUseCase.cs`
- Modify: `Ez.Handball.Api/Program.cs` (player-detail response, lines ~190–203)

- [ ] **Step 1: Write the failing use-case assertions**

In `GetPlayerProfileUseCaseTests.cs`, in `ExecuteAsync_PlayerExists_ReturnsFoundWithPlayerAndPrice`, after the existing price asserts, add:
```csharp
        Assert.Equal(128, found.Rating);
```
And in `ExecuteAsync_RuleSetMissing_ReturnsFoundWithNullPrice`, after `Assert.Null(found.Price);`, add:
```csharp
        Assert.Null(found.Rating);
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerProfileUseCaseTests"`
Expected: COMPILE FAILURE — `GetPlayerProfileResult.Found` has no `Rating` member.

- [ ] **Step 3: Add `Rating` to the `Found` result and populate it**

In `GetPlayerProfileUseCase.cs`, change the result record (currently `Found(Player Player, PlayerPrice? Price)`):
```csharp
    public sealed record Found(Player Player, PlayerPrice? Price, double? Rating) : GetPlayerProfileResult;
```
And change the `ExecuteAsync` return (currently `return new GetPlayerProfileResult.Found(player, price?.Price);`):
```csharp
        var price = await _price.GetPriceAsync(playerId, DefaultPriceVersion, null, null, ct);
        return new GetPlayerProfileResult.Found(player, price?.Price, price?.Rating);
```
(The local is named `price` after Task 3's rename; the field is `_price`. Both `Price` and `Rating` are null together when the rule-set is missing.)

- [ ] **Step 4: Run the use-case tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerProfileUseCaseTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing endpoint tests**

In `PlayerEndpointsTests.cs`, update `GetPlayer_Existing_Returns200WithProfile`:
- change the `Found` construction to include a rating:
  `.ReturnsAsync(new GetPlayerProfileResult.Found(player, new PlayerPrice(11_000_000, "ISK"), 128.0));`
- after the price asserts, add:
```csharp
        Assert.Equal(128.0, body.GetProperty("rating").GetDouble());
```
Add a new test for the no-games case:
```csharp
    [Fact]
    public async Task GetPlayer_NoGamesInScope_Returns200WithRatingZero()
    {
        var player = new Player(
            "12345", "Aron Pálmarsson", "23",
            new DateOnly(1990, 7, 19),
            35, "385-karlar", "385", "Stjarnan", "karlar", "VS");

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(player, new PlayerPrice(5_000_000, "ISK"), 0.0));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.0, body.GetProperty("rating").GetDouble());
    }
```

- [ ] **Step 6: Run to verify failure**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerEndpointsTests"`
Expected: failures — the `rating` JSON property is missing.

- [ ] **Step 7: Add `rating` to the player-detail response in `Program.cs`**

The player-detail handler (`app.MapGet("/api/players/{playerId}", ...)`) maps the `Found` case to an anonymous object ending with `price = f.Price`. Change that tail:
```csharp
            f.Player.Position,
            price = f.Price
        }),
```
to:
```csharp
            f.Player.Position,
            price = f.Price,
            rating = f.Rating
        }),
```

- [ ] **Step 8: Run the endpoint tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerEndpointsTests"`
Expected: PASS.

- [ ] **Step 9: Final acceptance — no "salary" anywhere + full suite**

Run: `grep -rni --include='*.cs' --exclude-dir=bin --exclude-dir=obj 'salary' .`
Expected: no output (the spec's headline acceptance criterion).

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: build succeeds; all tests pass.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add rating to player-detail response (#78)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Done-when

- `GET /api/players/{playerId}` returns `rating` alongside `price`; `rating` is a number, `0` for no-games-in-scope, `null` when the rule-set is missing (mirrors `price`).
- `GET /api/players/{playerId}/salary` no longer exists.
- `grep -rni salary --include='*.cs'` over the repo returns nothing.
- No stored config / Table data changed; existing price values are byte-for-byte identical.
- Full test suite green.

## Follow-up (separate, not in this plan)

- **Web #23:** relabel the player-page "Salary/Laun" → "Price/Verð". The `rating` field lights up automatically once this backend ships (the Web already reads `p.rating`). See `Ez.Handball.Web/PROMPT-salary-to-price-relabel.md`.
- **Deployment runbook:** the seed route is now `POST /api/seed/price-rule-sets` (was `seed/salary-rule-sets`). Update any ops notes / the deployment memory.
