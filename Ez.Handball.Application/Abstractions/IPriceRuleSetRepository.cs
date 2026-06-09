using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPriceRuleSetRepository
{
    // The typed price rule set, or null if the version does not exist / is incomplete.
    Task<PriceRuleSet?> GetAsync(int version, CancellationToken ct);
}
