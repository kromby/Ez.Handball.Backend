using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Ingestion.Parsing;

// Records one (player, match) position observation and immediately recomputes that player's
// Position/PositionSecondary from their full observation history. Called once per reconciled,
// position-mapped HBStatz player line by TriggerHbStatzSyncFunction's live sync path.
public class HbStatzPlayerPositionAggregator : IHbStatzPlayerPositionAggregator
{
    private readonly ITableWriter _tableWriter;

    public HbStatzPlayerPositionAggregator(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    public async Task RecordAndRecomputeAsync(
        string playerId, string matchId, DateTimeOffset matchDate, string positionCode, CancellationToken ct = default)
    {
        await _tableWriter.UpsertAsync("PlayerPositionObservations", new PlayerPositionObservationEntity
        {
            PartitionKey = playerId,
            RowKey = matchId,
            Position = positionCode,
            MatchDate = matchDate
        }, ct);

        var observations = await _tableWriter.QueryAsync<PlayerPositionObservationEntity>(
            "PlayerPositionObservations", $"PartitionKey eq '{Escape(playerId)}'", ct);

        var (primary, secondary) = PositionModeCalculator.Compute(
            observations.Select(o => (o.Position, o.MatchDate)).ToList());

        var players = await _tableWriter.QueryAsync<PlayerEntity>(
            "Players", $"RowKey eq '{Escape(playerId)}'", ct);
        var player = players.FirstOrDefault();
        if (player is null) return;

        var newSecondary = secondary ?? string.Empty;
        if (player.Position == primary && player.PositionSecondary == newSecondary) return;

        player.Position = primary;
        player.PositionSecondary = newSecondary;
        await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
