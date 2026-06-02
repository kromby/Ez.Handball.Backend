using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetClubsUseCase
{
    Task<IReadOnlyList<Club>> ExecuteAsync(CancellationToken ct);
}

public class GetClubsUseCase : IGetClubsUseCase
{
    private readonly IClubRepository _repo;

    public GetClubsUseCase(IClubRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Club>> ExecuteAsync(CancellationToken ct) => _repo.ListAsync(ct);
}
