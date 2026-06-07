using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IScoringRuleSetRepository
{
    // Returns the typed rule set, or null if the version does not exist or is incomplete.
    Task<ScoringRuleSet?> GetAsync(GameFlavor flavor, int version, CancellationToken ct);
}
