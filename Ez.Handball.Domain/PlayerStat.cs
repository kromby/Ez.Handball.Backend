namespace Ez.Handball.Domain;

public sealed record PlayerStat(
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    string TeamId,
    string? ClubName,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
