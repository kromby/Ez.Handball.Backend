using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetClubResult
{
    public sealed record NotFound : GetClubResult;
    public sealed record Found(Club Club) : GetClubResult;
}

public interface IGetClubUseCase
{
    Task<GetClubResult> ExecuteAsync(string clubId, CancellationToken ct);
}

public sealed class GetClubUseCase : IGetClubUseCase
{
    private readonly IClubRepository _clubs;

    public GetClubUseCase(IClubRepository clubs) => _clubs = clubs;

    public async Task<GetClubResult> ExecuteAsync(string clubId, CancellationToken ct)
    {
        var club = await _clubs.GetByIdAsync(clubId, ct);
        return club is null ? new GetClubResult.NotFound() : new GetClubResult.Found(club);
    }
}
