using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ISalaryRuleSetRepository
{
    // The typed price rule set, or null if the version does not exist / is incomplete.
    Task<SalaryRuleSet?> GetAsync(int version, CancellationToken ct);
}
