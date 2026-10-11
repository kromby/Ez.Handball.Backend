namespace Ez.Handball.Domain;

// Enriched owned player returned by the squad read. Enrichment fields (Name/Club/
// Position/Gender) are null when the player can't be resolved this season. Price is the
// current market price (#52); PricePaid is the price locked when the player was bought.
// PlayAs is the manager's pin (#166) for a dual-position player; EligiblePositions honours it.
public sealed record SquadPlayer(
    string PlayerId,
    string? Name,
    string? ClubId,
    string? ClubName,
    string? Position,
    string? Gender,
    PlayerPrice Price,
    PlayerPrice PricePaid,
    double Rating,   // current-season fantasy rating (#52); 0 = below min-games guard
    string? PositionSecondary = null,
    string? AssignedPosition = null,   // the slot this player fills under the squad caps (#108)
    string? PlayAs = null)             // manager's position pin (#166); ignored when no longer a natural position
{
    public IReadOnlyList<string> NaturalPositions => PositionAssignment.EligibleFor(Position, PositionSecondary);

    // A pin that no longer matches a natural position (e.g. the player's positions changed) is stale and ignored.
    public string? ValidPlayAs => !string.IsNullOrWhiteSpace(PlayAs) && NaturalPositions.Contains(PlayAs, StringComparer.Ordinal) ? PlayAs : null;

    // A valid pin narrows eligibility to that one position, so the solver, validator and views honour it.
    public IReadOnlyList<string> EligiblePositions => ValidPlayAs is { } pin ? new[] { pin } : NaturalPositions;
}

public sealed record SquadView(
    IReadOnlyList<SquadPlayer> Players,
    PlayerPrice BudgetUsed,        // sum of each owned player's PricePaid (what the manager paid for the current squad)
    PlayerPrice RemainingBudget,   // stored cash balance (Squad.Budget); once sell-on fees apply this no longer equals StartingCap − BudgetUsed — the stored value is authoritative
    PlayerPrice SquadValue);       // sum of current market prices
