# HBStatz scraping spike — findings

Spike for [Backend #7]. Target: Olís deild karla outfield + goalkeeper stats.

## 1. Data source path

- Outfield (`OlisDeildKarlaTolfraedi.php`): stats table (`<table id="statz1">`) is
  **server-rendered inline** — a plain HTTP GET returns the full data. No JS execution needed.
- Goalkeepers (`OlisDeildKarlaTolfraedi2.php`): wrapper page with no inline table; the real GK
  table lives in an `<iframe src="https://hbstatz.is/test22.php">`. The client fetches the
  wrapper, follows the iframe src, and parses the inner page.
  - Caveat for a production build: the wrapper carries **two** iframes (`test22.php` and
    `test23.php`), both rendering a `table#statz1`. The spike selects the first iframe (in DOM
    order) that parses to a non-empty table, which is `test22.php` (the GK stats). A real
    integration should pin the correct inner page explicitly rather than rely on iframe order.
- A plain `HttpClient` GET with a descriptive User-Agent (`EzHandball-spike/0.1 (+…/issues/7)`)
  was sufficient for both pages. **No headless browser or Playwright fallback was needed.** No
  bot-protection or JS challenge was encountered.

## 2. Available fields

Outfield columns (26 total, 245 player rows):

```
Lið, Nafn, Lið, Leikir, Mörk, %, xG, +/-, Víti, V%, FiV, Sto, Sk.F, P%, S7m, TB, Sto/TB, Sk.F/TB, G/A, Frk, Stl, Blk, Stp, Gul, 2m, Rau
```

Goalkeeper columns (18 total, 41 goalkeeper rows):

```
Lið, Nafn, Lið, Leikir, Varin, % Varsla, xS, +/-, Mörk á, Víti Varin, Víta %, Stl, Mörk, Stoð, TB, Gul, 2Mín, Rau
```

Advanced metrics HSÍ does **not** provide:

- `xG` — expected goals (outfield)
- `xS` — expected saves (GK)
- `+/-` — plus/minus differential (both)
- `Stl` — steals (both)
- `Blk` — blocks (outfield)
- `% Varsla` — save percentage (GK)
- `Víti Varin` / `Víta %` — penalty saves and percentage (GK)

These fields are the primary value HBStatz adds over the existing HSÍ data.

## 3. Player identity & join-ability

Each row exposes player **name** (`Nafn`) and **club** in two forms: a text abbreviation column
(`Lið`, which appears twice — once with the full club name, once as a lowercase short code such as
`ka`, `fh`, `ibv`). There is **no numeric player ID** in either table.

Joining to existing HSÍ players would rely on normalized name + club. This is feasible as a first
pass — the club short codes map cleanly to the existing `ClubEntity` abbreviations — but carries
two risks:

1. **Name collisions.** Two players with the same name at different clubs (rare) or the same club
   (extremely rare but possible) would be indistinguishable without a manual override.
2. **Diacritic and spelling variance.** Icelandic names rendered differently between the two
   sources (e.g. `Jón` vs `Jon`, patronymic suffix variations) would produce failed joins. A
   normalisation step (Unicode NFC + case-fold) catches most cases; a small residual set will need
   manual reconciliation or a managed mapping table.

Conclusion: join-by-name is workable for a first integration but requires a reconciliation step
and an escape hatch for edge cases. If a partnership under #7 yields a shared numeric ID, that
supersedes the name-join entirely.

## 4. Recommendation

**Go — technically.** The data is rich, cleanly structured, and retrievable with a plain HTTP
fetch. No infrastructure beyond an `HttpClient` and an HTML parser is needed.

**Conditional on partnership.** HBStatz is HSÍ's contracted stats provider (see §5). No
production build should start before the #7 conversation concludes.

If the partnership clears, the integration shape mirrors the existing HSÍ pipeline:

1. **Scheduled fetch** — a timer-triggered Azure Function fetches the outfield and GK wrapper
   pages on a configurable cadence (e.g. nightly during the season).
2. **Archive raw HTML** — each response archived to blob storage under a path such as
   `raw/hbstatz/{date}/{target}.html`, preserving a replayable source of truth.
3. **Parse** — a blob-triggered function runs `StatsTableParser` (already written in this spike)
   over the archived HTML, following the iframe src where needed.
4. **Map to entities** — rows keyed by normalized name + club (or by an HBStatz numeric ID if one
   is later negotiated). Stats stored in a new `HbStatzPlayerStats` table alongside the existing
   `PlayerStats` table, or merged into it as additional columns.

No JSON API exists on hbstatz.is today. All data comes from HTML tables.

## 5. Legal / risk

HBStatz is "all rights reserved" and is HSÍ's contracted stats provider — the data is not public
domain. This spike was investigation-only: single competition, single manual run, polite
User-Agent identifying the project and issue number, no redistribution of the scraped data.

**Any production or ongoing scraping depends on resolving the #7 partnership conversation.** Until
that is settled, this spike and its captured sample data (`output/`) should be treated as internal
evidence only.
