# HBStatz Per-Game Database Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scrape per-game player statistics from HBStatz, parse the HTML, map players to their HSÍ database IDs, and merge advanced metrics into the `PlayerStats` table.

**Architecture:** A queue-driven decoupled pipeline. The `ParseMatchFunction` enqueues finished matches. `FetchHbStatzMatchStatsFunction` scrapes the home/away HTML reports and saves them to blob storage. `ParseHbStatzMatchStatsFunction` triggers on the new blobs, parses stats, reconciles player names against the team roster, and merges properties into the `PlayerStats` table.

**Tech Stack:** C# .NET 9.0, Azure Functions (Worker/Isolated), Azure Table Storage, Azure Blob Storage, Azure Storage Queues (via `Azure.Storage.Queues` client library), AngleSharp.

## Global Constraints

- Preserve all existing domain comments and docstrings.
- Maintain idempotency (all parsing and table writes must support replayability).
- Use `TableUpdateMode.Merge` when updating `PlayerStats` to avoid overwriting HSI core statistics.

---

### Task 1: Update Entities & Models

**Files:**
- Modify: `Ez.Handball.Shared/Entities/TournamentEntity.cs`
- Modify: `Ez.Handball.Shared/Entities/PlayerStatEntity.cs`
- Modify: `Ez.Handball.Domain/PlayerStat.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerStatsRepository.cs`
- Modify: `Ez.Handball.Ingestion/Functions/SeedTournamentsFunction.cs`
- Modify: `Ez.Handball.Tests/Ingestion/Functions/SeedTournamentsFunctionTests.cs`
- Modify: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerStatsRepositoryTests.cs`

**Interfaces:**
- Consumes: None (base tables)
- Produces: Nullable advanced metrics properties on `PlayerStatEntity` and `PlayerStat` domain record.

- [ ] **Step 1: Write the failing test**
  Add a new test inside `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerStatsRepositoryTests.cs` asserting that `GetByPlayerAsync` maps the new advanced metrics from `PlayerStatEntity` to `PlayerStat`.
  ```csharp
  [Fact]
  public async Task GetByPlayerAsync_MapsAdvancedMetrics()
  {
      SetupStats("12345", new PlayerStatEntity
      {
          PartitionKey = "match-1",
          RowKey = "12345",
          Goals = 5,
          TournamentId = "8444", Season = "2025-26", TeamId = "385-karlar",
          Assists = 3, Turnovers = 2, Steals = 1, PlusMinus = 1.5, Saves = 12, SaveRate = 34.5
      });
      SetupTournaments("2025-26", new TournamentEntity
      {
          PartitionKey = "2025-26", RowKey = "8444", Name = "Olís deild karla"
      });

      var result = await CreateSut().GetByPlayerAsync("12345", default);

      var only = Assert.Single(result);
      Assert.Equal(3, only.Assists);
      Assert.Equal(2, only.Turnovers);
      Assert.Equal(1, only.Steals);
      Assert.Equal(1.5, only.PlusMinus);
      Assert.Equal(12, only.Saves);
      Assert.Equal(34.5, only.SaveRate);
  }
  ```

- [ ] **Step 2: Run test to verify it fails**
  Run: `dotnet test --filter "TablePlayerStatsRepositoryTests"`
  Expected: Compile failure (properties do not exist on `PlayerStat` or `PlayerStatEntity`).

- [ ] **Step 3: Implement minimal code changes**
  - Add `IngestHbStatz` (bool) property to `TournamentEntity`. Update `SeedTournamentsFunction` and its unit tests (by expanding the definition tuple and test assert checks) to seed it as `true` for Olís deild karla (`"8444"`) and kvenna (`"8434"`), and `false` for others.
  - Add properties to `PlayerStatEntity`:
    ```csharp
    public int? Assists { get; set; }
    public int? Turnovers { get; set; }
    public int? Steals { get; set; }
    public double? PlusMinus { get; set; }
    public int? Shots { get; set; }
    public int? Blocks { get; set; }
    public double? Stops { get; set; }
    public double? ExpectedGoals { get; set; }
    public int? PenaltiesEarned { get; set; }
    public int? PenaltyGoals { get; set; }
    public int? Saves { get; set; }
    public double? SaveRate { get; set; }
    public double? ExpectedSaves { get; set; }
    public double? GoalsAgainst { get; set; }
    public int? PenaltySaves { get; set; }
    ```
  - Add matching nullable properties (defaulted to `null`) to `PlayerStat` domain record in `Ez.Handball.Domain/PlayerStat.cs`.
  - Update `TablePlayerStatsRepository.cs` to map all new properties inside both `GetByPlayerAsync` and `GetByMatchAsync` mapping loops:
    ```csharp
    Assists: s.Assists,
    Turnovers: s.Turnovers,
    Steals: s.Steals,
    PlusMinus: s.PlusMinus,
    Shots: s.Shots,
    Blocks: s.Blocks,
    Stops: s.Stops,
    ExpectedGoals: s.ExpectedGoals,
    PenaltiesEarned: s.PenaltiesEarned,
    PenaltyGoals: s.PenaltyGoals,
    Saves: s.Saves,
    SaveRate: s.SaveRate,
    ExpectedSaves: s.ExpectedSaves,
    GoalsAgainst: s.GoalsAgainst,
    PenaltySaves: s.PenaltySaves
    ```

- [ ] **Step 4: Run test to verify it passes**
  Run: `dotnet test --filter "TablePlayerStatsRepositoryTests"`
  Expected: PASS. Also run all tests: `dotnet test`.

- [ ] **Step 5: Commit**
  ```bash
  git add Ez.Handball.Shared/Entities/TournamentEntity.cs Ez.Handball.Shared/Entities/PlayerStatEntity.cs Ez.Handball.Domain/PlayerStat.cs Ez.Handball.Infrastructure/TableAccess/TablePlayerStatsRepository.cs Ez.Handball.Ingestion/Functions/SeedTournamentsFunction.cs Ez.Handball.Tests/
  git commit -m "feat: extend entities and repository for HBStatz metrics"
  ```

---

### Task 2: Migrate Scraper Clients

**Files:**
- Create: `Ez.Handball.Ingestion/Services/IHbStatzClient.cs`
- Create: `Ez.Handball.Ingestion/Services/HbStatzClient.cs`
- Create: `Ez.Handball.Ingestion/Services/IMatchReportClient.cs`
- Create: `Ez.Handball.Ingestion/Services/MatchReportClient.cs`

**Interfaces:**
- Consumes: `HttpClient`
- Produces: `IHbStatzClient` (fetches raw HTML), `IMatchReportClient` (resolves and scrapes per-side match team report HTML pages).

- [ ] **Step 1: Create client interface definitions**
  Define `IHbStatzClient` and `IMatchReportClient` inside `Ez.Handball.Ingestion/Services/`:
  ```csharp
  namespace Ez.Handball.Ingestion.Services;

  public interface IHbStatzClient
  {
      Task<string> GetHtmlAsync(string url, CancellationToken ct = default);
  }

  public interface IMatchReportClient
  {
      Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default);
  }
  ```

- [ ] **Step 2: Implement Client classes**
  Migrate client logic from `HbStatz.Spike` into `Ez.Handball.Ingestion/Services/`:
  `HbStatzClient.cs`:
  ```csharp
  namespace Ez.Handball.Ingestion.Services;

  public sealed class HbStatzClient : IHbStatzClient
  {
      private readonly HttpClient _http;

      public HbStatzClient(HttpClient http)
      {
          _http = http;
      }

      public async Task<string> GetHtmlAsync(string url, CancellationToken ct = default)
      {
          using var req = new HttpRequestMessage(HttpMethod.Get, url);
          var resp = await _http.SendAsync(req, ct);
          resp.EnsureSuccessStatusCode();
          return await resp.Content.ReadAsStringAsync(ct);
      }
  }
  ```
  `MatchReportClient.cs`:
  ```csharp
  namespace Ez.Handball.Ingestion.Services;

  public sealed class MatchReportClient : IMatchReportClient
  {
      private readonly IHbStatzClient _client;

      public MatchReportClient(IHbStatzClient client)
      {
          _client = client;
      }

      public static string TeamPageUrl(string matchId, string side)
      {
          var page = side == "home" ? "test6b" : "test7b";
          return $"https://hbstatz.is/{page}.php?ID={matchId}";
      }

      public Task<string> GetTeamPageHtmlAsync(string matchId, string side, CancellationToken ct = default) =>
          _client.GetHtmlAsync(TeamPageUrl(matchId, side), ct);
  }
  ```

- [ ] **Step 3: Create tests for MatchReportClient**
  Create unit test `Ez.Handball.Tests/Ingestion/Services/MatchReportClientTests.cs`:
  ```csharp
  using Ez.Handball.Ingestion.Services;
  using Moq;
  using Xunit;

  namespace Ez.Handball.Tests.Ingestion.Services;

  public class MatchReportClientTests
  {
      [Fact]
      public async Task GetTeamPageHtmlAsync_CallsExpectedUrls()
      {
          var mockClient = new Mock<IHbStatzClient>();
          mockClient.Setup(c => c.GetHtmlAsync(It.IsAny<string>(), default))
                    .ReturnsAsync("<html></html>");

          var sut = new MatchReportClient(mockClient.Object);
          await sut.GetTeamPageHtmlAsync("12922", "home");
          await sut.GetTeamPageHtmlAsync("12922", "away");

          mockClient.Verify(c => c.GetHtmlAsync("https://hbstatz.is/test6b.php?ID=12922", default), Times.Once);
          mockClient.Verify(c => c.GetHtmlAsync("https://hbstatz.is/test7b.php?ID=12922", default), Times.Once);
      }
  }
  ```

- [ ] **Step 4: Run tests to verify**
  Run: `dotnet test --filter "MatchReportClientTests"`
  Expected: PASS

- [ ] **Step 5: Commit**
  ```bash
  git add Ez.Handball.Ingestion/Services/IHbStatzClient.cs Ez.Handball.Ingestion/Services/HbStatzClient.cs Ez.Handball.Ingestion/Services/IMatchReportClient.cs Ez.Handball.Ingestion/Services/MatchReportClient.cs Ez.Handball.Tests/Ingestion/Services/MatchReportClientTests.cs
  git commit -m "feat: migrate and adapt scraping clients from spike"
  ```

---

### Task 3: Migrate & Extend Parsers

**Files:**
- Create: `Ez.Handball.Ingestion/Models/ParsedTable.cs`
- Create: `Ez.Handball.Ingestion/Models/PlayerStatLine.cs`
- Create: `Ez.Handball.Ingestion/Parsing/StatsTableParser.cs`
- Create: `Ez.Handball.Ingestion/Parsing/MatchStatLineBuilder.cs`
- Create: `Ez.Handball.Tests/Ingestion/Parsing/MatchStatLineBuilderTests.cs`

**Interfaces:**
- Consumes: HTML content string
- Produces: `IReadOnlyList<PlayerStatLine>` containing all parsed advanced metrics.

- [ ] **Step 1: Add model records**
  Create `Ez.Handball.Ingestion/Models/ParsedTable.cs`:
  ```csharp
  namespace Ez.Handball.Ingestion.Models;

  public sealed record ParsedTable(
      IReadOnlyList<string> Columns,
      IReadOnlyList<IReadOnlyList<string>> Rows)
  {
      public int RowCount => Rows.Count;
  }
  ```
  Create `Ez.Handball.Ingestion/Models/PlayerStatLine.cs` matching the full properties signature of our data model:
  ```csharp
  namespace Ez.Handball.Ingestion.Models;

  public sealed record PlayerStatLine(
      string Side,
      int? Jersey,
      string Name,
      bool IsGoalkeeper,
      int Goals,
      int YellowCards,
      int TwoMinuteSuspensions,
      int RedCards,
      int? Assists = null,
      int? Turnovers = null,
      int? Steals = null,
      double? PlusMinus = null,
      int? Shots = null,
      int? Blocks = null,
      double? Stops = null,
      double? ExpectedGoals = null,
      int? PenaltiesEarned = null,
      int? PenaltyGoals = null,
      int? Saves = null,
      double? SaveRate = null,
      double? ExpectedSaves = null,
      double? GoalsAgainst = null,
      int? PenaltySaves = null
  );
  ```

- [ ] **Step 2: Implement StatsTableParser**
  Create `Ez.Handball.Ingestion/Parsing/StatsTableParser.cs` using AngleSharp to extract headers and values:
  ```csharp
  using AngleSharp.Dom;
  using AngleSharp.Html.Parser;
  using Ez.Handball.Ingestion.Models;

  namespace Ez.Handball.Ingestion.Parsing;

  public static class StatsTableParser
  {
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

      private static string CellText(IElement cell)
      {
          var text = cell.TextContent.Trim();
          if (!string.IsNullOrEmpty(text)) return text;

          var img = cell.QuerySelector("img");
          var src = img?.GetAttribute("src");
          if (string.IsNullOrEmpty(src)) return string.Empty;

          var file = src.Split('/').Last();
          var dot = file.LastIndexOf('.');
          return dot > 0 ? file[..dot] : file;
      }
  }
  ```

- [ ] **Step 3: Implement MatchStatLineBuilder**
  Create `Ez.Handball.Ingestion/Parsing/MatchStatLineBuilder.cs`. It must extract the advanced metrics columns from the HTML tables:
  ```csharp
  using Ez.Handball.Ingestion.Models;

  namespace Ez.Handball.Ingestion.Parsing;

  public static class MatchStatLineBuilder
  {
      public static IReadOnlyList<PlayerStatLine> Build(IReadOnlyList<ParsedTable> teamTables, string side)
      {
          var gkTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Varin"));
          var offensiveTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin") && !t.Columns.Contains("Gul"));
          var disciplineTable = teamTables.FirstOrDefault(t => t.Columns.Contains("Nafn") && t.Columns.Contains("Gul") && !t.Columns.Contains("Mörk") && !t.Columns.Contains("Varin"));

          var lines = new List<PlayerStatLine>();

          if (gkTable is not null)
          {
              foreach (var row in gkTable.Rows)
              {
                  var (jersey, name) = SplitPlayer(Cell(gkTable, row, "Nafn"));
                  lines.Add(new PlayerStatLine(
                      Side: side, Jersey: jersey, Name: name, IsGoalkeeper: true,
                      Goals: Int(Cell(gkTable, row, "Mörk")),
                      YellowCards: Int(Cell(gkTable, row, "Gul")),
                      TwoMinuteSuspensions: Int(Cell(gkTable, row, "2Mín")),
                      RedCards: Int(Cell(gkTable, row, "Rau")),
                      Assists: NullInt(Cell(gkTable, row, "Sköpuð færi (Stoð)")),
                      Turnovers: NullInt(Cell(gkTable, row, "TB (TB/ S.Brot)")),
                      Steals: NullInt(Cell(gkTable, row, "Stl")),
                      PlusMinus: Double(Cell(gkTable, row, "+/-")),
                      Saves: NullInt(Cell(gkTable, row, "Varin")),
                      SaveRate: Double(Cell(gkTable, row, "%")),
                      ExpectedSaves: Double(Cell(gkTable, row, "xS")),
                      GoalsAgainst: Double(Cell(gkTable, row, "Mörk á")),
                      PenaltySaves: NullInt(Cell(gkTable, row, "Víti Varin"))
                  ));
              }
          }

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

                  lines.Add(new PlayerStatLine(
                      Side: side, Jersey: jersey, Name: name, IsGoalkeeper: false,
                      Goals: Int(Cell(offensiveTable, row, "Mörk")),
                      YellowCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Gul")) : 0,
                      TwoMinuteSuspensions: hasCards ? Int(Cell(disciplineTable!, cardRow!, "2Mín")) : 0,
                      RedCards: hasCards ? Int(Cell(disciplineTable!, cardRow!, "Rau")) : 0,
                      Assists: NullInt(Cell(offensiveTable, row, "Sköpuð færi (Stoð)")),
                      Turnovers: NullInt(Cell(offensiveTable, row, "TB (TB/S.Brot)")),
                      Steals: hasCards ? NullInt(Cell(disciplineTable!, cardRow!, "Stolinn")) : null,
                      PlusMinus: Double(Cell(offensiveTable, row, "+/-")),
                      Shots: NullInt(Cell(offensiveTable, row, "Skot")),
                      Blocks: hasCards ? NullInt(Cell(disciplineTable!, cardRow!, "Blokk")) : null,
                      Stops: hasCards ? Double(Cell(disciplineTable!, cardRow!, "Lögleg Stopp")) : null,
                      ExpectedGoals: Double(Cell(offensiveTable, row, "xG")),
                      PenaltiesEarned: NullInt(Cell(offensiveTable, row, "Víta send.")),
                      PenaltyGoals: NullInt(Cell(offensiveTable, row, "Víta Mörk"))
                  ));
              }
          }

          return lines;
      }

      private static string Cell(ParsedTable table, IReadOnlyList<string> row, string column)
      {
          var idx = table.Columns.ToList().IndexOf(column);
          return idx >= 0 && idx < row.Count ? row[idx] : string.Empty;
      }

      private static int Int(string val) => int.TryParse(val.Trim(), out var n) ? n : 0;
      private static int? NullInt(string val) => int.TryParse(val.Trim(), out var n) ? n : null;
      private static double? Double(string val) => double.TryParse(val.Replace("%", "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

      private static (int? Jersey, string Name) SplitPlayer(string cell)
      {
          var dot = cell.IndexOf('.');
          if (dot > 0 && int.TryParse(cell[..dot].Trim(), out var jersey))
              return (jersey, cell[(dot + 1)..].Trim());
          return (null, cell.Trim());
      }
  }
  ```

- [ ] **Step 4: Create parser unit test**
  Copy the test fixtures files into test project if not already present, and write `Ez.Handball.Tests/Ingestion/Parsing/MatchStatLineBuilderTests.cs` verifying GK & Outfield mapping from fixture.
  ```csharp
  using Ez.Handball.Ingestion.Parsing;
  using Xunit;

  namespace Ez.Handball.Tests.Ingestion.Parsing;

  public class MatchStatLineBuilderTests
  {
      private static string LoadFixture() =>
          System.IO.File.ReadAllText("../../../tools/HbStatz.Spike.Tests/fixtures/pergame/12922-home.html");

      [Fact]
      public void Build_MapsBothOutfieldAndGoalkeepersCorrectly()
      {
          var tables = StatsTableParser.ParseAll(LoadFixture());
          var lines = MatchStatLineBuilder.Build(tables, "home");

          Assert.NotEmpty(lines);
          var gk = lines.First(l => l.IsGoalkeeper);
          Assert.NotNull(gk.Saves);
          Assert.NotNull(gk.SaveRate);

          var outfield = lines.First(l => !l.IsGoalkeeper && l.Goals > 0);
          Assert.NotNull(outfield.ExpectedGoals);
          Assert.NotNull(outfield.PlusMinus);
          Assert.NotNull(outfield.Assists);
      }
  }
  ```

- [ ] **Step 5: Run tests and commit**
  Run: `dotnet test --filter "MatchStatLineBuilderTests"`
  Expected: PASS
  ```bash
  git add Ez.Handball.Ingestion/Models/ParsedTable.cs Ez.Handball.Ingestion/Models/PlayerStatLine.cs Ez.Handball.Ingestion/Parsing/StatsTableParser.cs Ez.Handball.Ingestion/Parsing/MatchStatLineBuilder.cs Ez.Handball.Tests/Ingestion/Parsing/MatchStatLineBuilderTests.cs
  git commit -m "feat: implement production-ready AngleSharp stats parser and line builder"
  ```

---

### Task 4: Player Reconciliation Logic

**Files:**
- Create: `Ez.Handball.Ingestion/Parsing/PlayerReconciler.cs`
- Create: `Ez.Handball.Tests/Ingestion/Parsing/PlayerReconcilerTests.cs`

**Interfaces:**
- Consumes: `PlayerStatLine` and `IReadOnlyList<PlayerEntity>` roster.
- Produces: `string? playerId` representing resolved database player ID.

- [ ] **Step 1: Create unit test file**
  Create `Ez.Handball.Tests/Ingestion/Parsing/PlayerReconcilerTests.cs` covering exact and fuzzy mapping logic.
  ```csharp
  using Ez.Handball.Ingestion.Models;
  using Ez.Handball.Ingestion.Parsing;
  using Ez.Handball.Shared.Entities;
  using Xunit;

  namespace Ez.Handball.Tests.Ingestion.Parsing;

  public class PlayerReconcilerTests
  {
      private readonly List<PlayerEntity> _roster =
      [
          new() { RowKey = "p-1", Name = "Arnór Snær Óskarsson", JerseyNumber = "6" },
          new() { RowKey = "p-2", Name = "Gísli Þorgeir Kristjánsson", JerseyNumber = "10" }
      ];

      [Fact]
      public void Reconcile_ExactJerseyAndName_Matches()
      {
          var line = new PlayerStatLine("home", 6, "Arnór Snær Óskarsson", false, 0, 0, 0, 0);
          var id = PlayerReconciler.Reconcile(line, _roster);
          Assert.Equal("p-1", id);
      }

      [Fact]
      public void Reconcile_FuzzyNameWithoutJersey_Matches()
      {
          var line = new PlayerStatLine("home", null, "Arnor Snaer Oskarsson", false, 0, 0, 0, 0);
          var id = PlayerReconciler.Reconcile(line, _roster);
          Assert.Equal("p-1", id);
      }
  }
  ```

- [ ] **Step 2: Implement PlayerReconciler**
  Create `Ez.Handball.Ingestion/Parsing/PlayerReconciler.cs` with Unicode and accent-insensitive normalization + similarity distance check:
  ```csharp
  using System.Globalization;
  using System.Text;
  using Ez.Handball.Ingestion.Models;
  using Ez.Handball.Shared.Entities;

  namespace Ez.Handball.Ingestion.Parsing;

  public static class PlayerReconciler
  {
      public static string? Reconcile(PlayerStatLine line, IReadOnlyList<PlayerEntity> roster)
      {
          var normScraped = Normalize(line.Name);

          // 1. Exact Jersey + Normalized Name
          if (line.Jersey.HasValue)
          {
              var jerseyStr = line.Jersey.Value.ToString();
              var match = roster.FirstOrDefault(p => p.JerseyNumber == jerseyStr && Normalize(p.Name) == normScraped);
              if (match is not null) return match.RowKey;
          }

          // 2. Exact Normalized Name
          var nameMatch = roster.FirstOrDefault(p => Normalize(p.Name) == normScraped);
          if (nameMatch is not null) return nameMatch.RowKey;

          // 3. Fuzzy Name Match (> 90%)
          foreach (var p in roster)
          {
              if (GetSimilarity(Normalize(p.Name), normScraped) >= 0.9)
              {
                  return p.RowKey;
              }
          }

          // 4. Unique Jersey Match
          if (line.Jersey.HasValue)
          {
              var jerseyStr = line.Jersey.Value.ToString();
              var candidates = roster.Where(p => p.JerseyNumber == jerseyStr).ToList();
              if (candidates.Count == 1) return candidates[0].RowKey;
          }

          return null;
      }

      private static string Normalize(string name)
      {
          if (string.IsNullOrWhiteSpace(name)) return string.Empty;
          var normalized = name.Normalize(NormalizationForm.FormD).Trim().ToLowerInvariant();
          var sb = new StringBuilder();
          foreach (var c in normalized)
          {
              if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
              {
                  sb.Append(c);
              }
          }
          return sb.ToString().Normalize(NormalizationForm.FormC);
      }

      private static double GetSimilarity(string s, string t)
      {
          if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 1.0 : 0.0;
          if (string.IsNullOrEmpty(t)) return 0.0;
          int n = s.Length, m = t.Length;
          int[,] d = new int[n + 1, m + 1];
          for (int i = 0; i <= n; d[i, 0] = i++) { }
          for (int j = 0; j <= m; d[0, j] = j++) { }
          for (int i = 1; i <= n; i++)
          {
              for (int j = 1; j <= m; j++)
              {
                  int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                  d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
              }
          }
          return 1.0 - ((double)d[n, m] / Math.Max(n, m));
      }
  }
  ```

- [ ] **Step 3: Run test to verify**
  Run: `dotnet test --filter "PlayerReconcilerTests"`
  Expected: PASS

- [ ] **Step 4: Commit**
  ```bash
  git add Ez.Handball.Ingestion/Parsing/PlayerReconciler.cs Ez.Handball.Tests/Ingestion/Parsing/PlayerReconcilerTests.cs
  git commit -m "feat: add player reconciler helper with fuzzy matching"
  ```

---

### Task 5: Ingestion Functions (Sync Queue & Blob Parsing)

**Files:**
- Modify: `Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj`
- Modify: `Ez.Handball.Ingestion/Parsing/MatchParser.cs`
- Create: `Ez.Handball.Ingestion/Functions/FetchHbStatzMatchStatsFunction.cs`
- Create: `Ez.Handball.Ingestion/Functions/ParseHbStatzMatchStatsFunction.cs`
- Create: `Ez.Handball.Tests/Ingestion/Functions/ParseHbStatzMatchStatsFunctionTests.cs`

**Interfaces:**
- Consumes: Storage connection configuration
- Produces: Azure Queue triggered HTML fetching, and Blob triggered player stats merging.

- [ ] **Step 1: Install Azure Storage Queue package**
  Add PackageReference to `Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj`:
  ```xml
  <PackageReference Include="Azure.Storage.Queues" Version="12.19.0" />
  ```
  Run: `dotnet restore`

- [ ] **Step 2: Modify MatchParser to enqueue finished games**
  Update `MatchParser.cs` to inject `QueueServiceClient` and dispatch message to Storage Queue:
  ```csharp
  // Add dependency:
  private readonly QueueServiceClient _queueServiceClient;

  public MatchParser(ITableWriter tableWriter, IBlobArchiver blobArchiver, QueueServiceClient queueServiceClient, ILogger<MatchParser> logger)
  {
      _tableWriter = tableWriter;
      _blobArchiver = blobArchiver;
      _queueServiceClient = queueServiceClient;
      _logger = logger;
  }
  ```
  Add trigger block inside `ParseAsync`:
  ```csharp
  if (details.ReportStatus == "S" && tournament.IngestHbStatz)
  {
      var queueClient = _queueServiceClient.GetQueueClient("hbstatz-match-sync");
      await queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
      var messageJson = JsonSerializer.Serialize(new { MatchId = matchId, TournamentId = tournamentId });
      var bytes = System.Text.Encoding.UTF8.GetBytes(messageJson);
      await queueClient.SendMessageAsync(Convert.ToBase64String(bytes), ct);
      _logger.LogInformation("Enqueued HBStatz sync message for match {MatchId}", matchId);
  }
  ```

- [ ] **Step 3: Implement Fetch function**
  Create `Ez.Handball.Ingestion/Functions/FetchHbStatzMatchStatsFunction.cs`:
  ```csharp
  using System.Text.Json;
  using Ez.Handball.Ingestion.Services;
  using Microsoft.Azure.Functions.Worker;
  using Microsoft.Extensions.Logging;

  namespace Ez.Handball.Ingestion.Functions;

  public class FetchHbStatzMatchStatsFunction
  {
      private readonly IMatchReportClient _reportClient;
      private readonly IBlobArchiver _blobArchiver;

      public FetchHbStatzMatchStatsFunction(IMatchReportClient reportClient, IBlobArchiver blobArchiver)
      {
          _reportClient = reportClient;
          _blobArchiver = blobArchiver;
      }

      [Function("FetchHbStatzMatchStats")]
      public async Task RunAsync(
          [QueueTrigger("hbstatz-match-sync", Connection = "HandballStorageConnection")] string message,
          FunctionContext context)
      {
          var logger = context.GetLogger<FetchHbStatzMatchStatsFunction>();
          var doc = JsonSerializer.Deserialize<JsonElement>(message);
          var matchId = doc.GetProperty("MatchId").GetString() ?? string.Empty;

          logger.LogInformation("Scraping HBStatz team stats for match {MatchId}", matchId);

          var homeHtml = await _reportClient.GetTeamPageHtmlAsync(matchId, "home", context.CancellationToken);
          var awayHtml = await _reportClient.GetTeamPageHtmlAsync(matchId, "away", context.CancellationToken);

          await _blobArchiver.SaveAsync($"hbstatz/matches/{matchId}/players-home.html", homeHtml, context.CancellationToken);
          await _blobArchiver.SaveAsync($"hbstatz/matches/{matchId}/players-away.html", awayHtml, context.CancellationToken);

          logger.LogInformation("Archived HBStatz team stats for match {MatchId}", matchId);
      }
  }
  ```

- [ ] **Step 4: Implement Parse function**
  Create `Ez.Handball.Ingestion/Functions/ParseHbStatzMatchStatsFunction.cs`:
  ```csharp
  using Ez.Handball.Ingestion.Parsing;
  using Ez.Handball.Ingestion.Services;
  using Ez.Handball.Shared.Entities;
  using Microsoft.Azure.Functions.Worker;
  using Microsoft.Extensions.Logging;

  namespace Ez.Handball.Ingestion.Functions;

  public class ParseHbStatzMatchStatsFunction
  {
      private readonly ITableWriter _tableWriter;
      private readonly ILogger<ParseHbStatzMatchStatsFunction> _logger;

      public ParseHbStatzMatchStatsFunction(ITableWriter tableWriter, ILogger<ParseHbStatzMatchStatsFunction> logger)
      {
          _tableWriter = tableWriter;
          _logger = logger;
      }

      [Function("ParseHbStatzMatchStats")]
      public async Task RunAsync(
          [BlobTrigger("raw/hbstatz/matches/{matchId}/players-{side}.html", Connection = "HandballStorageConnection")] string htmlContent,
          string matchId,
          string side,
          FunctionContext context)
      {
          _logger.LogInformation("Parsing HBStatz stats for match {MatchId} ({Side} side)", matchId, side);

          var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", context.CancellationToken);
          if (matches.Count == 0)
          {
              _logger.LogError("Match {MatchId} not found in database; cannot reconcile HBStatz players.", matchId);
              return;
          }
          var match = matches[0];
          var teamId = side == "home" ? match.HomeTeamId : match.AwayTeamId;

          var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{teamId}'", context.CancellationToken);
          var tables = StatsTableParser.ParseAll(htmlContent);
          var lines = MatchStatLineBuilder.Build(tables, side);

          foreach (var line in lines)
          {
              var playerId = PlayerReconciler.Reconcile(line, roster);
              if (playerId is null)
              {
                  _logger.LogWarning("Could not reconcile HBStatz player '{Name}' (Jersey {Jersey}) in team {TeamId}", line.Name, line.Jersey, teamId);
                  continue;
              }

              await _tableWriter.UpsertAsync("PlayerStats", new PlayerStatEntity
              {
                  PartitionKey = matchId,
                  RowKey = playerId,
                  Assists = line.Assists,
                  Turnovers = line.Turnovers,
                  Steals = line.Steals,
                  PlusMinus = line.PlusMinus,
                  Shots = line.Shots,
                  Blocks = line.Blocks,
                  Stops = line.Stops,
                  ExpectedGoals = line.ExpectedGoals,
                  PenaltiesEarned = line.PenaltiesEarned,
                  PenaltyGoals = line.PenaltyGoals,
                  Saves = line.Saves,
                  SaveRate = line.SaveRate,
                  ExpectedSaves = line.ExpectedSaves,
                  GoalsAgainst = line.GoalsAgainst,
                  PenaltySaves = line.PenaltySaves
              }, context.CancellationToken, TableUpdateMode.Merge);
          }

          _logger.LogInformation("Finished merging HBStatz stats for match {MatchId} ({Side} side)", matchId, side);
      }
  }
  ```

- [ ] **Step 5: Write Integration tests & run**
  Write tests confirming functions fetch and parse correctly. Fix all mocks in existing tests (like `MatchParserTests`) by passing the new mock `QueueServiceClient`.
  Run: `dotnet test`
  Expected: PASS

- [ ] **Step 6: Commit**
  ```bash
  git add Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj Ez.Handball.Ingestion/Parsing/MatchParser.cs Ez.Handball.Ingestion/Functions/FetchHbStatzMatchStatsFunction.cs Ez.Handball.Ingestion/Functions/ParseHbStatzMatchStatsFunction.cs Ez.Handball.Tests/
  git commit -m "feat: implement fetch and parse functions with Azure Queue trigger"
  ```

---

### Task 6: Dependency Injection and Seeding Setup

**Files:**
- Modify: `Ez.Handball.Ingestion/Program.cs`
- Modify: `Ez.Handball.Ingestion/local.settings.json`

**Interfaces:**
- Consumes: `HostBuilderContext` configuration builder
- Produces: Service registration of scrapers and queue services.

- [ ] **Step 1: Setup Service registrations in Program.cs**
  Open `Ez.Handball.Ingestion/Program.cs`. Register the `QueueServiceClient` and scrapers:
  ```csharp
  // Add using directives:
  using Azure.Storage.Queues;

  // Inside ConfigureServices method:
  services.AddSingleton(_ => new QueueServiceClient(storageConnection));
  services.AddHttpClient<IHbStatzClient, HbStatzClient>(client =>
  {
      client.BaseAddress = new Uri("https://hbstatz.is/");
      client.DefaultRequestHeaders.UserAgent.ParseAdd("EzHandball-Ingestion/1.0 (+https://github.com/kromby/Ez.Handball.Backend)");
  });
  services.AddSingleton<IMatchReportClient, MatchReportClient>();
  ```

- [ ] **Step 2: Add local configurations**
  Ensure local.settings.json has `"HandballStorageConnection": "UseDevelopmentStorage=true"` or the development string.

- [ ] **Step 3: Run complete test suite and build**
  Run: `dotnet build`
  Expected: Success without errors or warnings.
  Run: `dotnet test`
  Expected: PASS

- [ ] **Step 4: Commit**
  ```bash
  git add Ez.Handball.Ingestion/Program.cs
  git commit -m "feat: wire up DI for HBStatz scraper services"
  ```
