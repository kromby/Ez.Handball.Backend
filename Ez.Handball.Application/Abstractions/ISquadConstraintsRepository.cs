using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ISquadConstraintsRepository
{
    // The typed constraints for a version, or null if the group is missing / incomplete.
    Task<SquadConstraints?> GetAsync(int version, CancellationToken ct);
}
