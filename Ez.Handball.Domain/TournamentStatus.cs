namespace Ez.Handball.Domain;

// Admin-only view of a Tournaments row: every field on the entity, across every
// season, so an operator can see what is Active/Ingest without opening table storage.
public sealed record TournamentStatus(
    string TournamentId,
    string Name,
    string Gender,
    TournamentType Type,
    string CompetitionId,
    string CompetitionName,
    string Season,
    bool Active,
    bool Ingest,
    bool IngestHbStatz,
    int Priority);
