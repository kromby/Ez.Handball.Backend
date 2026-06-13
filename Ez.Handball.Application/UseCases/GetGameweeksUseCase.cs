using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetGameweeksResult
{
    public sealed record ConfigMissing : GetGameweeksResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : GetGameweeksResult { public static readonly NotFound Instance = new(); }
    public sealed record Found(IReadOnlyList<Gameweek> Gameweeks) : GetGameweeksResult;
}

public interface IGetGameweeksUseCase
{
    Task<GetGameweeksResult> ExecuteAsync(int? configVersion, CancellationToken ct);
}

public sealed class GetGameweeksUseCase : IGetGameweeksUseCase
{
    private const int DefaultVersion = 1;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;

    public GetGameweeksUseCase(IGameweekConfigRepository config, IGameweekCalendarService calendar)
    {
        _config = config;
        _calendar = calendar;
    }

    public async Task<GetGameweeksResult> ExecuteAsync(int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return GetGameweeksResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return GetGameweeksResult.NotFound.Instance;

        return new GetGameweeksResult.Found(calendar);
    }
}

public abstract record GetCurrentGameweekResult
{
    public sealed record ConfigMissing : GetCurrentGameweekResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : GetCurrentGameweekResult { public static readonly NotFound Instance = new(); }
    // Current is null only if every gameweek has passed its deadline; LastSettled is null pre-season.
    public sealed record Found(Gameweek? Current, Gameweek? LastSettled) : GetCurrentGameweekResult;
}

public interface IGetCurrentGameweekUseCase
{
    Task<GetCurrentGameweekResult> ExecuteAsync(int? configVersion, CancellationToken ct);
}

public sealed class GetCurrentGameweekUseCase : IGetCurrentGameweekUseCase
{
    private const int DefaultVersion = 1;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;

    public GetCurrentGameweekUseCase(IGameweekConfigRepository config, IGameweekCalendarService calendar)
    {
        _config = config;
        _calendar = calendar;
    }

    public async Task<GetCurrentGameweekResult> ExecuteAsync(int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return GetCurrentGameweekResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return GetCurrentGameweekResult.NotFound.Instance;

        // Editable gameweek = earliest still Open (deadline not passed).
        var current = calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open);
        var lastSettled = calendar.LastOrDefault(g => g.Status == GameweekStatus.Settled);
        return new GetCurrentGameweekResult.Found(current, lastSettled);
    }
}
