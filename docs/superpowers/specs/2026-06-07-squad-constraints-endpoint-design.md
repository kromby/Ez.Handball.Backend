# Public squad-constraints read endpoint — Design (Backend #66)

**Date:** 2026-06-07
**Issue:** [kromby/Ez.Handball.Backend#66](https://github.com/kromby/Ez.Handball.Backend/issues/66)
**Mode:** Fantasy-only. Public (unauthenticated).

## Goal

Expose the fantasy squad rule-set constraints (squad-size cap, starting budget,
per-position limits) over a public read endpoint, so the Squad Builder UI (Web#16)
can render squad/position counters and pre-validate Buy buttons client-side without
calling the per-player buy decision (#53) for every row.

`ISquadConstraintsRepository` already exists and is consumed by the buy decision (#53)
and squad read (#54). This is a thin read over it — values are returned exactly as
seeded by `SeedSquadConstraints`; nothing is re-derived.

## Contract

```
GET /api/squad/constraints?flavor=fantasy&ruleSetVersion=1    (public — no auth)
```

`200`:

```jsonc
{
  "ruleSetVersion": 1,                                       // resolves "fantasy-squad-v1"
  "maxSquadSize": 15,
  "startingCap": { "amount": 100000000, "currency": "ISK" },
  "posLimits": { "GK": 2, "LW": 2, "RW": 2, "LB": 3, "CB": 3, "RB": 3, "P": 2 }
}
```

- `flavor`: blank or `fantasy` (case-insensitive) accepted; anything else → `400 { "error": "invalid_flavor" }`.
- `ruleSetVersion` (int, optional): selects the constraints group (`fantasy-squad-v{n}`);
  omitted → the default/current version (`1`). Unknown → `400 { "error": "invalid_rule_set" }`.
- `posLimits`: string→int map keyed by the stored position codes — placeholder vocabulary,
  owner review still pending; the endpoint returns whatever is seeded.

## Why public

This is rule-set configuration, like `/api/clubs`, `/api/seasons`, `/api/genders`,
and `/api/tournaments` — all mapped inline in `Program.cs` with no `RequireAuthorization()`.
The squad-constraints endpoint joins them.

## Components

### 1. Application — `GetSquadConstraintsUseCase`

Location: `Ez.Handball.Application/UseCases/GetSquadConstraintsUseCase.cs`.
Mirrors `GetSquadUseCase` (#54). Depends only on `ISquadConstraintsRepository`.

```csharp
public interface IGetSquadConstraintsUseCase
{
    Task<GetSquadConstraintsResult> ExecuteAsync(int? ruleSetVersion, CancellationToken ct);
}

public abstract record GetSquadConstraintsResult
{
    public sealed record RuleSetNotFound : GetSquadConstraintsResult
    {
        public static readonly RuleSetNotFound Instance = new();
    }
    public sealed record Found(SquadConstraints Constraints) : GetSquadConstraintsResult;
}
```

Logic:

```
version = ruleSetVersion ?? DefaultVersion   // DefaultVersion = 1
constraints = await repo.GetAsync(version, ct)
return constraints is null
    ? GetSquadConstraintsResult.RuleSetNotFound.Instance
    : new GetSquadConstraintsResult.Found(constraints);
```

`DefaultVersion = 1` is a local `const`, consistent with the same constant in
`GetSquadUseCase`. The existing minor duplication is accepted; a shared constant is
not introduced yet (YAGNI).

### 2. API edge — inline in `Program.cs`

Mapped next to `/api/clubs` and `/api/seasons`, **public (no `RequireAuthorization()`)**:

```csharp
app.MapGet("/api/squad/constraints", async Task<IResult> (
    string? flavor,
    int? ruleSetVersion,
    IGetSquadConstraintsUseCase uc,
    CancellationToken ct) =>
{
    // Fantasy-only: blank or "fantasy" accepted; anything else rejected.
    // Mirrors the edge check in SquadEndpoints — flavor never reaches the use case.
    if (!string.IsNullOrWhiteSpace(flavor)
        && !flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "invalid_flavor" });

    var result = await uc.ExecuteAsync(ruleSetVersion, ct);
    return result switch
    {
        GetSquadConstraintsResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
        GetSquadConstraintsResult.Found f => Results.Ok(new
        {
            ruleSetVersion = f.Constraints.Version,
            maxSquadSize   = f.Constraints.MaxSquadSize,
            startingCap    = new PlayerCost(f.Constraints.StartingCap, f.Constraints.Currency),
            posLimits      = f.Constraints.PositionLimits
        }),
        _ => Results.Problem()
    };
});
```

Reusing `PlayerCost(amount, currency)` yields the exact `{ amount, currency }` money
shape the rest of the API already emits (e.g. `price`, `remainingBudget`).
`posLimits` is the `IReadOnlyDictionary<string,int>` passed straight through.

### 3. Dependency injection

One line in `Program.cs`, alongside the other use-case registrations:

```csharp
builder.Services.AddScoped<IGetSquadConstraintsUseCase, GetSquadConstraintsUseCase>();
```

`ISquadConstraintsRepository` is already registered (consumed by #53/#54 via
`AddTableStorageInfrastructure`), so no infrastructure wiring is needed.

## Testing

### Use-case unit tests
`Ez.Handball.Tests/Application/UseCases/GetSquadConstraintsUseCaseTests.cs`,
mocking `ISquadConstraintsRepository`:

- Omitted `ruleSetVersion` resolves to version `1` (verify repo called with `1`).
- Explicit `ruleSetVersion` is forwarded to the repository.
- Repo returns `null` → `RuleSetNotFound`.
- Repo returns constraints → `Found` carrying the same `SquadConstraints`.

### Endpoint tests
`Ez.Handball.Tests/Api/Endpoints/SquadConstraintsEndpointTests.cs`, using
`WebApplicationFactory<Program>` with `IGetSquadConstraintsUseCase` replaced by a
Moq mock (same harness as `SquadEndpointTests` — no real storage touched):

- Happy path, default version → `200` with expected shape.
- Happy path, explicit `ruleSetVersion` → `200`; use case received that version.
- `flavor=manager` → `400 invalid_flavor`.
- Use case returns `RuleSetNotFound` → `400 invalid_rule_set`.
- Response-shape assertions: `ruleSetVersion`, `maxSquadSize`,
  `startingCap.amount` / `startingCap.currency`, `posLimits` map entries.
- No `Authorization` header is sent — confirms the endpoint is public.

## Acceptance criteria (from the issue)

- [x] Endpoint mapped (public, no `RequireAuthorization()`); DI wiring.
- [x] Use case reads the resolved constraints group via `ISquadConstraintsRepository`
      and shapes the response.
- [x] `flavor` / `ruleSetVersion` validation with `invalid_flavor` / `invalid_rule_set`
      error codes, consistent with #54.
- [x] `posLimits` returned as a string→int map keyed by the stored position codes
      (placeholder vocabulary; endpoint returns whatever is seeded).
- [x] Tests: happy path (default + explicit `ruleSetVersion`), `invalid_flavor`,
      `invalid_rule_set`, response shape.

## Out of scope (YAGNI)

- No separate `SquadConstraintsEndpoints.cs` extension file — it is a single public
  read, mapped inline like clubs/seasons. A grouped file is only justified if more
  public `/api/squad/*` endpoints appear later.
- No position-vocabulary validation — the position codes are placeholder and pending
  owner review; the endpoint returns whatever is seeded.
- No caching.

## Dependencies

- Reuses `ISquadConstraintsRepository` + `SeedSquadConstraints` data (from #53).
- Consumed by the Fantasy Squad Builder UI (Web#16): the gate for fully client-side
  pre-validated Buy buttons and the squad/position counters.
