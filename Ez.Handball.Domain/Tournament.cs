namespace Ez.Handball.Domain;

public sealed record Tournament(
    string TournamentId,
    string Name,
    string Gender,
    TournamentType Type,
    string CompetitionId,
    string CompetitionName);
