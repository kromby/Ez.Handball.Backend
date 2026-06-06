namespace Ez.Handball.Domain;

public sealed record AggregatedStats(
    int Games,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
