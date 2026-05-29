namespace Ez.Handball.Domain;

public sealed record MatchPlayerLine(
    string PlayerId,
    string? Name,
    string? JerseyNumber,
    string? Position,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
