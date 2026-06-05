using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetSeasonsUseCase
{
    Task<IReadOnlyList<Season>> ExecuteAsync(CancellationToken ct);
}

public class GetSeasonsUseCase : IGetSeasonsUseCase
{
    private readonly ISeasonRepository _repo;

    public GetSeasonsUseCase(ISeasonRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Season>> ExecuteAsync(CancellationToken ct) => _repo.ListAsync(ct);
}
