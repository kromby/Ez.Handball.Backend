using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetSquadConstraintsResult
{
    public sealed record RuleSetNotFound : GetSquadConstraintsResult
    {
        public static readonly RuleSetNotFound Instance = new();
    }
    public sealed record Found(SquadConstraints Constraints) : GetSquadConstraintsResult;
}

public interface IGetSquadConstraintsUseCase
{
    Task<GetSquadConstraintsResult> ExecuteAsync(int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetSquadConstraintsUseCase : IGetSquadConstraintsUseCase
{
    private const int DefaultVersion = 1;

    private readonly ISquadConstraintsRepository _repo;

    public GetSquadConstraintsUseCase(ISquadConstraintsRepository repo) => _repo = repo;

    public async Task<GetSquadConstraintsResult> ExecuteAsync(int? ruleSetVersion, CancellationToken ct)
    {
        var version = ruleSetVersion ?? DefaultVersion;
        var constraints = await _repo.GetAsync(version, ct);
        return constraints is null
            ? GetSquadConstraintsResult.RuleSetNotFound.Instance
            : new GetSquadConstraintsResult.Found(constraints);
    }
}
