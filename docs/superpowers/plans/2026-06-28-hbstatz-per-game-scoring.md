# HBStatz Per-Game Scoring Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the disposable HBStatz spike to scrape **per-player per-game** stat lines for two real games, apply the project's actual fantasy `ScoringRuleSet`, and print + capture each player's computed fantasy points.

**Architecture:** Adds to the existing standalone `tools/HbStatz.Spike` console app (same branch as PR #100). Per-game data is server-rendered (plain HTTP GET, no headless browser) in `test6b.php` (home) / `test7b.php` (away). Pure logic — parse-all-tables, build stat lines, score — lives in small testable classes covered against committed HTML fixtures. A thin `MatchReportClient` does the live fetch; `Program` gains a `score` mode.

**Tech Stack:** .NET 9 console app, AngleSharp (already referenced), xUnit (already wired). Reuses the existing `ParsedTable`, `StatsTableParser`, `TableSerializer`, `HbStatzClient`.

## Global Constraints

- **Disposable & isolated:** the spike projects are NOT in `Ez.Handball.sln`. Nothing wires into Azure Functions, blob storage, or Table Storage.
- **Branch:** all work on `docs/hbstatz-scraping-spike-spec` (the PR #100 branch).
- **No headless browser:** per-game data is server-rendered; plain `HttpClient` GET only.
- **Target games:** match IDs `12922` and `12919` (Olís deild karla).
- **Scoring values (verbatim, from `SeedScoringRuleSetsFunction.RuleSetDefinitions` `fantasy-v1`):** goal `+2`, yellow card `-1`, two-minute suspension `-2`, red card `-5`, appearance `+1`. Formula: `points = 2·goals − 1·yellow − 2·twoMin − 5·red + 1`.
- **net9.0:** keep the existing target framework. The installed SDK is .NET 10; if a build fails solely due to a missing net9.0 targeting pack, report it rather than changing the target.
- **Discovered facts (verified live 2026-06-28 on games 12924/12922/12919):**
  - A team page (`test6b`/`test7b`) has 5 `<table>` elements (each with a `<thead>`). Three carry scoring inputs; two (passing combinations, GK-vs-shooter matrix) are empty/irrelevant and have first header `Sendingar maður` / `Markmaður` (NOT `Nafn`).
  - **GK table**: headers contain `Nafn` and `Varin`; goals=`Mörk`, yellow=`Gul`, 2min=`2Mín`, red=`Rau` (all in one row). ~2 rows.
  - **Outfield offensive table**: headers contain `Nafn`, `Mörk`, `Skot`; no `Varin`, no `Gul`. Goals=`Mörk`. ~13–14 rows.
  - **Outfield discipline table**: headers contain `Nafn`, `Gul`; no `Mörk`, no `Varin`. Cards=`Gul`/`2Mín`/`Rau`. Same players as offensive (clean 1:1 join by the `Nafn` cell).
  - Player cell format: `"{jersey}. {name}"`, e.g. `"25. Ómar Darri Sigurgeirsson"`.
  - Real values (12922 home): `25. Ómar Darri Sigurgeirsson` → 11 goals, 0 yellow, 1 two-min, 0 red → **21 pts**. `20. Birkir Benediktsson` → 2 goals, 1 yellow → **4 pts**. GK `1. Daníel Freyr Andrésson` → 0/0/0/0 → **1 pt**.

---

### Task 1: Capture per-game HTML fixtures

**Files:**
- Create: `tools/HbStatz.Spike.Tests/fixtures/pergame/12922-home.html`
- Create: `tools/HbStatz.Spike.Tests/fixtures/pergame/12922-away.html`
- Create: `tools/HbStatz.Spike.Tests/fixtures/pergame/12919-home.html`
- Create: `tools/HbStatz.Spike.Tests/fixtures/pergame/12919-away.html`

**Interfaces:**
- Consumes: nothing.
- Produces: four committed team-page HTML fixtures (deterministic test inputs for Tasks 2–3). Each contains 5 server-rendered `<table>` elements; the GK / offensive / discipline tables carry the scoring inputs.

- [ ] **Step 1: Download the four fixtures**

```bash
mkdir -p tools/HbStatz.Spike.Tests/fixtures/pergame
UA="EzHandball-spike/0.1 (+https://github.com/kromby/Ez.Handball.Backend/issues/7)"
curl -s -A "$UA" "https://hbstatz.is/test6b.php?ID=12922" -o tools/HbStatz.Spike.Tests/fixtures/pergame/12922-home.html
curl -s -A "$UA" "https://hbstatz.is/test7b.php?ID=12922" -o tools/HbStatz.Spike.Tests/fixtures/pergame/12922-away.html
curl -s -A "$UA" "https://hbstatz.is/test6b.php?ID=12919" -o tools/HbStatz.Spike.Tests/fixtures/pergame/12919-home.html
curl -s -A "$UA" "https://hbstatz.is/test7b.php?ID=12919" -o tools/HbStatz.Spike.Tests/fixtures/pergame/12919-away.html
```

- [ ] **Step 2: Verify the fixtures have the expected shape**

```bash
for f in tools/HbStatz.Spike.Tests/fixtures/pergame/*.html; do
  echo "$f: tables=$(grep -c '<table' "$f") hasVarin=$(grep -c 'Varin' "$f") hasMork=$(grep -c 'M&ouml;rk\|Mörk' "$f")"
done
grep -c 'Ómar Darri\|&Oacute;mar Darri' tools/HbStatz.Spike.Tests/fixtures/pergame/12922-home.html  # expect >= 1
```

Expected: each file has `tables>=5`, contains `Varin` and a `Mörk` form. The 12922-home file contains "Ómar Darri".

> If any `curl` returns a tiny/error/challenge page instead of the tables (e.g. `tables` is 0), STOP and report BLOCKED with what came back — do not commit fabricated fixtures. (Not expected: plain GET worked during design.)

- [ ] **Step 3: Commit**

```bash
git add tools/HbStatz.Spike.Tests/fixtures/pergame
git commit -m "test: add per-game HBStatz team-page fixtures for scoring spike (#7)"
```

---

### Task 2: `PlayerStatLine` model + `StatsTableParser.ParseAll`

**Files:**
- Create: `tools/HbStatz.Spike/PlayerStatLine.cs`
- Modify: `tools/HbStatz.Spike/StatsTableParser.cs`
- Test: `tools/HbStatz.Spike.Tests/ParseAllTests.cs`

**Interfaces:**
- Consumes: existing `ParsedTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)` with `RowCount`; per-game fixtures from Task 1.
- Produces:
  - `PlayerStatLine` record (consumed by Tasks 3–5): `string Side, int? Jersey, string Name, bool IsGoalkeeper, int Goals, int YellowCards, int TwoMinuteSuspensions, int RedCards`.
  - `StatsTableParser.ParseAll(string html) -> IReadOnlyList<ParsedTable>` — every `<table>` that has a `<thead>`, in document order (per-game pages yield 5). The existing single-table `Parse` is unchanged.

- [ ] **Step 1: Create the `PlayerStatLine` record**

`tools/HbStatz.Spike/PlayerStatLine.cs`:

```csharp
namespace HbStatz.Spike;

public sealed record PlayerStatLine(
    string Side,
    int? Jersey,
    string Name,
    bool IsGoalkeeper,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
```

- [ ] **Step 2: Write the failing `ParseAll` test**

`tools/HbStatz.Spike.Tests/ParseAllTests.cs`:

```csharp
using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class ParseAllTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pergame", name));

    [Fact]
    public void ParseAll_TeamPage_ReturnsAllTheadTables()
    {
        var tables = StatsTableParser.ParseAll(Fixture("12922-home.html"));

        // 5 tables on a team page; the three data tables are non-empty
        Assert.Equal(5, tables.Count);

        // GK table: has Nafn + Varin
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
        // Outfield offensive: Nafn + Mörk + Skot, no Varin, no Gul
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk")
            && t.Columns.Contains("Skot") && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
        // Outfield discipline: Nafn + Gul, no Mörk, no Varin
        Assert.Contains(tables, t => t.Columns.Contains("Nafn") && t.Columns.Contains("Gul")
            && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter ParseAllTests`
Expected: FAIL — `StatsTableParser.ParseAll` does not exist (compile error).

- [ ] **Step 4: Add `ParseAll` to `StatsTableParser`**

Add this method to the existing `StatsTableParser` class in `tools/HbStatz.Spike/StatsTableParser.cs` (reuses the private `CellText`):

```csharp
    public static IReadOnlyList<ParsedTable> ParseAll(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        return doc.QuerySelectorAll("table")
            .Where(t => t.QuerySelector("thead") != null)
            .Select(table =>
            {
                var columns = table.QuerySelectorAll("thead th, thead td")
                    .Select(CellText)
                    .ToList();

                var rows = table.QuerySelectorAll("tbody tr")
                    .Select(tr => (IReadOnlyList<string>)tr.QuerySelectorAll("td").Select(CellText).ToList())
                    .Where(r => r.Count > 0)
                    .ToList();

                return new ParsedTable(columns, rows);
            })
            .ToList();
    }
```

- [ ] **Step 5: Run it to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter ParseAllTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/HbStatz.Spike/PlayerStatLine.cs tools/HbStatz.Spike/StatsTableParser.cs tools/HbStatz.Spike.Tests/ParseAllTests.cs
git commit -m "feat: add PlayerStatLine and StatsTableParser.ParseAll for per-game spike (#7)"
```

---

### Task 3: `MatchStatLineBuilder`

**Files:**
- Create: `tools/HbStatz.Spike/MatchStatLineBuilder.cs`
- Test: `tools/HbStatz.Spike.Tests/MatchStatLineBuilderTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<ParsedTable>` from `StatsTableParser.ParseAll`; `PlayerStatLine`.
- Produces: `MatchStatLineBuilder.Build(IReadOnlyList<ParsedTable> teamTables, string side) -> IReadOnlyList<PlayerStatLine>`. Classifies the GK / offensive / discipline tables by header signature, reads columns **by header name** (indices differ per table), joins offensive (goals) with discipline (cards) by the `Nafn` cell, emits one line per outfield player plus one per GK. Players are tagged with `side`.

- [ ] **Step 1: Write the failing builder test**

`tools/HbStatz.Spike.Tests/MatchStatLineBuilderTests.cs`:

```csharp
using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class MatchStatLineBuilderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pergame", name));

    private static IReadOnlyList<PlayerStatLine> HomeLines() =>
        MatchStatLineBuilder.Build(StatsTableParser.ParseAll(Fixture("12922-home.html")), "home");

    [Fact]
    public void Build_JoinsOutfieldGoalsAndCardsByPlayer()
    {
        var omar = HomeLines().Single(p => p.Name == "Ómar Darri Sigurgeirsson");

        Assert.Equal(25, omar.Jersey);
        Assert.False(omar.IsGoalkeeper);
        Assert.Equal(11, omar.Goals);                  // from offensive table
        Assert.Equal(0, omar.YellowCards);             // from discipline table
        Assert.Equal(1, omar.TwoMinuteSuspensions);    // from discipline table
        Assert.Equal(0, omar.RedCards);
        Assert.Equal("home", omar.Side);
    }

    [Fact]
    public void Build_EmitsGoalkeeperLinesFromGkTable()
    {
        var gk = HomeLines().Single(p => p.Name == "Daníel Freyr Andrésson");

        Assert.True(gk.IsGoalkeeper);
        Assert.Equal(1, gk.Jersey);
        Assert.Equal(0, gk.Goals);
        Assert.Equal(0, gk.YellowCards);
    }

    [Fact]
    public void Build_ReturnsAllPlayers_GkPlusOutfield()
    {
        // 12922 home: 2 GK + 13 outfield = 15 lines
        Assert.Equal(15, HomeLines().Count);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter MatchStatLineBuilderTests`
Expected: FAIL — `MatchStatLineBuilder` does not exist.

- [ ] **Step 3: Implement `MatchStatLineBuilder`**

`tools/HbStatz.Spike/MatchStatLineBuilder.cs`:

```csharp
namespace HbStatz.Spike;

public static class MatchStatLineBuilder
{
    public static IReadOnlyList<PlayerStatLine> Build(IReadOnlyList<ParsedTable> teamTables, string side)
    {
        var gkTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
        var offensiveTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk")
            && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
        var disciplineTable = teamTables.FirstOrDefault(t =>
            t.Columns.Contains("Nafn") && t.Columns.Contains("Gul")
            && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));

        var lines = new List<PlayerStatLine>();

        // Goalkeepers: all inputs in one row.
        if (gkTable is not null)
        {
            foreach (var row in gkTable.Rows)
            {
                var (jersey, name) = SplitPlayer(Cell(gkTable, row, "Nafn"));
                lines.Add(new PlayerStatLine(side, jersey, name, IsGoalkeeper: true,
                    Goals: Int(Cell(gkTable, row, "Mörk")),
                    YellowCards: Int(Cell(gkTable, row, "Gul")),
                    TwoMinuteSuspensions: Int(Cell(gkTable, row, "2Mín")),
                    RedCards: Int(Cell(gkTable, row, "Rau"))));
            }
        }

        // Outfield: goals from offensive, cards from discipline, joined by player cell.
        var cardsByPlayer = new Dictionary<string, IReadOnlyList<string>>();
        if (disciplineTable is not null)
            foreach (var row in disciplineTable.Rows)
                cardsByPlayer[Cell(disciplineTable, row, "Nafn")] = row;

        if (offensiveTable is not null)
        {
            foreach (var row in offensiveTable.Rows)
            {
                var key = Cell(offensiveTable, row, "Nafn");
                var (jersey, name) = SplitPlayer(key);
                var hasCards = cardsByPlayer.TryGetValue(key, out var cardRow);
                lines.Add(new PlayerStatLine(side, jersey, name, IsGoalkeeper: false,
                    Goals: Int(Cell(offensiveTable, row, "Mörk")),
                    YellowCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Gul")) : 0,
                    TwoMinuteSuspensions: hasCards ? Int(Cell(disciplineTable!, cardRow!, "2Mín")) : 0,
                    RedCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Rau")) : 0));
            }
        }

        return lines;
    }

    private static string Cell(ParsedTable table, IReadOnlyList<string> row, string column)
    {
        var idx = table.Columns.ToList().IndexOf(column);
        return idx >= 0 && idx < row.Count ? row[idx] : string.Empty;
    }

    private static int Int(string value) => int.TryParse(value.Trim(), out var n) ? n : 0;

    private static (int? Jersey, string Name) SplitPlayer(string cell)
    {
        var dot = cell.IndexOf('.');
        if (dot > 0 && int.TryParse(cell[..dot].Trim(), out var jersey))
            return (jersey, cell[(dot + 1)..].Trim());
        return (null, cell.Trim());
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter MatchStatLineBuilderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/HbStatz.Spike/MatchStatLineBuilder.cs tools/HbStatz.Spike.Tests/MatchStatLineBuilderTests.cs
git commit -m "feat: build per-game player stat lines from team tables (#7)"
```

---

### Task 4: `FantasyScorer`

**Files:**
- Create: `tools/HbStatz.Spike/FantasyScorer.cs`
- Test: `tools/HbStatz.Spike.Tests/FantasyScorerTests.cs`

**Interfaces:**
- Consumes: `PlayerStatLine`.
- Produces: `FantasyScorer.Score(PlayerStatLine line) -> double` applying the `fantasy-v1` values. Public point constants `GoalPoints, YellowCardPoints, TwoMinutePoints, RedCardPoints, AppearancePoints`.

- [ ] **Step 1: Write the failing scorer test**

`tools/HbStatz.Spike.Tests/FantasyScorerTests.cs`:

```csharp
using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class FantasyScorerTests
{
    private static PlayerStatLine Line(int goals, int yellow, int two, int red, bool gk = false) =>
        new("home", 1, "Test Player", gk, goals, yellow, two, red);

    [Fact]
    public void Score_GoalsPlusAppearance()
    {
        // 9 goals + appearance = 2*9 + 1 = 19
        Assert.Equal(19, FantasyScorer.Score(Line(9, 0, 0, 0)));
    }

    [Fact]
    public void Score_YellowAndTwoMinuteAreNegative()
    {
        // 0 goals, 1 yellow, 1 two-min, appeared = -1 - 2 + 1 = -2
        Assert.Equal(-2, FantasyScorer.Score(Line(0, 1, 1, 0)));
    }

    [Fact]
    public void Score_RealOutfieldExample_OmarDarri()
    {
        // 12922 home: 11 goals, 0 yellow, 1 two-min, 0 red = 2*11 - 2 + 1 = 21
        Assert.Equal(21, FantasyScorer.Score(Line(11, 0, 1, 0)));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter FantasyScorerTests`
Expected: FAIL — `FantasyScorer` does not exist.

- [ ] **Step 3: Implement `FantasyScorer`**

`tools/HbStatz.Spike/FantasyScorer.cs`:

```csharp
namespace HbStatz.Spike;

// Values mirror SeedScoringRuleSetsFunction.RuleSetDefinitions ("fantasy-v1").
public static class FantasyScorer
{
    public const double GoalPoints = 2;
    public const double YellowCardPoints = -1;
    public const double TwoMinutePoints = -2;
    public const double RedCardPoints = -5;
    public const double AppearancePoints = 1;

    public static double Score(PlayerStatLine line) =>
        line.Goals * GoalPoints
        + line.YellowCards * YellowCardPoints
        + line.TwoMinuteSuspensions * TwoMinutePoints
        + line.RedCards * RedCardPoints
        + AppearancePoints;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter FantasyScorerTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/HbStatz.Spike/FantasyScorer.cs tools/HbStatz.Spike.Tests/FantasyScorerTests.cs
git commit -m "feat: add FantasyScorer mirroring fantasy-v1 rule set (#7)"
```

---

### Task 5: `MatchReportClient` + `score` mode, run end-to-end, capture scored CSVs

**Files:**
- Modify: `tools/HbStatz.Spike/HbStatzClient.cs` (expose the fetch)
- Create: `tools/HbStatz.Spike/MatchReportClient.cs`
- Modify: `tools/HbStatz.Spike/Program.cs`
- Test: `tools/HbStatz.Spike.Tests/MatchReportClientTests.cs`
- Create (generated, then committed): `tools/HbStatz.Spike/output/scored-12922.csv`, `output/scored-12919.csv`

**Interfaces:**
- Consumes: `MatchStatLineBuilder`, `StatsTableParser.ParseAll`, `FantasyScorer`, `TableSerializer.ToCsv`, `HbStatzClient`.
- Produces:
  - `HbStatzClient.GetHtmlAsync(string url, CancellationToken ct = default) -> Task<string>` (the former private `GetAsync`, now public so the match client can reuse the User-Agent + status check).
  - `MatchReportClient.TeamPageUrl(string matchId, string side) -> string` (static, pure) and `MatchReportClient.GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) -> Task<string>`.
  - `Program` gains a `score [matchId...]` mode (defaults to `12922 12919`) that prints per-game scored tables and writes `output/scored-{matchId}.csv`. The existing no-arg season behavior is unchanged.

- [ ] **Step 1: Expose the fetch on `HbStatzClient`**

In `tools/HbStatz.Spike/HbStatzClient.cs`, rename the private `GetAsync` to a public `GetHtmlAsync` and update its internal caller. The full file becomes:

```csharp
namespace HbStatz.Spike;

public sealed class HbStatzClient(HttpClient http)
{
    private const string UserAgent =
        "EzHandball-spike/0.1 (+https://github.com/kromby/Ez.Handball.Backend/issues/7)";

    public async Task<IReadOnlyList<string>> GetTableHtmlsAsync(string pageUrl, CancellationToken ct = default)
    {
        var html = await GetHtmlAsync(pageUrl, ct);
        if (IframeResolver.HasInlineStatsTable(html))
            return new[] { html };

        var results = new List<string>();
        foreach (var src in IframeResolver.ExtractSources(html, pageUrl))
            results.Add(await GetHtmlAsync(src, ct));
        return results;
    }

    public async Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
```

- [ ] **Step 2: Write the failing `MatchReportClient` URL test**

`tools/HbStatz.Spike.Tests/MatchReportClientTests.cs`:

```csharp
using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class MatchReportClientTests
{
    [Fact]
    public void TeamPageUrl_HomeUsesTest6b_AwayUsesTest7b()
    {
        Assert.Equal("https://hbstatz.is/test6b.php?ID=12922", MatchReportClient.TeamPageUrl("12922", "home"));
        Assert.Equal("https://hbstatz.is/test7b.php?ID=12922", MatchReportClient.TeamPageUrl("12922", "away"));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter MatchReportClientTests`
Expected: FAIL — `MatchReportClient` does not exist.

- [ ] **Step 4: Implement `MatchReportClient`**

`tools/HbStatz.Spike/MatchReportClient.cs`:

```csharp
namespace HbStatz.Spike;

public sealed class MatchReportClient(HbStatzClient client)
{
    public static string TeamPageUrl(string matchId, string side)
    {
        var page = side == "home" ? "test6b" : "test7b";
        return $"https://hbstatz.is/{page}.php?ID={matchId}";
    }

    public Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) =>
        client.GetHtmlAsync(TeamPageUrl(matchId, side), ct);
}
```

- [ ] **Step 5: Run it to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter MatchReportClientTests`
Expected: PASS.

- [ ] **Step 6: Add the `score` mode to `Program`**

Replace `tools/HbStatz.Spike/Program.cs` with (keeps the season mode as the no-arg default, adds `score`):

```csharp
namespace HbStatz.Spike;

public static class Program
{
    private record Target(string Name, string Url);

    private static readonly Target[] Targets =
    {
        new("outfield",    "https://hbstatz.is/OlisDeildKarlaTolfraedi.php"),
        new("goalkeepers", "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php"),
    };

    private static readonly string[] DefaultGames = { "12922", "12919" };

    public static async Task<int> Main(string[] args)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        Directory.CreateDirectory(outputDir);

        using var http = new HttpClient();

        if (args.Length > 0 && args[0] == "score")
        {
            var games = args.Length > 1 ? args[1..] : DefaultGames;
            await ScoreGamesAsync(new MatchReportClient(new HbStatzClient(http)), games, outputDir);
            return 0;
        }

        await ScrapeSeasonAsync(new HbStatzClient(http), outputDir);
        return 0;
    }

    private static async Task ScoreGamesAsync(MatchReportClient client, string[] games, string outputDir)
    {
        foreach (var matchId in games)
        {
            var lines = new List<PlayerStatLine>();
            foreach (var side in new[] { "home", "away" })
            {
                var html = await client.GetTeamPageHtmlAsync(matchId, side);
                lines.AddRange(MatchStatLineBuilder.Build(StatsTableParser.ParseAll(html), side));
            }

            var scored = lines
                .Select(p => (Player: p, Points: FantasyScorer.Score(p)))
                .OrderByDescending(x => x.Points)
                .ToList();

            Console.WriteLine($"=== Game {matchId} — {scored.Count} players ===");
            Console.WriteLine($"{"Player",-28} {"Side",-4} {"G",2} {"Y",2} {"2m",2} {"R",2} {"Pts",5}");
            foreach (var (p, pts) in scored)
                Console.WriteLine($"{p.Name,-28} {p.Side,-4} {p.Goals,2} {p.YellowCards,2} {p.TwoMinuteSuspensions,2} {p.RedCards,2} {pts,5}");

            var table = new ParsedTable(
                new[] { "side", "jersey", "name", "goalkeeper", "goals", "yellow", "twoMin", "red", "points" },
                scored.Select(x => (IReadOnlyList<string>)new[]
                {
                    x.Player.Side,
                    x.Player.Jersey?.ToString() ?? "",
                    x.Player.Name,
                    x.Player.IsGoalkeeper ? "true" : "false",
                    x.Player.Goals.ToString(),
                    x.Player.YellowCards.ToString(),
                    x.Player.TwoMinuteSuspensions.ToString(),
                    x.Player.RedCards.ToString(),
                    x.Points.ToString(),
                }).ToList());

            await File.WriteAllTextAsync(Path.Combine(outputDir, $"scored-{matchId}.csv"), TableSerializer.ToCsv(table));
        }

        Console.WriteLine($"Scored CSVs written to {Path.GetFullPath(outputDir)}");
    }

    private static async Task ScrapeSeasonAsync(HbStatzClient client, string outputDir)
    {
        foreach (var target in Targets)
        {
            var htmls = await client.GetTableHtmlsAsync(target.Url);
            ParsedTable? table = null;
            foreach (var html in htmls)
            {
                try { var t = StatsTableParser.Parse(html); if (t.RowCount > 0) { table = t; break; } }
                catch (InvalidOperationException) { /* not the table-bearing fragment */ }
            }

            if (table is null)
            {
                Console.WriteLine($"[{target.Name}] NO TABLE FOUND at {target.Url}");
                continue;
            }

            await File.WriteAllTextAsync(Path.Combine(outputDir, $"{target.Name}.json"), TableSerializer.ToJson(table));
            await File.WriteAllTextAsync(Path.Combine(outputDir, $"{target.Name}.csv"),  TableSerializer.ToCsv(table));

            Console.WriteLine($"[{target.Name}] {table.RowCount} rows, {table.Columns.Count} columns");
            Console.WriteLine($"  columns: {string.Join(", ", table.Columns)}");
            Console.WriteLine($"  row[0] : {string.Join(" | ", table.Rows[0])}");
        }

        Console.WriteLine($"Output written to {Path.GetFullPath(outputDir)}");
    }
}
```

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj`
Expected: PASS (all tests — the 6 prior + the new ParseAll/builder/scorer/client tests).

- [ ] **Step 8: Run the scoring mode end-to-end**

Run: `dotnet run --project tools/HbStatz.Spike -- score`
Expected: prints `=== Game 12922 … ===` and `=== Game 12919 … ===` tables sorted by points; `Ómar Darri Sigurgeirsson` shows `21` points in game 12922; writes `output/scored-12922.csv` and `output/scored-12919.csv`.

> If a fetch throws on `EnsureSuccessStatusCode` or a game prints 0 players, the live path is blocked — report it; do not hand-write CSVs.

- [ ] **Step 9: Sanity-check the captured output**

Run: `head -5 tools/HbStatz.Spike/output/scored-12922.csv`
Expected: header `side,jersey,name,goalkeeper,goals,yellow,twoMin,red,points` followed by the highest-scoring players.

- [ ] **Step 10: Commit code and captured evidence**

```bash
git add tools/HbStatz.Spike/HbStatzClient.cs tools/HbStatz.Spike/MatchReportClient.cs tools/HbStatz.Spike/Program.cs tools/HbStatz.Spike.Tests/MatchReportClientTests.cs tools/HbStatz.Spike/output/scored-12922.csv tools/HbStatz.Spike/output/scored-12919.csv
git commit -m "feat: score per-game HBStatz stat lines and capture results (#7)"
```

---

## Self-Review

**Spec coverage:**
- Per-game source `test6b`/`test7b`, plain GET, no headless → Task 5 (`MatchReportClient`), Global Constraints. ✓
- Parse all 5 tables → Task 2 (`ParseAll`). ✓
- Classify GK/offensive/discipline by header signature; columns by name → Task 3. ✓
- Join offensive (goals) + discipline (cards) by player; GK single row → Task 3. ✓
- Player identity `jersey. name` parsed → Task 3 (`SplitPlayer`). ✓
- Scoring values goal +2 / yellow −1 / 2m −2 / red −5 / appearance +1 → Task 4 (`FantasyScorer`). ✓
- Print per-game scored table + write `scored-{id}.csv` for 12922 & 12919 → Task 5. ✓
- Fixtures committed; logic TDD-covered → Tasks 1–4. ✓
- Disposable, not in sln, net9.0 → Global Constraints. ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases" — every code step has complete code. ✓

**Type consistency:** `PlayerStatLine(Side, Jersey, Name, IsGoalkeeper, Goals, YellowCards, TwoMinuteSuspensions, RedCards)` defined in Task 2 and used identically in Tasks 3–5. `StatsTableParser.ParseAll` (Task 2) consumed in Tasks 3 & 5. `MatchStatLineBuilder.Build(tables, side)` (Task 3) called in Task 5. `FantasyScorer.Score(line)` (Task 4) called in Task 5. `HbStatzClient.GetHtmlAsync` (Task 5 Step 1) consumed by `MatchReportClient` (Task 5 Step 4). `TableSerializer.ToCsv(ParsedTable)` reused from the existing spike. ✓
