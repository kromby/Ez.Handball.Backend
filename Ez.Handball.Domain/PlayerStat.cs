namespace Ez.Handball.Domain;

public sealed record PlayerStat(
    string PlayerId,
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    string TeamId,
    string? ClubName,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    int? HbStatzAssists = null,
    int? HbStatzSteals = null,
    int? HbStatzBlocks = null,
    int? HbStatzSaves = null);
