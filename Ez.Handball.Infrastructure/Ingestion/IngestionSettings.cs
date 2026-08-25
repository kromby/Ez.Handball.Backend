namespace Ez.Handball.Infrastructure.Ingestion;

// FunctionKey is required against a deployed Function App (AuthorizationLevel.Function);
// local `func start` does not enforce it, so it's optional and simply omitted when unset.
public sealed record IngestionSettings(string BaseUrl, string? FunctionKey);
