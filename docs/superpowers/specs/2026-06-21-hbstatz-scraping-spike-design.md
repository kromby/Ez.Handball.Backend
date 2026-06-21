# HBStatz scraping spike — design

**Issue:** subtask of [Backend #7 — HBStatz data integration (tracking)](https://github.com/kromby/Ez.Handball.Backend/issues/7)
**Date:** 2026-06-21
**Type:** Spike / proof-of-concept (time-boxed, disposable)

## Goal

Answer one question: **can we reliably extract HBStatz's richer player stats by
scraping, and what would a real integration look like?**

HBStatz holds the deeper dataset (defensive stats, advanced metrics) that the
HSÍ API does not. Issue #7 is a tracking issue blocked on a partnership
conversation; its definition of done includes "decision recorded on the
integration path." This spike feeds that decision with concrete evidence
instead of speculation.

The deliverable is a **runnable but disposable C# console app** plus a short
findings write-up. Nothing wires into the Functions pipeline, blob storage, or
Table Storage.

## Target

**Olís deild karla** (men's top division — the core fantasy/manager league),
two distinct table shapes:

- **Outfield player stats** — the `...Tolfraedi`-style stats table.
- **Goalkeeper stats** — the separate goalkeeper stats table.

Proving both shapes works shows the approach generalizes beyond a single
layout. Women's league, other competitions, and team-level pages are out of
scope.

## Approach — discover, then extract

The stats pages appear to render their data via JavaScript/AJAX: the static
HTML returned by a plain GET shows only navigation/menu chrome, no data rows.
The menu links carry query parameters like `k` / `f` / `d` (gender / division /
format). The spike therefore works in two steps and prefers the cheaper one:

1. **Discover the data source.** Inspect the page's network traffic (browser
   devtools, or the chrome-devtools MCP) to find the request the page makes to
   load stats — most likely a `.php` endpoint returning JSON or an HTML
   fragment, parameterized by the menu's `k`/`f`/`d`-style values for Olís
   deild karla.
2. **Extract over HTTP if possible.** If such an endpoint exists, the console
   app calls it directly with `HttpClient` — mirroring how the existing
   `HsiApiClient` sets a browser-style `Accept` header — and parses the
   response (JSON or HTML fragment).
3. **Headless fallback.** Only if no usable endpoint exists, render the page
   with **Playwright for .NET** and scrape the rendered DOM.

**Which path worked is itself a primary finding** and must be recorded.

## Components

Kept small and disposable.

- A single C# console project, e.g. `tools/HbStatz.Spike/`, kept out of the
  product build (not referenced by `Ez.Handball.sln`'s shipping projects, or
  explicitly excluded). It does not need to be part of the main solution.
- **`HbStatzClient`** — fetches the outfield and goalkeeper data for Olís deild
  karla via whichever path step 1–3 settled on.
- **Parser/mapper** — turns the raw response into a flat record per player
  (one record shape for outfield, one for goalkeepers).
- **`Program.cs`** — runs both fetches, writes output files to a local output
  directory, and prints a summary to the console (row counts, column list, a
  few sample rows).

## Output / evidence

Committed alongside the tool as the spike's evidence:

- Captured datasets: `outfield.json` + `outfield.csv` and
  `goalkeepers.json` + `goalkeepers.csv` (or equivalent), containing real Olís
  deild karla data.
- A **findings markdown** answering:
  - **Data source path** — which extraction path worked (direct endpoint vs.
    headless), the exact endpoint/URL and parameters if applicable.
  - **Available fields** — the full column list, calling out the
    defensive/advanced metrics HSÍ does *not* provide.
  - **Player identity & join-ability** — how a player is identified (name only?
    jersey number? club?) and whether that is enough to reconcile with existing
    HSÍ player records — feasibility noted, reconciliation not built.
  - **Go / no-go recommendation** and a rough shape of a real integration
    (e.g. discovered-endpoint HTTP fetch → archive blob → parse, matching the
    existing HSÍ pipeline pattern).

## Legal / risk note

HBStatz is "all rights reserved" (`© HBStatz`) and is HSÍ's contracted
statistics provider — overlapping directly with the #7 partnership. The spike
is **investigation-only**:

- Single competition (Olís deild karla), low volume, run manually.
- Polite: rate-limited, identifies itself via a sensible User-Agent.
- No redistribution of captured data beyond evidence committed to this repo.

The findings doc must flag that **any production use depends on resolving the
#7 partnership conversation**; the spike does not authorize ongoing scraping.

## Out of scope (YAGNI)

- No Azure Functions, blob, or Table Storage wiring.
- No scheduling or automation.
- No other competitions and no women's league.
- No goalkeeper/outfield player-identity reconciliation with HSÍ data beyond
  *noting* feasibility.
- No retry logic, error hardening, or auth.

## Success criteria

1. Running the console app produces real Olís deild karla **outfield** and
   **goalkeeper** datasets as JSON/CSV.
2. The findings doc answers: data-source path, available fields (highlighting
   what HSÍ lacks), player join-ability, and a go/no-go recommendation.
