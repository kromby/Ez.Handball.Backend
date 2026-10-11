namespace Ez.Handball.Domain;

// The per-round transfer rules (Config group "fantasy-transfers-v{Version}", #63/#168).
// FreeTransfersPerRound is granted every round; up to MaxBankedFreeTransfers unused ones carry
// into the next. Each transfer beyond the free ones costs PointsPerExtraTransfer at settlement.
// Rounds numbered below StartsFromRound are unlimited and bank nothing, so a season that is
// already under way when the rules ship doesn't hand every team free transfers for past rounds.
public sealed record TransferConfig(
    int Version,
    int FreeTransfersPerRound,
    int MaxBankedFreeTransfers,
    int PointsPerExtraTransfer,
    int StartsFromRound);
