# HBStatz Scraping Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a disposable C# console tool that scrapes Olís deild karla outfield + goalkeeper stats from HBStatz into JSON/CSV, then record a go/no-go findings note — proving whether scraping is a viable HBStatz integration path for Backend #7.

**Architecture:** A standalone console app under `tools/` (deliberately NOT in `Ez.Handball.sln`, so the product build and CI never touch it). Pure logic — HTML table parsing, iframe resolution, CSV serialization — lives in small testable classes covered by an xUnit project. A thin `HbStatzClient` does the live HTTP fetch; `Program` wires fetch → parse → write → summarize. The captured datasets and a `FINDINGS.md` are committed as the spike's evidence.

**Tech Stack:** .NET 9 console app, [AngleSharp](https://anglesharp.github.io/) for HTML parsing, `System.Text.Json` for JSON, hand-rolled CSV writer, xUnit + Moq-free fixture tests.

## Global Constraints

- **Disposable & isolated:** the spike projects are NOT added to `Ez.Handball.sln`. Nothing wires into Azure Functions, blob storage, or Table Storage.
- **Scope:** Olís deild karla only — outfield (`OlisDeildKarlaTolfraedi.php`) and goalkeepers (`OlisDeildKarlaTolfraedi2.php`). No women's league, no other competitions, no team-level pages.
- **Investigation-only / polite:** single competition, run manually, no concurrency, descriptive `User-Agent` identifying the request, no redistribution of captured data beyond evidence committed to this repo.
- **Branch:** all work on `docs/hbstatz-scraping-spike-spec`.
- **Discovered facts (verified live 2026-06-21):**
  - Outfield page serves `<table id="statz1">` inline in the page HTML.
  - GK page is a wrapper; the real table is in an `<iframe src="https://hbstatz.is/test22.php">` (a second `test23.php` iframe also exists).
  - Headers are HTML-entity-encoded Icelandic (e.g. `M&ouml;rk` → `Mörk`, `Li&eth;` → `Lið`).
  - A row's club is a text abbreviation cell (e.g. `KA`) and/or a logo `<img src="player_images/ka.png">`. There is **no numeric player ID** in the table.
  - Advanced columns present that HSÍ lacks: `xG` (outfield) / `xS` (GK), `+/-`, `Stl`, `Blk`.

---

### Task 1: Scaffold the isolated spike projects

**Files:**
- Create: `tools/HbStatz.Spike/HbStatz.Spike.csproj`
- Create: `tools/HbStatz.Spike/Program.cs`
- Create: `tools/HbStatz.Spike/README.md`
- Create: `tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj`
- Create: `tools/HbStatz.Spike/.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: a runnable console project `HbStatz.Spike` and a test project `HbStatz.Spike.Tests` that references it. Neither is in `Ez.Handball.sln`.

- [ ] **Step 1: Create the console project file**

`tools/HbStatz.Spike/HbStatz.Spike.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>HbStatz.Spike</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AngleSharp" Version="1.1.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create a placeholder entry point**

`tools/HbStatz.Spike/Program.cs`:

```csharp
namespace HbStatz.Spike;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        Console.WriteLine("HBStatz scraping spike — run with no args to scrape Olís deild karla.");
        return Task.FromResult(0);
    }
}
```

- [ ] **Step 3: Create the README and .gitignore**

`tools/HbStatz.Spike/README.md`:

```markdown
# HBStatz scraping spike

Disposable proof-of-concept for Backend #7. Scrapes Olís deild karla outfield +
goalkeeper stats from hbstatz.is into JSON/CSV. NOT part of Ez.Handball.sln and
never wired into the ingestion pipeline. See FINDINGS.md for results.

Run: `dotnet run --project tools/HbStatz.Spike`
Output: `tools/HbStatz.Spike/output/*.json` and `*.csv`
```

`tools/HbStatz.Spike/.gitignore`:

```
bin/
obj/
```

(The `output/` directory is intentionally NOT ignored — captured datasets are committed as evidence in Task 5.)

- [ ] **Step 4: Create the test project file**

`tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HbStatz.Spike\HbStatz.Spike.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Verify both projects build and the app runs**

Run: `dotnet run --project tools/HbStatz.Spike`
Expected: prints `HBStatz scraping spike — run with no args to scrape Olís deild karla.` and exits 0.

Run: `dotnet build tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj`
Expected: build succeeds (0 errors).

- [ ] **Step 6: Commit**

```bash
git add tools/HbStatz.Spike tools/HbStatz.Spike.Tests
git commit -m "chore: scaffold isolated HBStatz scraping spike projects (#7)"
```

---

### Task 2: Capture live HTML fixtures

**Files:**
- Create: `tools/HbStatz.Spike.Tests/fixtures/outfield.html`
- Create: `tools/HbStatz.Spike.Tests/fixtures/gk-wrapper.html`
- Create: `tools/HbStatz.Spike.Tests/fixtures/gk-inner.html`

**Interfaces:**
- Consumes: nothing.
- Produces: three committed HTML fixtures used as deterministic test inputs by Tasks 3–4. `outfield.html` contains an inline `<table id="statz1">`. `gk-wrapper.html` contains `<iframe>` tags but no inline stats table. `gk-inner.html` is the iframe target containing the GK stats table.

- [ ] **Step 1: Download the three fixtures**

```bash
mkdir -p tools/HbStatz.Spike.Tests/fixtures
UA="Mozilla/5.0 (EzHandball-spike; +https://github.com/kromby/Ez.Handball.Backend/issues/7)"
curl -s -A "$UA" "https://hbstatz.is/OlisDeildKarlaTolfraedi.php"  -o tools/HbStatz.Spike.Tests/fixtures/outfield.html
curl -s -A "$UA" "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php" -o tools/HbStatz.Spike.Tests/fixtures/gk-wrapper.html
curl -s -A "$UA" "https://hbstatz.is/test22.php"                   -o tools/HbStatz.Spike.Tests/fixtures/gk-inner.html
```

- [ ] **Step 2: Verify the fixtures have the expected shape**

```bash
grep -c 'id="statz1"' tools/HbStatz.Spike.Tests/fixtures/outfield.html   # expect >= 1
grep -c '<iframe'      tools/HbStatz.Spike.Tests/fixtures/gk-wrapper.html # expect >= 1
grep -c 'test22.php'   tools/HbStatz.Spike.Tests/fixtures/gk-wrapper.html # expect >= 1
grep -c '<tr'          tools/HbStatz.Spike.Tests/fixtures/gk-inner.html   # expect many (data rows)
```

Expected: `outfield.html` has the inline table, `gk-wrapper.html` references the iframe, `gk-inner.html` has the data rows.

> If any `curl` returns a tiny/challenge page instead of data (bot protection), STOP and record it: the HttpClient path is blocked and the Playwright fallback (noted in Task 3 / FINDINGS) is needed. This is itself a spike finding.

- [ ] **Step 3: Commit**

```bash
git add tools/HbStatz.Spike.Tests/fixtures
git commit -m "test: add live HBStatz HTML fixtures for spike parser (#7)"
```

---

### Task 3: Iframe resolver + stats-table parser (pure logic, TDD)

**Files:**
- Create: `tools/HbStatz.Spike/IframeResolver.cs`
- Create: `tools/HbStatz.Spike/ParsedTable.cs`
- Create: `tools/HbStatz.Spike/StatsTableParser.cs`
- Test: `tools/HbStatz.Spike.Tests/StatsTableParserTests.cs`
- Test: `tools/HbStatz.Spike.Tests/IframeResolverTests.cs`

**Interfaces:**
- Consumes: fixtures from Task 2.
- Produces:
  - `IframeResolver.ExtractSources(string html, string baseUrl)` → `IReadOnlyList<string>` of absolute iframe URLs (empty if none).
  - `IframeResolver.HasInlineStatsTable(string html)` → `bool` (true when a `<table id="statz1">` is present).
  - `ParsedTable` record: `IReadOnlyList<string> Columns`, `IReadOnlyList<IReadOnlyList<string>> Rows`, plus `int RowCount => Rows.Count`.
  - `StatsTableParser.Parse(string html)` → `ParsedTable`. Reads the stats `<table>` (prefers `#statz1`, else the first table with a `<thead>`), HTML-decodes header and cell text, and for an otherwise-empty cell containing only a logo `<img>` uses the image filename without extension (e.g. `player_images/ka.png` → `ka`).

- [ ] **Step 1: Write the failing parser test**

`tools/HbStatz.Spike.Tests/StatsTableParserTests.cs`:

```csharp
using HbStatz.Spike;

namespace HbStatz.Spike.Tests;

public class StatsTableParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Parse_Outfield_DecodesHeadersAndReturnsRows()
    {
        var table = StatsTableParser.Parse(Fixture("outfield.html"));

        Assert.Contains("Nafn", table.Columns);
        Assert.Contains("Mörk", table.Columns);   // HTML entity M&ouml;rk decoded
        Assert.Contains("xG", table.Columns);      // advanced metric HSÍ lacks
        Assert.True(table.RowCount > 0);
        Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Count));
    }

    [Fact]
    public void Parse_Gk_CapturesClubAndExpectedSaves()
    {
        var table = StatsTableParser.Parse(Fixture("gk-inner.html"));

        Assert.Contains("Nafn", table.Columns);
        Assert.Contains("xS", table.Columns);      // expected saves
        Assert.True(table.RowCount > 0);
        // club is captured either as the abbrev text cell or from the logo image filename
        var firstRowText = string.Join("|", table.Rows[0]).ToLowerInvariant();
        Assert.Matches("ka|val|fh|haukar|afturelding|stjarnan|fram|selfoss|grotta|ir|hk", firstRowText);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter StatsTableParserTests`
Expected: FAIL — `StatsTableParser` / `ParsedTable` do not exist (compile error).

- [ ] **Step 3: Implement `ParsedTable`**

`tools/HbStatz.Spike/ParsedTable.cs`:

```csharp
namespace HbStatz.Spike;

public sealed record ParsedTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;
}
```

- [ ] **Step 4: Implement `StatsTableParser`**

`tools/HbStatz.Spike/StatsTableParser.cs`:

```csharp
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace HbStatz.Spike;

public static class StatsTableParser
{
    public static ParsedTable Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var table = doc.QuerySelector("table#statz1")
                    ?? doc.QuerySelectorAll("table").FirstOrDefault(t => t.QuerySelector("thead") != null)
                    ?? throw new InvalidOperationException("No stats table found in HTML.");

        var columns = table.QuerySelectorAll("thead th, thead td")
            .Select(CellText)
            .ToList();

        var rows = table.QuerySelectorAll("tbody tr")
            .Select(tr => (IReadOnlyList<string>)tr.QuerySelectorAll("td").Select(CellText).ToList())
            .Where(r => r.Count > 0)
            .ToList();

        return new ParsedTable(columns, rows);
    }

    private static string CellText(IElement cell)
    {
        var text = cell.TextContent.Trim();
        if (!string.IsNullOrEmpty(text)) return text;

        var img = cell.QuerySelector("img");
        var src = img?.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return string.Empty;

        var file = src.Split('/').Last();           // player_images/ka.png -> ka.png
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;          // ka.png -> ka
    }
}
```

(AngleSharp decodes HTML entities automatically via `TextContent`.)

- [ ] **Step 5: Run the parser test to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter StatsTableParserTests`
Expected: PASS (both tests).

- [ ] **Step 6: Write the failing iframe-resolver test**

`tools/HbStatz.Spike.Tests/IframeResolverTests.cs`:

```csharp
using HbStatz.Spike;

namespace HbStatz.Spike.Tests;

public class IframeResolverTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Outfield_HasInlineTable_AndNoIframesNeeded()
    {
        var html = Fixture("outfield.html");
        Assert.True(IframeResolver.HasInlineStatsTable(html));
    }

    [Fact]
    public void GkWrapper_HasNoInlineTable_AndResolvesAbsoluteIframeSources()
    {
        var html = Fixture("gk-wrapper.html");
        Assert.False(IframeResolver.HasInlineStatsTable(html));

        var sources = IframeResolver.ExtractSources(html, "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php");
        Assert.Contains(sources, s => s.EndsWith("test22.php"));
        Assert.All(sources, s => Assert.StartsWith("https://", s));
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter IframeResolverTests`
Expected: FAIL — `IframeResolver` does not exist.

- [ ] **Step 8: Implement `IframeResolver`**

`tools/HbStatz.Spike/IframeResolver.cs`:

```csharp
using AngleSharp.Html.Parser;

namespace HbStatz.Spike;

public static class IframeResolver
{
    public static bool HasInlineStatsTable(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelector("table#statz1") != null;
    }

    public static IReadOnlyList<string> ExtractSources(string html, string baseUrl)
    {
        var baseUri = new Uri(baseUrl);
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelectorAll("iframe")
            .Select(f => f.GetAttribute("src"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new Uri(baseUri, s!).AbsoluteUri)
            .Distinct()
            .ToList();
    }
}
```

- [ ] **Step 9: Run the full test project to verify all pass**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj`
Expected: PASS (4 tests).

- [ ] **Step 10: Commit**

```bash
git add tools/HbStatz.Spike/IframeResolver.cs tools/HbStatz.Spike/ParsedTable.cs tools/HbStatz.Spike/StatsTableParser.cs tools/HbStatz.Spike.Tests/StatsTableParserTests.cs tools/HbStatz.Spike.Tests/IframeResolverTests.cs
git commit -m "feat: add HBStatz iframe resolver and stats-table parser (#7)"
```

---

### Task 4: CSV + JSON serializers (TDD the CSV)

**Files:**
- Create: `tools/HbStatz.Spike/TableSerializer.cs`
- Test: `tools/HbStatz.Spike.Tests/TableSerializerTests.cs`

**Interfaces:**
- Consumes: `ParsedTable` from Task 3.
- Produces:
  - `TableSerializer.ToCsv(ParsedTable table)` → `string` (RFC-4180-ish: comma-separated, fields containing `"`, `,`, or newline are double-quoted with `"` doubled; first line is the header row).
  - `TableSerializer.ToJson(ParsedTable table)` → `string` (an array of objects keyed by column name; duplicate column names are de-duplicated by suffixing `_2`, `_3`, … so JSON keys stay unique).

- [ ] **Step 1: Write the failing CSV/JSON test**

`tools/HbStatz.Spike.Tests/TableSerializerTests.cs`:

```csharp
using System.Text.Json;
using HbStatz.Spike;

namespace HbStatz.Spike.Tests;

public class TableSerializerTests
{
    private static ParsedTable Sample() => new(
        Columns: new[] { "Nafn", "Lið", "Mörk" },
        Rows: new IReadOnlyList<string>[]
        {
            new[] { "Jón, Jónsson", "KA", "5.5" },
            new[] { "Quote\"Man", "Valur", "3.0" },
        });

    [Fact]
    public void ToCsv_QuotesFieldsWithCommasAndQuotes()
    {
        var csv = TableSerializer.ToCsv(Sample());
        var lines = csv.Replace("\r\n", "\n").Trim().Split('\n');

        Assert.Equal("Nafn,Lið,Mörk", lines[0]);
        Assert.Equal("\"Jón, Jónsson\",KA,5.5", lines[1]);
        Assert.Equal("\"Quote\"\"Man\",Valur,3.0", lines[2]);
    }

    [Fact]
    public void ToJson_ProducesObjectPerRowKeyedByColumn()
    {
        var json = TableSerializer.ToJson(Sample());
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("KA", doc.RootElement[0].GetProperty("Lið").GetString());
        Assert.Equal("5.5", doc.RootElement[0].GetProperty("Mörk").GetString());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter TableSerializerTests`
Expected: FAIL — `TableSerializer` does not exist.

- [ ] **Step 3: Implement `TableSerializer`**

`tools/HbStatz.Spike/TableSerializer.cs`:

```csharp
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HbStatz.Spike;

public static class TableSerializer
{
    public static string ToCsv(ParsedTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Select(Escape)));
        foreach (var row in table.Rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    public static string ToJson(ParsedTable table)
    {
        var keys = UniqueKeys(table.Columns);
        var objects = table.Rows.Select(row =>
        {
            var obj = new Dictionary<string, string>();
            for (var i = 0; i < keys.Count && i < row.Count; i++)
                obj[keys[i]] = row[i];
            return obj;
        });
        return JsonSerializer.Serialize(objects, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Icelandic chars readable
        });
    }

    private static string Escape(string field)
    {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    private static List<string> UniqueKeys(IReadOnlyList<string> columns)
    {
        var seen = new Dictionary<string, int>();
        var keys = new List<string>();
        foreach (var c in columns)
        {
            var key = string.IsNullOrEmpty(c) ? "col" : c;
            if (seen.TryGetValue(key, out var n))
            {
                seen[key] = n + 1;
                key = $"{key}_{n + 1}";
            }
            else seen[key] = 1;
            keys.Add(key);
        }
        return keys;
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tools/HbStatz.Spike.Tests/HbStatz.Spike.Tests.csproj --filter TableSerializerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add tools/HbStatz.Spike/TableSerializer.cs tools/HbStatz.Spike.Tests/TableSerializerTests.cs
git commit -m "feat: add CSV/JSON serializers for HBStatz spike (#7)"
```

---

### Task 5: HbStatzClient + Program wiring, run end-to-end, capture evidence

**Files:**
- Create: `tools/HbStatz.Spike/HbStatzClient.cs`
- Modify: `tools/HbStatz.Spike/Program.cs`
- Create (generated, then committed): `tools/HbStatz.Spike/output/outfield.json`, `output/outfield.csv`, `output/goalkeepers.json`, `output/goalkeepers.csv`

**Interfaces:**
- Consumes: `IframeResolver`, `StatsTableParser`, `ParsedTable`, `TableSerializer`.
- Produces:
  - `HbStatzClient.GetTableHtmlsAsync(string pageUrl, CancellationToken ct = default)` → `Task<IReadOnlyList<string>>`. Fetches `pageUrl` with a descriptive `User-Agent`; if it has an inline stats table, returns `[pageHtml]`; otherwise resolves iframe sources, fetches each (sequentially, no concurrency), and returns their HTML.
  - `Program.Main` writes the four output files and prints a per-dataset summary.

- [ ] **Step 1: Implement `HbStatzClient`**

`tools/HbStatz.Spike/HbStatzClient.cs`:

```csharp
namespace HbStatz.Spike;

public sealed class HbStatzClient(HttpClient http)
{
    private const string UserAgent =
        "EzHandball-spike/0.1 (+https://github.com/kromby/Ez.Handball.Backend/issues/7)";

    public async Task<IReadOnlyList<string>> GetTableHtmlsAsync(string pageUrl, CancellationToken ct = default)
    {
        var html = await GetAsync(pageUrl, ct);
        if (IframeResolver.HasInlineStatsTable(html))
            return new[] { html };

        var results = new List<string>();
        foreach (var src in IframeResolver.ExtractSources(html, pageUrl))
            results.Add(await GetAsync(src, ct));
        return results;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
```

- [ ] **Step 2: Wire `Program.Main`**

`tools/HbStatz.Spike/Program.cs`:

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

    public static async Task<int> Main(string[] args)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        Directory.CreateDirectory(outputDir);

        using var http = new HttpClient();
        var client = new HbStatzClient(http);

        foreach (var target in Targets)
        {
            var htmls = await client.GetTableHtmlsAsync(target.Url);
            // a target may resolve to multiple iframe tables; take the first non-empty stats table
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
        return 0;
    }
}
```

- [ ] **Step 3: Run the spike end-to-end**

Run: `dotnet run --project tools/HbStatz.Spike`
Expected: prints two `[outfield] N rows …` / `[goalkeepers] N rows …` summary blocks (N > 0 for both), and writes four files under `tools/HbStatz.Spike/output/`.

> If a target prints `NO TABLE FOUND` or the run throws on `EnsureSuccessStatusCode`, the live HTTP path is being blocked/challenged. Record this in FINDINGS (Task 6) as the headless-fallback trigger; do not fabricate output files.

- [ ] **Step 4: Sanity-check the captured output**

Run: `head -5 tools/HbStatz.Spike/output/outfield.csv && echo '---' && head -5 tools/HbStatz.Spike/output/goalkeepers.csv`
Expected: a header row of Icelandic column names followed by real player rows in each file.

- [ ] **Step 5: Commit the code and the captured evidence**

```bash
git add tools/HbStatz.Spike/HbStatzClient.cs tools/HbStatz.Spike/Program.cs tools/HbStatz.Spike/output
git commit -m "feat: scrape Olís deild karla stats end-to-end and capture datasets (#7)"
```

---

### Task 6: Write the findings note

**Files:**
- Create: `tools/HbStatz.Spike/FINDINGS.md`

**Interfaces:**
- Consumes: the captured `output/` datasets and everything learned in Tasks 1–5.
- Produces: the decision document that satisfies the spec's success criteria and feeds Backend #7's "decision recorded on the integration path."

- [ ] **Step 1: Write `FINDINGS.md`**

Use this structure, filling every section from the actual run (replace bracketed values with observed data — column lists copied from the run summary, row counts from the output files):

```markdown
# HBStatz scraping spike — findings

Spike for [Backend #7]. Target: Olís deild karla outfield + goalkeeper stats.

## 1. Data source path
- Outfield (`OlisDeildKarlaTolfraedi.php`): stats table (`<table id="statz1">`) is
  **server-rendered inline** — a plain HTTP GET returns the full data. No JS execution needed.
- Goalkeepers (`OlisDeildKarlaTolfraedi2.php`): wrapper page; the table is in an
  `<iframe src="…/test22.php">`. Fetch the wrapper, follow the iframe, parse the inner page.
- A plain `HttpClient` GET with a descriptive User-Agent was sufficient; **no headless
  browser required**. [If a fallback was needed, say so here and why.]

## 2. Available fields
- Outfield columns: [paste from run].
- Goalkeeper columns: [paste from run].
- Advanced metrics HSÍ does NOT provide: `xG` (outfield) / `xS` (GK) expected goals/saves,
  `+/-`, `Stl` (steals), `Blk` (blocks), [others]. This is the value HBStatz adds.

## 3. Player identity & join-ability
- Each row exposes player **name** + **club** (text abbrev and/or `player_images/{club}.png` logo).
- There is **no numeric player ID** in the table.
- Join to existing HSÍ players would be by normalized name + club. Risk: name collisions and
  spelling/diacritic variants; needs a reconciliation step. Feasibility: [your assessment].

## 4. Recommendation
- Go / no-go: [decision].
- If go, integration shape mirroring the HSÍ pipeline: scheduled fetch of the wrapper/inner
  HTML → archive raw HTML to a blob → parse table → map to entities (keyed by name+club, or a
  new HBStatz id if one is later negotiated). No JSON API exists today.

## 5. Legal / risk
- HBStatz is "all rights reserved" and is HSÍ's contracted stats provider — overlaps the #7
  partnership. This spike was investigation-only (single competition, manual, polite UA, no
  redistribution). **Any production/ongoing scraping depends on resolving the #7 partnership
  conversation.**
```

- [ ] **Step 2: Commit**

```bash
git add tools/HbStatz.Spike/FINDINGS.md
git commit -m "docs: record HBStatz scraping spike findings and recommendation (#7)"
```

---

## Self-Review

**Spec coverage:**
- Disposable C# console app, outside Functions app → Task 1 (not in sln). ✓
- Target Olís deild karla outfield + goalkeepers → Tasks 2, 5. ✓
- Discover-source-first / headless-fallback approach → discovery already done (server-rendered HTML; HttpClient path), fallback documented in Tasks 2/5/6. ✓
- Structured output (JSON/CSV) → Task 4 + Task 5. ✓
- Captured sample data committed as evidence → Task 5 Step 5. ✓
- Findings doc (source path, fields, join-ability, go/no-go, legal) → Task 6. ✓
- Legal/ToS note → Global Constraints + Task 6 §5. ✓
- Out-of-scope items (no pipeline wiring, one competition, no reconciliation build) → Global Constraints. ✓

**Placeholder scan:** No "TBD/TODO" in code. The only bracketed placeholders are inside the FINDINGS.md template, which is correct — those are values the executor fills from the live run, not unfinished plan content.

**Type consistency:** `ParsedTable(Columns, Rows)` with `RowCount` is used identically across Tasks 3–5. `StatsTableParser.Parse`, `IframeResolver.ExtractSources`/`HasInlineStatsTable`, `TableSerializer.ToCsv`/`ToJson`, and `HbStatzClient.GetTableHtmlsAsync` signatures match between their defining task and their callers. ✓
