namespace Ez.Handball.Domain;

public sealed record AggregatedStats(
    int Games,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    int Assists = 0,
    int Steals = 0,
    int Blocks = 0,
    int Saves = 0);
