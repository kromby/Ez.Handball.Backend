namespace Ez.Handball.Domain;

public sealed record PlayerValueComponent(
    string Key,
    double Value,
    double Weight,
    double Contribution);

public sealed record PlayerValue(
    string PlayerId,
    string Flavor,   // lowercased flavor name, e.g. "fantasy"
    double Value,
    IReadOnlyList<PlayerValueComponent> Components,
    string Version);
