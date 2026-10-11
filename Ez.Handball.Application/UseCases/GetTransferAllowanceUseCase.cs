using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetTransferAllowanceResult
{
    public sealed record NoTeam : GetTransferAllowanceResult { public static readonly NoTeam Instance = new(); }
    // No gameweek/transfer config, unknown round, or (when no round is given) no Open round left.
    public sealed record NotAvailable : GetTransferAllowanceResult { public static readonly NotAvailable Instance = new(); }
    public sealed record Found(TransferAllowance Allowance) : GetTransferAllowanceResult;
}

public interface IGetTransferAllowanceUseCase
{
    // Fantasy-only. roundLabel null = the round transfers currently apply to (earliest Open one,
    // as IGameweekSnapshotGuard reports it); a label = that round, e.g. for settlement (#171).
    Task<GetTransferAllowanceResult> ExecuteAsync(string userId, string? roundLabel, CancellationToken ct);
}

public sealed class GetTransferAllowanceUseCase : IGetTransferAllowanceUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameweekConfigRepository _gameweekConfig;
    private readonly IGameweekCalendarService _calendar;
    private readonly ITransferConfigRepository _transferConfig;
    private readonly ITransferCountRepository _counts;

    public GetTransferAllowanceUseCase(
        IGameTeamRepository teams, IGameweekConfigRepository gameweekConfig, IGameweekCalendarService calendar,
        ITransferConfigRepository transferConfig, ITransferCountRepository counts)
    {
        _teams = teams;
        _gameweekConfig = gameweekConfig;
        _calendar = calendar;
        _transferConfig = transferConfig;
        _counts = counts;
    }

    public async Task<GetTransferAllowanceResult> ExecuteAsync(string userId, string? roundLabel, CancellationToken ct)
    {
        var team = await _teams.GetAsync(userId, GameFlavor.Fantasy, ct);
        if (team is null) return GetTransferAllowanceResult.NoTeam.Instance;

        var gameweekConfig = await _gameweekConfig.GetAsync(DefaultVersion, ct);
        if (gameweekConfig is null) return GetTransferAllowanceResult.NotAvailable.Instance;

        var transferConfig = await _transferConfig.GetAsync(DefaultVersion, ct);
        if (transferConfig is null) return GetTransferAllowanceResult.NotAvailable.Instance;

        var calendar = await _calendar.GetCalendarAsync(gameweekConfig, ct);
        if (calendar is null) return GetTransferAllowanceResult.NotAvailable.Instance;

        var target = roundLabel ?? calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open)?.RoundLabel;
        if (target is null) return GetTransferAllowanceResult.NotAvailable.Instance;

        // Same key buy/sell write counts under.
        var counts = await _counts.ListAsync(GameTeamId.For(userId, GameFlavor.Fantasy), ct);

        // Wildcard rounds come from chip state once it exists (#173); until then there are none.
        var allowance = TransferAllowanceCalculator.Compute(
            transferConfig, calendar, counts, team.CreatedAt, target, wildcardRounds: null);

        return allowance is null
            ? GetTransferAllowanceResult.NotAvailable.Instance
            : new GetTransferAllowanceResult.Found(allowance);
    }
}
