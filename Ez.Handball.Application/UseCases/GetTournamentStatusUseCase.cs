using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetTournamentStatusUseCase
{
    Task<IReadOnlyList<TournamentStatus>> ExecuteAsync(CancellationToken ct);
}

public class GetTournamentStatusUseCase : IGetTournamentStatusUseCase
{
    private readonly ITournamentRepository _repo;

    public GetTournamentStatusUseCase(ITournamentRepository repo) => _repo = repo;

    public Task<IReadOnlyList<TournamentStatus>> ExecuteAsync(CancellationToken ct) => _repo.ListAllAsync(ct);
}
