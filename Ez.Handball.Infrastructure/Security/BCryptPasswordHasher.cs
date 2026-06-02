using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Infrastructure.Security;

internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    // Computed once at startup; same work factor as real hashes so the verify cost matches.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password", WorkFactor);

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false; // stored hash malformed → treat as non-match, never throw
        }
    }

    public bool VerifyDummy(string password) => Verify(password, DummyHash);
}
