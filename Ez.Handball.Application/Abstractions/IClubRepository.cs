namespace Ez.Handball.Application.Abstractions;

public interface IClubRepository
{
    Task<bool> ExistsAsync(string clubId, CancellationToken ct);
}
