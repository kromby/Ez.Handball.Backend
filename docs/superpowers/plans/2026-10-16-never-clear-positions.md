# Never-Clear Player Positions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HBStatz data may correct a player's positions but must never clear one, so an admin-given secondary survives later syncs (Backend#167).

**Architecture:** One pure merge rule, `PositionMerge.Apply`, decides what to store from the existing positions and the freshly computed ones. The live aggregator and the bulk backfill call it instead of overwriting. `PlayerParser` also stops dropping `PositionSecondary` when a club transfer recreates the player's row. No new columns, tables, endpoints or backfill. The admin endpoint `POST /api/admin/players/{id}/position` already sets both positions.

**Tech Stack:** .NET 8, C#, Azure Functions (isolated), xUnit, Moq.

**Spec:** none written — the design was approved in chat on 2026-10-16. It is captured in full in this header and in the Rule section below.

## The rule (binding)

```
newPrimary   = computedPrimary                                   // data corrects the primary
newSecondary = computedSecondary, if non-blank and != newPrimary  // data corrects the secondary
             else existingSecondary, if non-blank and != newPrimary
             else existingPrimary,   if non-blank and != newPrimary
             else ""
```

Examples:

| Existing (primary/secondary) | Computed (primary/secondary) | Result |
|---|---|---|
| CB/LB | LB/none | LB/CB |
| CB/LB | CB/none | CB/LB (an admin secondary survives) |
| CB/LB | RB/LW | RB/LW (the data corrects both) |
| CB/"" | CB/none | CB/"" |
| ""/"" | CB/none | CB/"" |
| CB/"" | LB/none | LB/CB |

## Global Constraints

- Worktree `/home/kromby/source/Handbolti/wt-167-positions`, branch `feat/167-never-clear-positions`. Never touch `/home/kromby/source/Handbolti/Ez.Handball.Backend`.
- Build: `dotnet build Ez.Handball.sln`. Tests: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "..."`.
  - Before using Azurite, run `pgrep -fa azurite`. If one is already running, reuse it and never start a second.
  - These tasks are Moq-based and shouldn't need Azurite.
- Commits: Conventional Commits, imperative lowercase subject <70 chars, **no Co-Authored-By or any trailer lines**, do not push. Never use bare `git stash`, because the stash stack is shared.
- Position codes are compared `StringComparison.Ordinal`. Treat blank strings like null.
- Comments: short *why* comments in the surrounding style.

## Review Focus

1. **The data has no observation-derived secondary.** An existing (admin) secondary must survive the live sync and the backfill. Tested in Tasks 1 and 2.
2. **The data's primary equals the old secondary.** The old primary moves to secondary, so nothing is lost. Tested in Task 1.
3. **The backfill dry-run diff must report what a real run would write** (the merged values), not the raw computed ones. Tested in Task 2.
4. **The no-change short-circuit:** the aggregator still skips the write when the merged result equals the stored values. Tested in Task 2.
5. **A club transfer must keep both positions** on the new row. Tested in Task 3.

---

### Task 1: `PositionMerge` rule

**Files:** Create `Ez.Handball.Ingestion/Parsing/PositionMerge.cs`. Test: `Ez.Handball.Tests/Ingestion/Parsing/PositionMergeTests.cs`.

**Interface produced:**
```csharp
public static (string Primary, string Secondary) Apply(
    string? existingPrimary, string? existingSecondary, string computedPrimary, string? computedSecondary)
```

- [ ] **Step 1: failing tests.** A `[Theory]` over every row of the examples table, plus:
  - (a) computed secondary equal to the computed primary → treated as none (falls through to existing);
  - (b) existing secondary equal to the new primary and existing primary blank → `""`;
  - (c) whitespace existing values are treated as blank.

  Follow `PositionModeCalculatorTests.cs` for style, namespace and `using Xunit;`.
- [ ] **Step 2:** run `--filter "FullyQualifiedName~PositionMergeTests"`; expect a compile failure.
- [ ] **Step 3: implement.**
```csharp
namespace Ez.Handball.Ingestion.Parsing;

// Backend#167: observation data may correct a player's positions but must never clear one — an
// admin-given secondary the player hasn't played yet would otherwise vanish on the next sync.
public static class PositionMerge
{
    public static (string Primary, string Secondary) Apply(
        string? existingPrimary, string? existingSecondary, string computedPrimary, string? computedSecondary)
    {
        var secondary = new[] { computedSecondary, existingSecondary, existingPrimary }
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)
                && !string.Equals(code, computedPrimary, StringComparison.Ordinal));
        return (computedPrimary, secondary ?? string.Empty);
    }
}
```
- [ ] **Step 4:** run the tests; they pass.
- [ ] **Step 5:** commit `feat(ingestion): add never-clear position merge rule`.

### Task 2: Aggregator and backfill use the merge

**Files:** Modify `Ez.Handball.Ingestion/Parsing/HbStatzPlayerPositionAggregator.cs` and `Ez.Handball.Ingestion/Functions/BackfillPlayerPositionsFunction.cs`. Test: `Ez.Handball.Tests/Ingestion/Parsing/HbStatzPlayerPositionAggregatorTests.cs` and `Ez.Handball.Tests/Ingestion/Functions/BackfillPlayerPositionsFunctionTests.cs`.

**Consumes:** `PositionMerge.Apply` (Task 1).

- [ ] **Step 1: failing tests.**
  - Aggregator:
    - (a) the stored player is CB/LB and the observations all say CB → the player is **not rewritten** (merged CB/LB equals the stored values), or, if the test style asserts the upsert, it is upserted with CB/LB;
    - (b) the stored player is CB/LB and the observations all say LB → upserted with LB/CB;
    - (c) existing tests still pass. Where an existing test expected a secondary to be cleared to `""`, re-read it: if it encoded clearing, update it to the never-clear rule and say so in the report.
  - Backfill:
    - (d) the stored player is CB/LB and the observations say CB only → no change reported, nothing written;
    - (e) the stored player is CB/"" and the observations say LB only → the reported change and the written row are both LB/CB;
    - (f) dry run: the reported `BackfillPositionResult` shows the merged values.
- [ ] **Step 2:** run `--filter "FullyQualifiedName~HbStatzPlayerPositionAggregatorTests|FullyQualifiedName~BackfillPlayerPositionsFunctionTests"`; expect the new tests to fail.
- [ ] **Step 3: implement.**
  - Aggregator: after `PositionModeCalculator.Compute(...)` and loading `player`:
    ```csharp
    var (primary, secondary) = PositionMerge.Apply(player.Position, player.PositionSecondary, computedPrimary, computedSecondary);
    if (player.Position == primary && player.PositionSecondary == secondary) return;
    ```
    Then write `primary`/`secondary`. Rename the `Compute` result locals to `computedPrimary`/`computedSecondary`. Update the class comment ("recomputes" → "recomputes and merges without clearing, #167").
  - Backfill: compute `(primary, newSecondary)` with `PositionMerge.Apply(player.Position, player.PositionSecondary, computedPrimary, computedSecondary)` and use it for both the `changed` check/report and the write.
- [ ] **Step 4:** run the filter; everything passes.
- [ ] **Step 5:** commit `fix(ingestion): stop position sync from clearing secondary positions`.

### Task 3: Parser keeps the secondary on a club transfer; docs; full verification

**Files:** Modify `Ez.Handball.Ingestion/Parsing/PlayerParser.cs` (the `isNewestMatch` block, ~lines 98–121) and `CLAUDE.md`. Test: `Ez.Handball.Tests/Functions/PlayerParserTests.cs`.

- [ ] **Step 1: failing test.** A player exists under club A's partition with Position CB and PositionSecondary LB. A newest match places them in club B. Assert the `Players` upsert for club B's partition carries `Position = "CB"` and `PositionSecondary = "LB"`. Find how the existing transfer tests ("inherits a previously-known position") set this up and capture the upserted entity; mirror that.
- [ ] **Step 2:** run `--filter "FullyQualifiedName~PlayerParserTests"`; the new test fails (`PositionSecondary` is null).
- [ ] **Step 3: implement.** In the entity written, add `PositionSecondary = existingPlayer?.PositionSecondary` with a short comment: "carried across a transfer so the new row doesn't lose it (#167)". Keep `TableUpdateMode.Merge`.
  - On a same-partition write with no existing secondary, this sets null under Merge. Merge **skips** null properties (Azure Tables Merge ignores nulls in the SDK's entity serialization), so it won't clear a stored value. Verify that claim against the existing tests or the SDK behaviour. If it's wrong, only set the property when the value is non-blank.
- [ ] **Step 4: CLAUDE.md.** Append to the "Secondary positions (Backend#108)" bullet:
  "HBStatz data may correct positions but never clears one (`PositionMerge`, Backend#167): when the observations show no secondary, the existing secondary — or the old primary if the primary moved — is kept, so an admin-set secondary (`POST /api/admin/players/{id}/position`) survives syncs and backfills. Club transfers carry both positions to the new `Players` row."
- [ ] **Step 5: full verification.** Run `dotnet build Ez.Handball.sln` (no warnings in touched files). Then run the full `dotnet test`, after the Azurite check in Global Constraints. Report totals and failing names verbatim. Known pre-existing flakes: `DebugReplayEndpointTests` and `AuthEndpointTests` with an empty storage connection string. If those fail, re-run them in isolation once and report both results.
- [ ] **Step 6:** commit `fix(ingestion): keep secondary position across club transfers`.
