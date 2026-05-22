namespace Ez.Handball.Api.Models;

public record PlayerStatRow(
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
