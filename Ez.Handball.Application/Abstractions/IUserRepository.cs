using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.Abstractions;

public interface IUserRepository
{
    Task<UserEntity?> GetByIdAsync(string userId, CancellationToken ct);
    Task<UserEntity?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);
    /// <summary>Inserts the email→userId index row. Returns false if the email is already taken.</summary>
    Task<bool> TryReserveEmailAsync(string normalizedEmail, string userId, CancellationToken ct);
    Task AddAsync(UserEntity user, CancellationToken ct);
    Task UpdateAsync(UserEntity user, CancellationToken ct);
}
