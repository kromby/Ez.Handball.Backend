# Public Squad-Constraints Read Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a public `GET /api/squad/constraints` endpoint that returns the fantasy squad rule-set constraints (max squad size, starting cap, per-position limits) straight from `ISquadConstraintsRepository`.

**Architecture:** A thin `GetSquadConstraintsUseCase` (mirroring `GetSquadUseCase` #54) resolves the rule-set version and returns a `RuleSetNotFound`/`Found` union. The endpoint is mapped inline in `Program.cs` as a public read (no `RequireAuthorization()`), alongside `/api/clubs` and `/api/seasons`. The fantasy-only `flavor` check lives at the edge, mirroring `SquadEndpoints`. The response reuses `PlayerCost` for the `startingCap` money shape and passes `PositionLimits` through unchanged.

**Tech Stack:** C# / .NET (minimal-API Azure-hosted), xUnit + Moq, `WebApplicationFactory<Program>` for endpoint tests.

**Spec:** `docs/superpowers/specs/2026-06-07-squad-constraints-endpoint-design.md`

---

## File Structure

- **Create** `Ez.Handball.Application/UseCases/GetSquadConstraintsUseCase.cs` — interface, result union, and use case. One responsibility: resolve the rule-set version and read the constraints group.
- **Create** `Ez.Handball.Tests/Application/UseCases/GetSquadConstraintsUseCaseTests.cs` — unit tests for the use case (mock `ISquadConstraintsRepository`).
- **Create** `Ez.Handball.Tests/Api/Endpoints/SquadConstraintsEndpointTests.cs` — endpoint tests (`WebApplicationFactory` with the use case mocked).
- **Modify** `Ez.Handball.Api/Program.cs` — register the use case in DI and map the endpoint inline.

No changes to `ISquadConstraintsRepository`, `SquadConstraints`, or infrastructure — the repository is already registered (consumed by #53/#54).

---

## Task 1: `GetSquadConstraintsUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetSquadConstraintsUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetSquadConstraintsUseCaseTests.cs`

- [ ] **Step 1: Write the failing unit tests**

Create `Ez.Handball.Tests/Application/UseCases/GetSquadConstraintsUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetSquadConstraintsUseCaseTests
{
    private readonly Mock<ISquadConstraintsRepository> _repo = new();

    private GetSquadConstraintsUseCase CreateSut() => new(_repo.Object);

    private static SquadConstraints AnyConstraints(int version) => new(
        version,
        MaxSquadSize: 15,
        PositionLimits: new Dictionary<string, int> { ["GK"] = 2, ["P"] = 2 },
        StartingCap: 100_000_000,
        Currency: "ISK");

    [Fact]
    public async Task OmittedVersion_ResolvesToOne()
    {
        _repo.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(AnyConstraints(1));

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        var found = Assert.IsType<GetSquadConstraintsResult.Found>(result);
        Assert.Equal(1, found.Constraints.Version);
        _repo.Verify(r => r.GetAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExplicitVersion_IsForwarded()
    {
        _repo.Setup(r => r.GetAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(AnyConstraints(3));

        var result = await CreateSut().ExecuteAsync(3, CancellationToken.None);

        Assert.IsType<GetSquadConstraintsResult.Found>(result);
        _repo.Verify(r => r.GetAsync(3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepoReturnsNull_ReturnsRuleSetNotFound()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SquadConstraints?)null);

        var result = await CreateSut().ExecuteAsync(9, CancellationToken.None);

        Assert.IsType<GetSquadConstraintsResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task RepoReturnsConstraints_ReturnsFoundWithSameData()
    {
        var constraints = AnyConstraints(1);
        _repo.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(constraints);

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        var found = Assert.IsType<GetSquadConstraintsResult.Found>(result);
        Assert.Same(constraints, found.Constraints);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.sln --filter "FullyQualifiedName~GetSquadConstraintsUseCaseTests"`
Expected: BUILD FAILURE — `GetSquadConstraintsUseCase` / `GetSquadConstraintsResult` / `IGetSquadConstraintsUseCase` do not exist yet.

- [ ] **Step 3: Write the use case**

Create `Ez.Handball.Application/UseCases/GetSquadConstraintsUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetSquadConstraintsResult
{
    public sealed record RuleSetNotFound : GetSquadConstraintsResult
    {
        public static readonly RuleSetNotFound Instance = new();
    }
    public sealed record Found(SquadConstraints Constraints) : GetSquadConstraintsResult;
}

public interface IGetSquadConstraintsUseCase
{
    Task<GetSquadConstraintsResult> ExecuteAsync(int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetSquadConstraintsUseCase : IGetSquadConstraintsUseCase
{
    private const int DefaultVersion = 1;

    private readonly ISquadConstraintsRepository _repo;

    public GetSquadConstraintsUseCase(ISquadConstraintsRepository repo) => _repo = repo;

    public async Task<GetSquadConstraintsResult> ExecuteAsync(int? ruleSetVersion, CancellationToken ct)
    {
        var version = ruleSetVersion ?? DefaultVersion;
        var constraints = await _repo.GetAsync(version, ct);
        return constraints is null
            ? GetSquadConstraintsResult.RuleSetNotFound.Instance
            : new GetSquadConstraintsResult.Found(constraints);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.sln --filter "FullyQualifiedName~GetSquadConstraintsUseCaseTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetSquadConstraintsUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/GetSquadConstraintsUseCaseTests.cs
git commit -m "feat: GetSquadConstraintsUseCase resolving rule-set version (#66)"
```

---

## Task 2: Map the public endpoint + DI wiring

**Files:**
- Modify: `Ez.Handball.Api/Program.cs` (DI registration block; endpoint map block near `/api/clubs`)
- Test: `Ez.Handball.Tests/Api/Endpoints/SquadConstraintsEndpointTests.cs`

- [ ] **Step 1: Write the failing endpoint tests**

Create `Ez.Handball.Tests/Api/Endpoints/SquadConstraintsEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class SquadConstraintsEndpointTests : IClassFixture<SquadConstraintsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetSquadConstraintsUseCase> Uc { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
            // AddAuthInfrastructure reads Jwt:SigningKey eagerly at host build, before the
            // in-memory ConfigureAppConfiguration below is layered — so the key must come
            // from an env var or the host throws and the test can't run in isolation.
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-32-bytes-min!!");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                    ["Jwt:Issuer"] = "ez-handball",
                    ["Jwt:Audience"] = "ez-handball-web",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Auth:RateLimit:PermitLimit"] = "1000",
                    ["Auth:RateLimit:SensitivePermitLimit"] = "1000"
                }));
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetSquadConstraintsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public SquadConstraintsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = factory.CreateClient();
    }

    private static SquadConstraints Sample(int version) => new(
        version,
        MaxSquadSize: 15,
        PositionLimits: new Dictionary<string, int> { ["GK"] = 2, ["P"] = 2 },
        StartingCap: 100_000_000,
        Currency: "ISK");

    [Fact]
    public async Task Default_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync((int?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(1)));

        var resp = await _client.GetAsync("/api/squad/constraints?flavor=fantasy");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("ruleSetVersion").GetInt32());
        Assert.Equal(15, body.GetProperty("maxSquadSize").GetInt32());
        Assert.Equal(100_000_000, body.GetProperty("startingCap").GetProperty("amount").GetDouble());
        Assert.Equal("ISK", body.GetProperty("startingCap").GetProperty("currency").GetString());
        Assert.Equal(2, body.GetProperty("posLimits").GetProperty("GK").GetInt32());
    }

    [Fact]
    public async Task ExplicitVersion_ForwardedToUseCase()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(2)));

        var resp = await _client.GetAsync("/api/squad/constraints?ruleSetVersion=2");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidFlavor_Returns400()
    {
        var resp = await _client.GetAsync("/api/squad/constraints?flavor=manager");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RuleSetNotFound_Returns400InvalidRuleSet()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadConstraintsResult.RuleSetNotFound.Instance);

        var resp = await _client.GetAsync("/api/squad/constraints?ruleSetVersion=99");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NoTokenAndNoFlavor_Returns200_EndpointIsPublic()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(1)));

        var resp = await _client.GetAsync("/api/squad/constraints");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.sln --filter "FullyQualifiedName~SquadConstraintsEndpointTests"`
Expected: FAIL — the `services.Single(... IGetSquadConstraintsUseCase)` lookup throws (not registered) and/or the route returns `404`.

- [ ] **Step 3: Register the use case in DI**

In `Ez.Handball.Api/Program.cs`, in the block of `AddScoped` use-case registrations, immediately after the existing line:

```csharp
builder.Services.AddScoped<IGetSquadUseCase, GetSquadUseCase>();
```

add:

```csharp
builder.Services.AddScoped<IGetSquadConstraintsUseCase, GetSquadConstraintsUseCase>();
```

- [ ] **Step 4: Map the endpoint**

In `Ez.Handball.Api/Program.cs`, immediately after the existing `app.MapGet("/api/seasons", ...)` block, add:

```csharp
app.MapGet("/api/squad/constraints", async Task<IResult> (
    string? flavor,
    int? ruleSetVersion,
    IGetSquadConstraintsUseCase uc,
    CancellationToken ct) =>
{
    // Fantasy-only: blank or "fantasy" is accepted; anything else is rejected.
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

Note: `Ez.Handball.Domain` (for `PlayerCost`) and `Ez.Handball.Application.UseCases` are already imported at the top of `Program.cs`, so no new `using` is needed.

- [ ] **Step 5: Run the endpoint tests to verify they pass**

Run: `dotnet test Ez.Handball.sln --filter "FullyQualifiedName~SquadConstraintsEndpointTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Ez.Handball.sln`
Expected: PASS — all tests green (the prior baseline plus the 9 new tests).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Api/Program.cs \
        Ez.Handball.Tests/Api/Endpoints/SquadConstraintsEndpointTests.cs
git commit -m "feat: public GET /api/squad/constraints endpoint (#66)"
```

---

## Verification Checklist (maps to issue acceptance criteria)

- Endpoint mapped public (no `RequireAuthorization()`), DI wired — Task 2 Steps 3-4; `NoTokenAndNoFlavor_Returns200` test.
- Use case reads the resolved constraints group via `ISquadConstraintsRepository` and shapes the response — Task 1; Task 2 Step 4.
- `flavor` / `ruleSetVersion` validation with `invalid_flavor` / `invalid_rule_set` — `InvalidFlavor_Returns400`, `RuleSetNotFound_Returns400InvalidRuleSet`.
- `posLimits` returned as a string→int map keyed by stored position codes — `Default_Returns200WithExpectedShape` asserts `posLimits.GK`.
- Tests: happy path (default + explicit `ruleSetVersion`), `invalid_flavor`, `invalid_rule_set`, response shape — covered across both test files.
