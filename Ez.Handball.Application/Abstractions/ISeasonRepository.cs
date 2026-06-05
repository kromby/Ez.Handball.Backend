using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ISeasonRepository
{
    Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct);
}
