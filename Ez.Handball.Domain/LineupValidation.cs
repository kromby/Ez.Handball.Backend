namespace Ez.Handball.Domain;

public sealed record LineupViolation(string Code, string Message);

public sealed record LineupValidation(
    bool IsValid,
    IReadOnlyList<LineupViolation> Violations);
