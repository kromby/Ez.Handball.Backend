namespace Ez.Handball.Domain;

public sealed record MatchPlayerLine(
    string PlayerId,
    string? Name,
    string? JerseyNumber,
    string? Position,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    // HBStatz enrichment: null until HBStatz has reported the match.
    int? HbStatzAssists = null,
    int? HbStatzSteals = null,
    int? HbStatzBlocks = null,
    int? HbStatzSaves = null,
    // Fantasy points for this game, same scoring as the player's per-match stats; null without a rule set.
    double? Points = null);
