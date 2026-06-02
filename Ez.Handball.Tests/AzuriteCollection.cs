namespace Ez.Handball.Tests;

// Serializes all tests that create/delete the shared auth tables on Azurite.
// xUnit runs separate classes in parallel by default; these share fixed table names
// (Users/UserEmailIndex/RefreshTokens/EmailTokens/Clubs), and table delete is
// eventually-consistent, so concurrent create/delete races drop rows. One collection
// with parallelization disabled forces them to run sequentially.
[CollectionDefinition("Azurite", DisableParallelization = true)]
public sealed class AzuriteCollection { }
