namespace Ez.Handball.Tests.TestSupport;

// Minimal fake TimeProvider for unit tests — returns a fixed instant. Avoids a dependency on
// Microsoft.Extensions.TimeProvider.Testing for the tiny surface we use.
internal sealed class StubTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public StubTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}
