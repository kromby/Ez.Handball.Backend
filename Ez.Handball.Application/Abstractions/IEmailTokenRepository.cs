using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.Abstractions;

public interface IEmailTokenRepository
{
    Task AddAsync(EmailTokenEntity token, CancellationToken ct);
    Task<EmailTokenEntity?> GetAsync(string purpose, string tokenHash, CancellationToken ct);
    Task DeleteAsync(string purpose, string tokenHash, CancellationToken ct);
}
