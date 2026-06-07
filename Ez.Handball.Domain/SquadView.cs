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
    PlayerCost Price,
    PlayerCost PricePaid);

public sealed record SquadView(
    IReadOnlyList<SquadPlayer> Players,
    PlayerCost BudgetUsed,        // sum of PricePaid
    PlayerCost RemainingBudget,   // StartingCap - BudgetUsed (== Squad.Budget)
    PlayerCost SquadValue);       // sum of current prices
