namespace Ez.Handball.Domain;

public sealed record MiniLeagueMember(
    string UserId,
    string Role,
    DateTimeOffset JoinedAt);
