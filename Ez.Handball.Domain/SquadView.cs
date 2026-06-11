namespace Ez.Handball.Domain;

// Enriched owned player returned by the squad read. Enrichment fields (Name/Club/
// Position/Gender) are null when the player can't be resolved this season. Price is the
// current market price (#52); PricePaid is the price locked when the player was bought.
public sealed record SquadPlayer(
    string PlayerId,
    string? Name,
    string? ClubId,
    string? ClubName,
    string? Position,
    string? Gender,
    PlayerPrice Price,
    PlayerPrice PricePaid,
    double Rating);   // current-season fantasy rating (#52); 0 = below min-games guard

public sealed record SquadView(
    IReadOnlyList<SquadPlayer> Players,
    PlayerPrice BudgetUsed,        // sum of each owned player's PricePaid (what the manager paid for the current squad)
    PlayerPrice RemainingBudget,   // stored cash balance (Squad.Budget); once sell-on fees apply this no longer equals StartingCap − BudgetUsed — the stored value is authoritative
    PlayerPrice SquadValue);       // sum of current market prices
