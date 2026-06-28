namespace HbStatz.Spike;

public sealed record PlayerStatLine(
    string Side,
    int? Jersey,
    string Name,
    bool IsGoalkeeper,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
