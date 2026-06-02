namespace Ez.Handball.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);

    /// <summary>Verifies the password against a fixed internal dummy hash of the same cost,
    /// to equalize timing on the user-not-found path (defeats user enumeration). Always returns false.</summary>
    bool VerifyDummy(string password);
}
